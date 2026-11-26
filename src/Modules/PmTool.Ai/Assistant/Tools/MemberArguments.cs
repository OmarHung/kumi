using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>成員相關參數的讀法（addMembers、成員建議的 changes）。型別不對照實告訴模型，不默默丟掉。</summary>
internal static class MemberArguments
{
    public static Guid? OptionalGuid(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)
            ? id
            : null;

    /// <summary>propose_new_items 的 addMembers；沒給是空的。角色沒給時是 contributor（只看得到自己負責或建立的項目，給的權限最少）。</summary>
    public static (IReadOnlyList<MemberAddition>? Value, string? Error) ReadAdditions(AIFunctionArguments arguments)
    {
        if (!arguments.TryGetValue("addMembers", out var given) || given is null or JsonElement { ValueKind: JsonValueKind.Null })
        {
            return ([], null);
        }

        if (ToolArguments.Array(arguments, "addMembers") is not { } elements)
        {
            return (null, "參數 addMembers 要是 [{person, role?}] 的陣列。");
        }

        if (elements.Count > AiRules.MaxMemberChangesPerProposal)
        {
            return (null, $"addMembers 最多 {AiRules.MaxMemberChangesPerProposal} 個，這次有 {elements.Count} 個。");
        }

        var result = new List<MemberAddition>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var path = $"addMembers[{i}]";
            var person = ReadPerson(elements[i], path);
            if (person.Error is { } error)
            {
                return (null, error);
            }

            var role = Text(elements[i], "role");
            if (elements[i].TryGetProperty("role", out var raw) && raw.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return (null, $"{path}.role 要是角色的 key（字串）。");
            }

            result.Add(new MemberAddition(person.Id, string.IsNullOrWhiteSpace(role) ? "contributor" : role.Trim()));
        }

        return (result, null);
    }

    /// <summary>成員建議的 changes：每一筆 {person, role?, remove?}，role 和 remove 剛好給一個。</summary>
    public static (IReadOnlyList<MemberChangeDraft>? Value, string? Error) ReadChanges(AIFunctionArguments arguments)
    {
        if (ToolArguments.Array(arguments, "changes") is not { } elements)
        {
            return (null, "參數 changes 要是 [{person, role?, remove?}] 的陣列。");
        }

        if (elements.Count > AiRules.MaxMemberChangesPerProposal)
        {
            return (null, $"changes 最多 {AiRules.MaxMemberChangesPerProposal} 筆，這次有 {elements.Count} 筆。");
        }

        var result = new List<MemberChangeDraft>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var path = $"changes[{i}]";
            var person = ReadPerson(elements[i], path);
            if (person.Error is { } error)
            {
                return (null, error);
            }

            var problem = ReadRole(elements[i], path, out var role);
            if (problem is not null)
            {
                return (null, problem);
            }

            result.Add(new MemberChangeDraft(person.Id, role));
        }

        return (result, null);
    }

    // role 與 remove 只能給一個：都給或都沒給，模型的意思不明，不猜。
    private static string? ReadRole(JsonElement element, string path, out string? role)
    {
        role = Text(element, "role")?.Trim();
        if (element.TryGetProperty("role", out var rawRole) && rawRole.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return $"{path}.role 要是角色的 key（字串）。";
        }

        var remove = false;
        if (element.TryGetProperty("remove", out var rawRemove) && rawRemove.ValueKind != JsonValueKind.Null)
        {
            if (rawRemove.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return $"{path}.remove 要是 true 或 false。";
            }

            remove = rawRemove.GetBoolean();
        }

        var hasRole = !string.IsNullOrEmpty(role);
        if (hasRole == remove)
        {
            return $"{path} 要給 role（加入或改角色）或 remove: true（移出專案），兩個剛好給一個。";
        }

        return null;
    }

    private static (Guid Id, string? Error) ReadPerson(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (Guid.Empty, $"{path} 要是物件。");
        }

        return Text(element, "person") is { } text && Guid.TryParse(text, out var id)
            ? (id, null)
            : (Guid.Empty, $"{path}.person 要是人員 id（list_people 或 get_project 回的 id）。");
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
