using System.Collections.Frozen;
using PmTool.Work.Blueprints;
using static PmTool.Work.WorkPermissions;

namespace PmTool.Work.Access;

/// <summary>專案角色與它們的權限。內建角色寫死在這裡，自訂角色寫在藍圖的 roles 裡。</summary>
internal static class ProjectRoles
{
    public const string Admin = "admin";
    public const string Editor = "editor";
    public const string Contributor = "contributor";
    public const string Commenter = "commenter";
    public const string Viewer = "viewer";

    /// <summary>
    /// 管理員與組織管理員的權限，依合約列出的順序；回給畫面的權限清單也照這個順序。
    /// 不含 <see cref="ItemReadOwn"/>：他們看得到全部，「只看自己的」是限縮用的權限，不是多給的。
    /// </summary>
    public static readonly IReadOnlyList<string> AllPermissions =
    [
        ItemRead, ItemCreate, ItemUpdate, ItemTransition, ItemDelete, CommentCreate,
        BlueprintRead, BlueprintPropose, BlueprintApply, ProjectManage,
    ];

    /// <summary>角色的權限整理成固定的順序用：<see cref="AllPermissions"/> 再加上只看自己的。</summary>
    private static readonly IReadOnlyList<string> OrderedPermissions =
    [
        ItemRead, ItemReadOwn, ItemCreate, ItemUpdate, ItemTransition, ItemDelete, CommentCreate,
        BlueprintRead, BlueprintPropose, BlueprintApply, ProjectManage,
    ];

    private static readonly FrozenDictionary<string, BuiltInRole> BuiltIn = new Dictionary<string, BuiltInRole>
    {
        [Admin] = new("管理員", AllPermissions),
        [Editor] = new("編輯者", [ItemRead, ItemCreate, ItemUpdate, ItemTransition, ItemDelete, CommentCreate, BlueprintRead]),
        [Contributor] = new("執行者", [ItemReadOwn, ItemCreate, ItemUpdate, ItemTransition, CommentCreate, BlueprintRead]),
        [Commenter] = new("留言者", [ItemRead, CommentCreate, BlueprintRead]),
        [Viewer] = new("檢視者", [ItemRead, BlueprintRead]),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsKnown(string role, ProjectSchema schema) =>
        BuiltIn.ContainsKey(role) || schema.Roles.ContainsKey(role);

    /// <summary>角色的權限，依固定的順序。不認得的角色（例如藍圖改版拿掉了）沒有任何權限。</summary>
    public static IReadOnlyList<string> PermissionsOf(string? role, ProjectSchema schema)
    {
        IReadOnlyList<string>? granted = role is null ? null
            : BuiltIn.TryGetValue(role, out var builtIn) ? builtIn.Permissions
            : schema.Roles.GetValueOrDefault(role)?.Permissions;
        return granted is null ? [] : [.. OrderedPermissions.Where(granted.Contains)];
    }

    /// <summary>角色讀得到項目：全部，或只有自己的。</summary>
    public static bool CanReadItems(string? role, ProjectSchema schema) =>
        PermissionsOf(role, schema).Any(permission => permission is ItemRead or ItemReadOwn);

    /// <summary>角色讀得到全部項目，不只自己的。</summary>
    public static bool ReadsAllItems(string? role, ProjectSchema schema) => PermissionsOf(role, schema).Contains(ItemRead);

    public static string DisplayName(string role, ProjectSchema schema) =>
        BuiltIn.TryGetValue(role, out var builtIn) ? builtIn.Name
        : schema.Roles.TryGetValue(role, out var custom) ? custom.Name
        : role;

    /// <summary>內建角色的 key，照權限由大到小的固定順序（給畫面與派工的角色清單用，不是依字母）。</summary>
    public static readonly IReadOnlyList<string> BuiltInKeys = [Admin, Editor, Contributor, Commenter, Viewer];

    /// <summary>內建角色在前，接著是藍圖裡的自訂角色。</summary>
    public static IEnumerable<string> All(ProjectSchema schema) =>
        BuiltIn.Keys.Order(StringComparer.Ordinal).Concat(schema.Blueprint.Roles.Select(r => r.Key));

    private sealed record BuiltInRole(string Name, IReadOnlyList<string> Permissions);
}
