using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Abstractions;
using PmTool.Knowledge;

namespace PmTool.Ai.Actions.Knowledge;

/// <summary>
/// 把工具收到的參數物件讀成 <see cref="KnowledgeChange"/>：只看形狀（型別、必填、不認得的屬性、字元），
/// 牽涉現況（空間、頁面、權限）的檢查在 <see cref="KnowledgePlanner"/>。每個問題一個細項，一次回齊。
/// </summary>
internal static partial class KnowledgeChangeReader
{
    private static readonly string[] PayloadProperties = ["changes", "reason"];

    public static IReadOnlyList<KnowledgeChange?> Read(JsonElement payload, List<FailureDetail> issues)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Issue("", "參數要是物件，裡面有 changes 陣列。"));
            return [];
        }

        foreach (var property in payload.EnumerateObject().Where(p => !PayloadProperties.Contains(p.Name)))
        {
            issues.Add(Issue(property.Name, $"不認得的參數：{property.Name}（只有 changes 與 reason）。"));
        }

        if (!payload.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
        {
            issues.Add(Issue("changes", "缺少 changes，它要是一個陣列。"));
            return [];
        }

        var count = changes.GetArrayLength();
        if (count is 0 or > AiRules.MaxActionSteps)
        {
            issues.Add(Issue("changes", $"一份建議要有 1 到 {AiRules.MaxActionSteps} 個動作，目前有 {count} 個。"));
            return [];
        }

        var result = new List<KnowledgeChange?>(count);
        var index = 0;
        foreach (var element in changes.EnumerateArray())
        {
            result.Add(ReadOne(element, $"changes[{index++}]", issues));
        }

        return result;
    }

    private static KnowledgeChange? ReadOne(JsonElement element, string path, List<FailureDetail> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Issue(path, "這一項要是物件。"));
            return null;
        }

        var action = element.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (action is null || !KnowledgeActions.Properties.TryGetValue(action, out var allowed))
        {
            issues.Add(Issue($"{path}.action", $"action 要是 {string.Join("、", KnowledgeActions.Properties.Keys)} 其中之一。"));
            return null;
        }

        var before = issues.Count;
        foreach (var property in element.EnumerateObject().Where(p => !allowed.Contains(p.Name)))
        {
            issues.Add(Issue($"{path}.{property.Name}", $"{action} 不收屬性 {property.Name}（只有 {string.Join("、", allowed.Where(n => n != "action"))}）。"));
        }

        var reader = new FieldReader(element, path, issues);
        var change = new KnowledgeChange(action)
        {
            SpaceKey = reader.SpaceKey("spaceKey", required: action is not KnowledgeActions.CreateSpace),
            Key = reader.SpaceKey("key", required: action is KnowledgeActions.CreateSpace),
            Number = reader.Int("number", required: action is not (KnowledgeActions.CreatePage or KnowledgeActions.CreateSpace or KnowledgeActions.UpdateSpace or KnowledgeActions.ArchiveSpace or KnowledgeActions.UnarchiveSpace)),
            ParentNumber = reader.Int("parentNumber", required: false),
            BeforeNumber = reader.Int("beforeNumber", required: false),
            Ref = reader.Ref("ref"),
            ParentRef = reader.Ref("parentRef"),
            Title = reader.Line("title", KnowledgeRules.MaxPageTitleLength, required: action is KnowledgeActions.CreatePage),
            Body = reader.Body("body"),
            ExpectedRevision = reader.Long("expectedRevision", required: action is KnowledgeActions.UpdatePage),
            Name = reader.Line("name", KnowledgeRules.MaxSpaceNameLength,required: action is KnowledgeActions.CreateSpace or KnowledgeActions.UpdateSpace),
            Description = reader.Multiline("description", KnowledgeRules.MaxSpaceDescriptionLength),
            ProjectKey = reader.Plain("projectKey"),
            People = action == KnowledgeActions.SetRestriction ? reader.People("people") : null,
        };

        if (action == KnowledgeActions.UpdatePage && change.Title is null && change.Body is null && issues.Count == before)
        {
            issues.Add(Issue(path, "update_page 至少要給 title 或 body 其中一個。"));
        }

        return issues.Count == before ? change : null;
    }

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    [GeneratedRegex("^[a-z0-9_-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex RefPattern();

    [GeneratedRegex(KnowledgeRules.SpaceKeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SpaceKeyPattern();

    private readonly struct FieldReader(JsonElement element, string path, List<FailureDetail> issues)
    {
        private bool Has(string name, out JsonElement value) =>
            element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;

        private void Bad(string name, string message) => issues.Add(Issue($"{path}.{name}", message));

        private string? Text(string name, bool required)
        {
            if (!Has(name, out var value))
            {
                if (required)
                {
                    Bad(name, $"缺少 {name}。");
                }

                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            Bad(name, $"{name} 要是字串。");
            return null;
        }

        public string? Plain(string name)
        {
            var text = Text(name, required: false)?.Trim();
            if (text is not null && (text.Length == 0 || text.Any(char.IsControl)))
            {
                Bad(name, $"{name} 不能是空的，也不能含有 NUL 或其他控制字元。");
                return null;
            }

            return text;
        }

        public string? SpaceKey(string name, bool required)
        {
            var text = Text(name, required)?.Trim().ToUpperInvariant();
            if (text is not null && !SpaceKeyPattern().IsMatch(text))
            {
                Bad(name, "空間代號是大寫英文字母開頭、只有大寫字母與數字、2 到 10 個字元（例如 HR）。");
                return null;
            }

            return text;
        }

        public string? Ref(string name)
        {
            var text = Text(name, required: false)?.Trim();
            if (text is not null && !RefPattern().IsMatch(text))
            {
                Bad(name, $"{name} 只能用小寫英文字母、數字、底線與連字號，最長 20 個字元。");
                return null;
            }

            return text;
        }

        public string? Line(string name, int max, bool required)
        {
            var text = Text(name, required)?.Trim();
            if (text is null)
            {
                return null;
            }

            if (text.Length == 0 || text.Length > max || text.Any(char.IsControl))
            {
                Bad(name, $"{name} 要有 1 到 {max} 個字，不能換行，也不能含有 NUL 或其他控制字元。");
                return null;
            }

            return text;
        }

        /// <summary>簡介：可以換行，但不能有 NUL 與其他控制字元；整段空白當作沒有。</summary>
        public string? Multiline(string name, int max)
        {
            var text = Text(name, required: false);
            if (text is null)
            {
                return null;
            }

            if (text.Length > max || HasForbiddenControl(text))
            {
                Bad(name, $"{name} 最多 {max} 個字，不能含有 NUL 或其他控制字元。");
                return null;
            }

            return text.Trim();
        }

        /// <summary>頁面內文：Markdown，可以是空字串（清空）；內文的換行統一成 \n，其他控制字元擋下。</summary>
        public string? Body(string name)
        {
            var text = Text(name, required: false);
            if (text is null)
            {
                return null;
            }

            if (text.Length > KnowledgeRules.MaxPageBodyLength || HasForbiddenControl(text))
            {
                Bad(name, $"內文最多 {KnowledgeRules.MaxPageBodyLength} 個字，不能含有 NUL 或其他控制字元。");
                return null;
            }

            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public int? Int(string name, bool required)
        {
            if (!Has(name, out var value))
            {
                if (required)
                {
                    Bad(name, $"缺少 {name}。");
                }

                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 1)
            {
                return number;
            }

            Bad(name, $"{name} 要是 1 以上的整數。");
            return null;
        }

        public long? Long(string name, bool required)
        {
            if (!Has(name, out var value))
            {
                if (required)
                {
                    Bad(name, $"缺少 {name}（用 get_page 讀到的 revision）。");
                }

                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 1)
            {
                return number;
            }

            Bad(name, $"{name} 要是 1 以上的整數。");
            return null;
        }

        /// <summary>人員編號的陣列；不給、null、空陣列都是「拿掉限制」。</summary>
        public IReadOnlyList<Guid>? People(string name)
        {
            if (!Has(name, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                Bad(name, $"{name} 要是人員編號的陣列（空陣列是拿掉限制）。");
                return null;
            }

            var people = new List<Guid>();
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
                {
                    if (!people.Contains(id))
                    {
                        people.Add(id);
                    }
                }
                else
                {
                    Bad($"{name}[{index}]", "要是人員編號（list_people 回的 id）。");
                }

                index++;
            }

            return people;
        }

        private static bool HasForbiddenControl(string text) => text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    }
}
