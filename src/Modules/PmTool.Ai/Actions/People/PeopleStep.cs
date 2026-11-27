using System.Text.Json;
using PmTool.Identity;

namespace PmTool.Ai.Actions.People;

/// <summary>
/// 一步人員管理的整理結果，原樣存進建議的 State。<b>沒有密碼</b>：新增人員的初始密碼是卡片上的輸入，
/// 只在套用的那一刻從 inputs 讀。
/// </summary>
internal sealed record PeopleStep
{
    public const string Create = "create";
    public const string SetRole = "set_role";
    public const string SetProfile = "set_profile";
    public const string Disable = "disable";
    public const string Enable = "enable";

    public required string Action { get; init; }

    public Guid? Person { get; init; }

    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public string? Role { get; init; }

    /// <summary>set_profile：有給才改（null 是維持現在的；空字串是清掉）。</summary>
    public string? JobTitle { get; init; }

    public string? Responsibilities { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static JsonElement Write(IReadOnlyList<PeopleStep> steps) =>
        JsonSerializer.SerializeToElement(new { steps }, Options);

    public static PeopleStep Read(JsonElement state, int index) =>
        state.GetProperty("steps")[index].Deserialize<PeopleStep>(Options)
        ?? throw new InvalidOperationException("人員建議的狀態壞了。");

    public static string RoleName(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => "owner",
        OrganizationRole.Admin => "admin",
        OrganizationRole.Guest => "guest",
        _ => "member",
    };

    public static OrganizationRole? ParseRole(string? text) => text switch
    {
        "owner" => OrganizationRole.Owner,
        "admin" => OrganizationRole.Admin,
        "member" => OrganizationRole.Member,
        "guest" => OrganizationRole.Guest,
        _ => null,
    };

    /// <summary>給人看的角色名稱（和網頁一致）。</summary>
    public static string RoleLabel(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => "擁有者",
        OrganizationRole.Admin => "管理員",
        OrganizationRole.Guest => "訪客",
        _ => "一般成員",
    };
}
