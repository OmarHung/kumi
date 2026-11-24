using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;

namespace PmTool.Blueprint.Planning;

/// <summary>
/// 比對兩版藍圖。資源用 key 對應：兩邊都有的比屬性，只有新版有的是新增，只有目前有的是移除。
/// 每個清單先依新藍圖的順序列出新增與修改（修改的資源後面緊跟著它底下的差異），再依目前藍圖的順序列出移除，
/// 所以結果的順序是固定的。整個資源被新增或移除時只有一筆，不再逐項列出它底下的東西。
/// </summary>
internal static class BlueprintDiffer
{
    private const string RootPath = "";

    private static readonly ResourceSpec<BlueprintMetadata> Metadata = new(
        ResourceKind.Metadata,
        "metadata",
        metadata => metadata.Key,
        [
            Value<BlueprintMetadata>("key", metadata => metadata.Key),
            Value<BlueprintMetadata>("name", metadata => metadata.Name),
            Value<BlueprintMetadata>("description", metadata => metadata.Description),
            Value<BlueprintMetadata>("basedOn", metadata => metadata.BasedOn),
        ]);

    private static readonly ResourceSpec<FieldOption> Options = new(
        ResourceKind.FieldOption,
        "options",
        option => option.Key,
        [
            Value<FieldOption>("name", option => option.Name),
            Value<FieldOption>("color", option => option.Color),
        ]);

    private static readonly ResourceSpec<FieldDefinition> Fields = new(
        ResourceKind.Field,
        "fields",
        field => field.Key,
        [
            Value<FieldDefinition>("name", field => field.Name),
            Value<FieldDefinition>("type", field => field.Type),
            Value<FieldDefinition>("description", field => field.Description),
            Value<FieldDefinition>("currency", field => field.Currency),
            Shape<FieldDefinition>("options", field => field.Options?.Select(Options.KeyOf)),
        ])
    {
        Children = (current, next, field) => DiffOptionalList(Options, current.Options, next.Options, field),
    };

    private static readonly ResourceSpec<WorkflowState> States = new(
        ResourceKind.State,
        "states",
        state => state.Key,
        [
            Value<WorkflowState>("name", state => state.Name),
            Value<WorkflowState>("category", state => state.Category),
        ]);

    // 轉換沒有 key，起點加終點就是它的身分。
    private static readonly ResourceSpec<WorkflowTransition> Transitions = new(
        ResourceKind.Transition,
        "transitions",
        transition => $"{transition.From}->{transition.To}",
        [
            Value<WorkflowTransition>("allowedRoles", transition => transition.AllowedRoles),
            Value<WorkflowTransition>("requireFields", transition => transition.RequireFields),
        ]);

    private static readonly ResourceSpec<Workflow> Workflows = new(
        ResourceKind.Workflow,
        "workflows",
        workflow => workflow.Key,
        [
            Value<Workflow>("name", workflow => workflow.Name),
            Shape<Workflow>("states", workflow => workflow.States.Select(States.KeyOf)),
            Shape<Workflow>("transitions", workflow => workflow.Transitions?.Select(Transitions.KeyOf)),
        ])
    {
        Children = (current, next, workflow) =>
            DiffList(States, current.States, next.States, workflow)
                .Concat(DiffOptionalList(Transitions, current.Transitions, next.Transitions, workflow)),
    };

    private static readonly ResourceSpec<ItemType> ItemTypes = new(
        ResourceKind.ItemType,
        "itemTypes",
        type => type.Key,
        [
            Value<ItemType>("name", type => type.Name),
            Value<ItemType>("workflow", type => type.Workflow),
            Value<ItemType>("fields", type => type.Fields),
            Value<ItemType>("parents", type => type.Parents),
            Value<ItemType>("icon", type => type.Icon),
        ]);

    private static readonly ResourceSpec<ViewDefinition> Views = new(
        ResourceKind.View,
        "views",
        view => view.Key,
        [
            Value<ViewDefinition>("name", view => view.Name),
            Value<ViewDefinition>("type", view => view.Type),
            Value<ViewDefinition>("groupBy", view => view.GroupBy),
            Value<ViewDefinition>("columns", view => view.Columns),
            Value<ViewDefinition>("sort", view => view.Sort),
            Value<ViewDefinition>("filter", view => view.Filter),
            Value<ViewDefinition>("visibleTo", view => view.VisibleTo),
        ]);

    private static readonly ResourceSpec<Automation> Automations = new(
        ResourceKind.Automation,
        "automations",
        automation => automation.Key,
        [
            Value<Automation>("name", automation => automation.Name),
            Value<Automation>("enabled", automation => automation.Enabled),
            Value<Automation>("trigger", automation => automation.Trigger),
            Value<Automation>("conditions", automation => automation.Conditions),
            Value<Automation>("actions", automation => automation.Actions),
        ]);

    private static readonly ResourceSpec<RoleDefinition> Roles = new(
        ResourceKind.Role,
        "roles",
        role => role.Key,
        [
            Value<RoleDefinition>("name", role => role.Name),
            Value<RoleDefinition>("permissions", role => role.Permissions),
        ]);

    // 整合綁定沒有 key，也沒有別的屬性：連接器加資源就是它的身分，換了就是另一個綁定。
    private static readonly ResourceSpec<IntegrationBinding> Bindings = new(
        ResourceKind.Binding,
        "bindings",
        binding => $"{DocumentNames.Of(binding.Connector)}:{binding.Resource}",
        []);

    // 最上層各個清單的順序。只有兩版都有的資源換了先後才算，中間插入或拿掉別的資源不算。
    private static readonly ResourceSpec<ProjectBlueprint> Root = new(
        ResourceKind.Blueprint,
        string.Empty,
        blueprint => blueprint.Metadata.Key,
        [
            Shape<ProjectBlueprint>(Fields.ListName, blueprint => blueprint.Fields.Select(Fields.KeyOf)),
            Shape<ProjectBlueprint>(Workflows.ListName, blueprint => blueprint.Workflows.Select(Workflows.KeyOf)),
            Shape<ProjectBlueprint>(ItemTypes.ListName, blueprint => blueprint.ItemTypes.Select(ItemTypes.KeyOf)),
            Shape<ProjectBlueprint>(Views.ListName, blueprint => blueprint.Views.Select(Views.KeyOf)),
            Shape<ProjectBlueprint>(Automations.ListName, blueprint => blueprint.Automations.Select(Automations.KeyOf)),
            Shape<ProjectBlueprint>(Roles.ListName, blueprint => blueprint.Roles.Select(Roles.KeyOf)),
            Shape<ProjectBlueprint>(Bindings.ListName, blueprint => blueprint.Bindings.Select(Bindings.KeyOf)),
        ]);

    public static IEnumerable<BlueprintChange> Diff(ProjectBlueprint? current, ProjectBlueprint next) =>
        DiffRoot(current, next)
            .Concat(DiffMetadata(current?.Metadata, next.Metadata))
            .Concat(DiffTopLevel(Fields, current?.Fields, next.Fields))
            .Concat(DiffTopLevel(Workflows, current?.Workflows, next.Workflows))
            .Concat(DiffTopLevel(ItemTypes, current?.ItemTypes, next.ItemTypes))
            .Concat(DiffTopLevel(Views, current?.Views, next.Views))
            .Concat(DiffTopLevel(Automations, current?.Automations, next.Automations))
            .Concat(DiffTopLevel(Roles, current?.Roles, next.Roles))
            .Concat(DiffTopLevel(Bindings, current?.Bindings, next.Bindings));

    private static IEnumerable<BlueprintChange> DiffRoot(ProjectBlueprint? current, ProjectBlueprint next) =>
        current is null ? [] : DiffItem(Root, current, next, Site.OfRoot(Root.KeyOf(next)));

    private static IEnumerable<BlueprintChange> DiffMetadata(BlueprintMetadata? current, BlueprintMetadata next)
    {
        const string path = "/metadata";
        return current is null
            ? [Change(ChangeKind.Added, ResourceKind.Metadata, new Site(next.Key, ParentKey: null, CurrentPath: null, path))]
            : DiffItem(Metadata, current, next, new Site(next.Key, ParentKey: null, path, path));
    }

    // 新專案沒有目前的藍圖，當成每個清單都是空的來比，所有資源都會是新增。
    private static IEnumerable<BlueprintChange> DiffTopLevel<T>(ResourceSpec<T> spec, IReadOnlyList<T>? current, IReadOnlyList<T> next) =>
        DiffList(spec, current ?? [], next, parent: null);

    // 清單從「沒寫」變成「有寫」（或反過來）是上一層的一個屬性變了，意義和逐項增減不同：
    // 流程沒寫 transitions 表示任何狀態之間都能互轉，這時把新清單的每一項都說成「新增」會誤導人。
    private static IEnumerable<BlueprintChange> DiffOptionalList<T>(
        ResourceSpec<T> spec, IReadOnlyList<T>? current, IReadOnlyList<T>? next, Site parent) =>
        current is null || next is null ? [] : DiffList(spec, current, next, parent);

    /// <param name="parent">這個清單所屬的資源；最上層的清單是 null。</param>
    private static IEnumerable<BlueprintChange> DiffList<T>(
        ResourceSpec<T> spec, IReadOnlyList<T> current, IReadOnlyList<T> next, Site? parent)
    {
        var currentBase = $"{parent?.CurrentPath}/{spec.ListName}";
        var nextBase = $"{parent?.NewPath}/{spec.ListName}";
        var currentPositions = KeyPositions.First(current, spec.KeyOf);
        var nextKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < next.Count; index++)
        {
            // 通過驗證的藍圖不會有重複的 key；真的有也只看第一個，不丟例外。
            var key = spec.KeyOf(next[index]);
            if (!nextKeys.Add(key))
            {
                continue;
            }

            if (!currentPositions.TryGetValue(key, out var position))
            {
                yield return Change(ChangeKind.Added, spec.Kind, new Site(key, parent?.Key, CurrentPath: null, $"{nextBase}/{index}"));
                continue;
            }

            var site = new Site(key, parent?.Key, $"{currentBase}/{position}", $"{nextBase}/{index}");
            foreach (var change in DiffItem(spec, current[position], next[index], site))
            {
                yield return change;
            }
        }

        for (var position = 0; position < current.Count; position++)
        {
            var key = spec.KeyOf(current[position]);
            if (!nextKeys.Contains(key) && currentPositions[key] == position)
            {
                yield return Change(ChangeKind.Removed, spec.Kind, new Site(key, parent?.Key, $"{currentBase}/{position}", NewPath: null));
            }
        }
    }

    private static IEnumerable<BlueprintChange> DiffItem<T>(ResourceSpec<T> spec, T current, T next, Site site)
    {
        var changed = spec.Properties.Where(property => !property.IsSame(current, next)).Select(property => property.Name).ToArray();
        if (changed.Length > 0)
        {
            yield return Change(ChangeKind.Modified, spec.Kind, site, changed);
        }

        foreach (var child in spec.Children(current, next, site))
        {
            yield return child;
        }
    }

    private static BlueprintChange Change(ChangeKind kind, ResourceKind resource, Site site, IReadOnlyList<string>? changed = null) =>
        new()
        {
            Kind = kind,
            Resource = resource,
            Key = site.Key,
            ParentKey = site.ParentKey,
            CurrentPath = site.CurrentPath,
            NewPath = site.NewPath,
            ChangedProperties = changed ?? [],
        };

    private static PropertySpec<T> Value<T>(string name, Func<T, object?> get) =>
        new(name, (current, next) => SameValue(get(current), get(next)));

    /// <summary>底下有自己差異的清單。這個屬性只管清單的「形狀」：有沒有寫，以及兩版都有的項目順序一不一樣。</summary>
    private static PropertySpec<T> Shape<T>(string name, Func<T, IEnumerable<string>?> keysOf) =>
        new(name, (current, next) => SameShape(keysOf(current), keysOf(next)));

    // 清單與巢狀的 record 沒有值相等，所以寫成 JSON 文字再比。null 和空清單寫出來不一樣，正好符合它們意義不同這件事。
    private static bool SameValue(object? current, object? next) =>
        Equals(current, next)
        || (current is not null && next is not null && JsonSerializer.Serialize(current) == JsonSerializer.Serialize(next));

    private static bool SameShape(IEnumerable<string>? current, IEnumerable<string>? next)
    {
        if (current is null || next is null)
        {
            return current is null && next is null;
        }

        var currentKeys = current.Distinct(StringComparer.Ordinal).ToArray();
        var nextKeys = next.Distinct(StringComparer.Ordinal).ToArray();
        var inCurrent = currentKeys.ToHashSet(StringComparer.Ordinal);
        var inNext = nextKeys.ToHashSet(StringComparer.Ordinal);
        return currentKeys.Where(inNext.Contains).SequenceEqual(nextKeys.Where(inCurrent.Contains), StringComparer.Ordinal);
    }

    /// <summary>一個資源是誰、在哪裡：它的 key、所屬資源的 key，以及在兩版藍圖裡的位置（沒有的那一邊是 null）。</summary>
    private sealed record Site(string Key, string? ParentKey, string? CurrentPath, string? NewPath)
    {
        public static Site OfRoot(string key) => new(key, ParentKey: null, RootPath, RootPath);
    }

    /// <summary>一類資源怎麼比：種類、它的清單在文件裡叫什麼、key 怎麼取、要比哪些屬性、底下還有哪些清單。</summary>
    private sealed record ResourceSpec<T>(
        ResourceKind Kind, string ListName, Func<T, string> KeyOf, IReadOnlyList<PropertySpec<T>> Properties)
    {
        /// <summary>兩版都有的資源，底下的清單還要再比。參數是目前的資源、新的資源，以及這個資源是誰、在哪裡。</summary>
        public Func<T, T, Site, IEnumerable<BlueprintChange>> Children { get; init; } = (_, _, _) => [];
    }

    /// <summary>一個屬性：文件裡的名稱，以及兩版的值算不算相同。</summary>
    private sealed record PropertySpec<T>(string Name, Func<T, T, bool> IsSame);
}
