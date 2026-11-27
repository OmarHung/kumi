using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.Actions.People;

/// <summary>
/// 把模型給的人員變更整理成步驟（<see cref="PeopleStep"/>）與卡片上看的文字。規則照身分模組（<see cref="IPeopleDirectory"/>、
/// <see cref="IPersonProfileService"/> 的合約註解）先擋一次，套用時身分模組會再照使用者的權限檢查。
/// 同一份裡後面的步驟看得到前面步驟之後的狀態（先停用再啟用同一個人時，「之前」是停用）。
/// </summary>
internal sealed class PeoplePlanner(Actor actor, OrganizationRole actorRole, IReadOnlyList<PersonSummary> directory)
{
    private static readonly string[] CreateProperties = ["action", "displayName", "email", "role", "jobTitle", "responsibilities"];
    private static readonly string[] RoleProperties = ["action", "person", "role"];
    private static readonly string[] ProfileProperties = ["action", "person", "jobTitle", "responsibilities"];
    private static readonly string[] PersonOnlyProperties = ["action", "person"];

    private readonly Dictionary<Guid, PersonSummary> _people = directory.ToDictionary(p => p.PersonId);
    private readonly HashSet<string> _emails = new(
        directory.Where(p => p.Email is not null).Select(p => p.Email!), StringComparer.OrdinalIgnoreCase);
    private readonly List<FailureDetail> _issues = [];
    private readonly List<string> _forbidden = [];
    private readonly List<PeopleStep> _steps = [];
    private readonly List<ActionStep> _cards = [];
    private readonly List<ActionInput> _inputs = [];

    private bool ActorIsOwner => actorRole == OrganizationRole.Owner;

    public Result<(IReadOnlyList<PeopleStep> Steps, IReadOnlyList<ActionStep> Cards, IReadOnlyList<ActionInput> Inputs)> Plan(
        IReadOnlyList<JsonElement> changes)
    {
        for (var i = 0; i < changes.Count; i++)
        {
            ReadChange(changes[i], i);
        }

        if (_forbidden.Count > 0)
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, _forbidden[0]) { Details = _issues };
        }

        return _issues.Count > 0
            ? PayloadJson.Invalid("人員建議不合規定，沒有建立。", _issues)
            : (_steps, _cards, _inputs);
    }

    private void ReadChange(JsonElement change, int index)
    {
        var path = $"changes[{index}]";
        if (change.ValueKind != JsonValueKind.Object)
        {
            _issues.Add(PayloadJson.Issue(path, "每一筆要是物件。"));
            return;
        }

        var action = PayloadJson.Text(change, "action", path, _issues, required: true);
        switch (action)
        {
            case null:
                return;
            case PeopleStep.Create:
                Create(change, path, index);
                break;
            case PeopleStep.SetRole:
                SetRole(change, path);
                break;
            case PeopleStep.SetProfile:
                SetProfile(change, path);
                break;
            case PeopleStep.Disable:
            case PeopleStep.Enable:
                SetDisabled(change, path, disable: action == PeopleStep.Disable);
                break;
            default:
                _issues.Add(PayloadJson.Issue($"{path}.action", "action 要是 create、set_role、set_profile、disable、enable 其中之一。"));
                break;
        }
    }

    private void Create(JsonElement change, string path, int index)
    {
        PayloadJson.Unknown(
            change, CreateProperties, path, _issues, "新人的初始密碼不要放進參數，卡片上會請管理員自己輸入。");
        var name = PayloadJson.Text(change, "displayName", path, _issues, required: true);
        var email = PayloadJson.Text(change, "email", path, _issues, required: true);
        var roleText = PayloadJson.Text(change, "role", path, _issues, required: true);
        var title = NullIfEmpty(ReadProfileField(change, "jobTitle", path, IdentityRules.MaxJobTitleLength));
        var scope = NullIfEmpty(ReadProfileField(change, "responsibilities", path, IdentityRules.MaxResponsibilitiesLength));
        if (name is not null)
        {
            CheckName(name, $"{path}.displayName");
        }

        var normalized = email is null ? null : CheckEmail(email, $"{path}.email");
        var role = roleText is null ? null : PeopleStep.ParseRole(roleText);
        if (roleText is not null && role is null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.role", "role 要是 owner、admin、member、guest 其中之一。"));
        }

        if (role is OrganizationRole.Owner or OrganizationRole.Admin && !ActorIsOwner)
        {
            Forbid($"{path}.role", "只有組織的擁有者可以新增擁有者或管理員。");
        }

        if (name is null || normalized is null || role is null)
        {
            return;
        }

        _steps.Add(new PeopleStep
        {
            Action = PeopleStep.Create,
            DisplayName = name,
            Email = normalized,
            Role = PeopleStep.RoleName(role.Value),
            JobTitle = title,
            Responsibilities = scope,
        });
        var after = $"Email：{normalized}\n角色：{PeopleStep.RoleLabel(role.Value)}";
        if (title is not null)
        {
            after += $"\n職稱：{title}";
        }

        if (scope is not null)
        {
            after += $"\n負責範圍：{scope}";
        }

        _cards.Add(new ActionStep("新增人員", name, null, after, null, ActionTextFormat.Text));
        _inputs.Add(new ActionInput(
            $"password:{index}", $"{name}的初始密碼", ActionInputKind.Password,
            $"至少 {IdentityRules.MinPasswordLength} 個字元；由你在這裡輸入，不會經過 AI，也不會被存下來。"));
    }

    private void SetRole(JsonElement change, string path)
    {
        PayloadJson.Unknown(change, RoleProperties, path, _issues);
        var target = FindPerson(change, path);
        var roleText = PayloadJson.Text(change, "role", path, _issues, required: true);
        var role = roleText is null ? null : PeopleStep.ParseRole(roleText);
        if (roleText is not null && role is null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.role", "role 要是 owner、admin、member、guest 其中之一。"));
        }

        if (target is null || role is null)
        {
            return;
        }

        if (target.PersonId == actor.PersonId)
        {
            _issues.Add(PayloadJson.Issue($"{path}.person", "不能改自己的角色（要換擁有者，先升別人，再由他來調整你）。"));
        }
        else if (target.Role == role.Value)
        {
            _issues.Add(PayloadJson.Issue($"{path}.role", $"{target.DisplayName} 現在就是{PeopleStep.RoleLabel(role.Value)}。"));
        }
        else if (!ActorIsOwner && (IsPrivileged(target.Role) || IsPrivileged(role.Value)))
        {
            Forbid($"{path}.role", "目前或要改成擁有者、管理員的人，只有組織的擁有者可以調整角色。");
        }
        else
        {
            _steps.Add(new PeopleStep { Action = PeopleStep.SetRole, Person = target.PersonId, Role = PeopleStep.RoleName(role.Value) });
            _cards.Add(new ActionStep(
                "改組織角色", target.DisplayName, PeopleStep.RoleLabel(target.Role), PeopleStep.RoleLabel(role.Value), null, ActionTextFormat.Text));
            _people[target.PersonId] = target with { Role = role.Value };
        }
    }

    private void SetProfile(JsonElement change, string path)
    {
        PayloadJson.Unknown(change, ProfileProperties, path, _issues);
        var target = FindPerson(change, path);
        var title = ReadProfileField(change, "jobTitle", path, IdentityRules.MaxJobTitleLength);
        var scope = ReadProfileField(change, "responsibilities", path, IdentityRules.MaxResponsibilitiesLength);
        if (title is null && scope is null && !_issues.Any(i => i.Path.StartsWith(path, StringComparison.Ordinal)))
        {
            _issues.Add(PayloadJson.Issue(path, "jobTitle 與 responsibilities 至少要給一個（空字串是清掉，不改就不要給）。"));
        }

        if (target is null)
        {
            return;
        }

        var newTitle = title is null ? target.JobTitle : NullIfEmpty(title);
        var newScope = scope is null ? target.Responsibilities : NullIfEmpty(scope);
        if (newTitle == target.JobTitle && newScope == target.Responsibilities)
        {
            if (!_issues.Any(i => i.Path.StartsWith(path, StringComparison.Ordinal)))
            {
                _issues.Add(PayloadJson.Issue(path, $"{target.DisplayName} 的職稱與負責範圍已經是這樣，不用改。"));
            }

            return;
        }

        _steps.Add(new PeopleStep { Action = PeopleStep.SetProfile, Person = target.PersonId, JobTitle = title, Responsibilities = scope });
        var (before, after) = ProfileLines(target, title is not null, newTitle, scope is not null, newScope);
        _cards.Add(new ActionStep("改職稱與負責範圍", target.DisplayName, before, after, null, ActionTextFormat.Text));
        _people[target.PersonId] = target with { JobTitle = newTitle, Responsibilities = newScope };
    }

    private void SetDisabled(JsonElement change, string path, bool disable)
    {
        PayloadJson.Unknown(change, PersonOnlyProperties, path, _issues);
        var target = FindPerson(change, path);
        if (target is null)
        {
            return;
        }

        if (target.PersonId == actor.PersonId)
        {
            _issues.Add(PayloadJson.Issue($"{path}.person", disable ? "不能停用自己的帳號。" : "不能對自己的帳號做這件事。"));
        }
        else if (target.IsDisabled == disable)
        {
            _issues.Add(PayloadJson.Issue($"{path}.person", $"{target.DisplayName} 的帳號已經是{(disable ? "停用" : "啟用")}的了。"));
        }
        else if (!ActorIsOwner && IsPrivileged(target.Role))
        {
            Forbid($"{path}.person", "擁有者與管理員的帳號，只有組織的擁有者可以停用或啟用。");
        }
        else
        {
            _steps.Add(new PeopleStep { Action = disable ? PeopleStep.Disable : PeopleStep.Enable, Person = target.PersonId });
            _cards.Add(new ActionStep(
                disable ? "停用帳號" : "啟用帳號", target.DisplayName, disable ? "啟用中" : "已停用", disable ? "已停用" : "啟用中",
                disable ? "停用後他所有的登入立刻失效；工作項目、留言與活動還是會顯示他。" : null, ActionTextFormat.Text));
            _people[target.PersonId] = target with { IsDisabled = disable };
        }
    }

    private PersonSummary? FindPerson(JsonElement change, string path)
    {
        var text = PayloadJson.Text(change, "person", path, _issues, required: true);
        if (text is null)
        {
            return null;
        }

        if (Guid.TryParse(text, out var id) && _people.TryGetValue(id, out var found))
        {
            return found;
        }

        _issues.Add(PayloadJson.Issue($"{path}.person", "找不到這個人（person 要用 list_people 回的人員編號）。"));
        return null;
    }

    private string? ReadProfileField(JsonElement change, string name, string path, int max)
    {
        var value = PayloadJson.Text(change, name, path, _issues);
        if (value is null)
        {
            return null;
        }

        if (value.Length > max)
        {
            _issues.Add(PayloadJson.Issue($"{path}.{name}", $"{name} 最多 {max} 個字，現在有 {value.Length} 個字。"));
            return null;
        }

        if (PayloadJson.HasInvisibleCharacters(value))
        {
            _issues.Add(PayloadJson.Issue($"{path}.{name}", $"{name} 不能有看不見的特殊字元。"));
            return null;
        }

        return value;
    }

    private void CheckName(string name, string path)
    {
        if (name.Length > IdentityRules.MaxDisplayNameLength)
        {
            _issues.Add(PayloadJson.Issue(path, $"顯示名稱最多 {IdentityRules.MaxDisplayNameLength} 個字。"));
        }
        else if (PayloadJson.HasInvisibleCharacters(name))
        {
            _issues.Add(PayloadJson.Issue(path, "顯示名稱不能有看不見的特殊字元。"));
        }
    }

    // 只擋明顯打錯的寫法（和身分模組同一個標準）；格式或看不見的字元的最後把關在身分模組。
    private string? CheckEmail(string email, string path)
    {
        var candidate = email.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
        var at = candidate.IndexOf('@');
        var domain = at > 0 ? candidate[(at + 1)..] : string.Empty;
        if (candidate.Length > IdentityRules.MaxEmailLength || candidate.Any(char.IsWhiteSpace) || PayloadJson.HasInvisibleCharacters(candidate)
            || at <= 0 || at != candidate.LastIndexOf('@')
            || !domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains(".."))
        {
            _issues.Add(PayloadJson.Issue(path, "Email 的格式不正確。"));
            return null;
        }

        if (!_emails.Add(candidate))
        {
            _issues.Add(PayloadJson.Issue(path, "這個 Email 已經有人用了（或在這份建議裡出現了不只一次）。"));
            return null;
        }

        return candidate;
    }

    private void Forbid(string path, string message)
    {
        _forbidden.Add(message);
        _issues.Add(new FailureDetail(AiErrorCodes.Forbidden, path, message));
    }

    private static bool IsPrivileged(OrganizationRole role) => role is OrganizationRole.Owner or OrganizationRole.Admin;

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private static (string Before, string After) ProfileLines(
        PersonSummary target, bool titleGiven, string? newTitle, bool scopeGiven, string? newScope)
    {
        const string None = "（沒有）";
        var before = new List<string>();
        var after = new List<string>();
        if (titleGiven)
        {
            before.Add($"職稱：{target.JobTitle ?? None}");
            after.Add($"職稱：{newTitle ?? None}");
        }

        if (scopeGiven)
        {
            before.Add($"負責範圍：{target.Responsibilities ?? None}");
            after.Add($"負責範圍：{newScope ?? None}");
        }

        return (string.Join('\n', before), string.Join('\n', after));
    }
}
