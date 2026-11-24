using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Tests.Validation;

/// <summary>
/// 和 schemas/blueprint/v0/examples 的兩個範例內容相同的模型。
/// 直接用 record 組出來，語意規則的測試才不必等讀檔與結構檢查做好。
/// </summary>
internal static class SampleBlueprints
{
    public const int QuoteAmount = 0;
    public const int ClientSignoffDate = 1;
    public const int ChangeReason = 2;
    public const int Impact = 3;

    public const int Delivery = 0;
    public const int TaskFlow = 1;

    public const int Requirement = 0;
    public const int ChangeRequest = 1;
    public const int TaskType = 2;

    public const int ProgressBoard = 0;
    public const int RequirementTable = 1;
    public const int ClientView = 2;

    public static ProjectBlueprint Agency() => new()
    {
        ApiVersion = "blueprint/v0",
        Kind = "ProjectBlueprint",
        Metadata = new BlueprintMetadata
        {
            Key = "agency_project",
            Name = "接案專案",
            Description = "從需求確認到結案的交付流程，客戶以訪客身分參與驗收。",
            BasedOn = new TemplateReference { Template = "agency_project", Version = "1.0.0" },
        },
        Fields = AgencyFields(),
        Workflows = [DeliveryWorkflow(), TaskFlowWorkflow()],
        ItemTypes = AgencyItemTypes(),
        Views = AgencyViews(),
        Automations = [MergedToInternalQa(whileIn: "developing")],
        Roles =
        [
            ProjectManager("item.read", "item.create", "item.update", "item.transition", "comment.create"),
            new RoleDefinition { Key = "client", Name = "客戶", Permissions = ["item.read", "comment.create"] },
        ],
        Bindings = [new IntegrationBinding { Connector = ConnectorType.Gitlab, Resource = "example-group/web-app" }],
    };

    public static ChangeSetRequest SplitAcceptance() => new()
    {
        BaseVersion = 12,
        Summary = "把驗收拆成內部驗收與客戶驗收，客戶驗收要專案經理才能送出。",
        Blueprint = new ProjectBlueprint
        {
            ApiVersion = "blueprint/v0",
            Kind = "ProjectBlueprint",
            Metadata = new BlueprintMetadata { Key = "website_redesign", Name = "官網改版" },
            Fields = [],
            Workflows = [MainWorkflow()],
            ItemTypes = [new ItemType { Key = "task", Name = "任務", Workflow = "main" }],
            Automations = [MergedToInternalQa(whileIn: "doing")],
            Roles = [ProjectManager("item.read", "item.update", "item.transition", "comment.create")],
            Bindings = [new IntegrationBinding { Connector = ConnectorType.Gitlab, Resource = "example-group/website" }],
        },
        Migrations = new MigrationHints
        {
            MoveStates = [new StateMove { Workflow = "main", From = "qa", To = "internal_qa" }],
        },
    };

    public static WorkflowState State(string key, StateCategory category) =>
        new() { Key = key, Name = key, Category = category };

    public static WorkflowTransition Transition(string from, string to) => new() { From = from, To = to };

    public static FieldDefinition TextField(string key) => new() { Key = key, Name = key, Type = FieldType.Text };

    public static FieldOption Option(string key) => new() { Key = key, Name = key };

    public static RoleDefinition Role(string key) => new() { Key = key, Name = key, Permissions = ["item.read"] };

    /// <summary>只有一個完成類狀態、沒有列轉換的流程：本身不會觸發任何規則。</summary>
    public static Workflow SimpleWorkflow(string key) =>
        new() { Key = key, Name = key, States = [State("done", StateCategory.Done)] };

    public static ItemType ItemTypeOf(string key, string workflow) => new() { Key = key, Name = key, Workflow = workflow };

    private static WorkflowState State(string key, string name, StateCategory category) =>
        new() { Key = key, Name = name, Category = category };

    private static Workflow DeliveryWorkflow() => new()
    {
        Key = "delivery",
        Name = "交付流程",
        States =
        [
            State("confirming", "需求確認", StateCategory.NotStarted),
            State("quoting", "報價", StateCategory.NotStarted),
            State("designing", "設計", StateCategory.InProgress),
            State("developing", "開發", StateCategory.InProgress),
            State("internal_qa", "內部驗收", StateCategory.InProgress),
            State("client_qa", "客戶驗收", StateCategory.InProgress),
            State("closed", "結案", StateCategory.Done),
        ],
        Transitions =
        [
            Transition("confirming", "quoting"),
            Transition("quoting", "designing"),
            Transition("designing", "developing"),
            Transition("developing", "internal_qa"),
            Transition("internal_qa", "developing"),
            Transition("internal_qa", "client_qa") with { AllowedRoles = ["project_manager"] },
            Transition("client_qa", "developing"),
            Transition("client_qa", "closed") with
            {
                AllowedRoles = ["project_manager"],
                RequireFields = ["client_signoff_date"],
            },
        ],
    };

    private static Workflow TaskFlowWorkflow() => new()
    {
        Key = "task_flow",
        Name = "任務流程",
        States =
        [
            State("todo", "待辦", StateCategory.NotStarted),
            State("doing", "進行中", StateCategory.InProgress),
            State("done", "完成", StateCategory.Done),
        ],
    };

    private static Workflow MainWorkflow() => new()
    {
        Key = "main",
        Name = "主要流程",
        States =
        [
            State("todo", "待辦", StateCategory.NotStarted),
            State("doing", "進行中", StateCategory.InProgress),
            State("internal_qa", "內部驗收", StateCategory.InProgress),
            State("client_qa", "客戶驗收", StateCategory.InProgress),
            State("done", "完成", StateCategory.Done),
        ],
        Transitions =
        [
            Transition("todo", "doing"),
            Transition("doing", "internal_qa"),
            Transition("internal_qa", "doing"),
            Transition("internal_qa", "client_qa") with { AllowedRoles = ["project_manager"] },
            Transition("client_qa", "doing"),
            Transition("client_qa", "done"),
        ],
    };

    private static IReadOnlyList<ViewDefinition> AgencyViews() =>
    [
        new ViewDefinition { Key = "progress_board", Name = "進度看板", Type = ViewType.Board, GroupBy = "state" },
        new ViewDefinition
        {
            Key = "requirement_table",
            Name = "需求清單",
            Type = ViewType.Table,
            Columns = ["title", "state", "assignee", "quote_amount", "due_date"],
            Sort = [new ViewSort { Field = "due_date", Direction = SortDirection.Asc }],
            Filter = new ViewFilter { ItemTypes = ["requirement", "change_request"] },
        },
        new ViewDefinition
        {
            Key = "client_view",
            Name = "給客戶看",
            Type = ViewType.Table,
            Columns = ["title", "state", "client_signoff_date"],
            Filter = new ViewFilter { ItemTypes = ["requirement", "change_request"] },
            VisibleTo = ["client", "project_manager"],
        },
    ];

    private static IReadOnlyList<FieldDefinition> AgencyFields() =>
    [
        new FieldDefinition { Key = "quote_amount", Name = "報價金額", Type = FieldType.Currency, Currency = "TWD" },
        new FieldDefinition { Key = "client_signoff_date", Name = "客戶驗收日", Type = FieldType.Date },
        new FieldDefinition { Key = "change_reason", Name = "變更原因", Type = FieldType.LongText },
        new FieldDefinition
        {
            Key = "impact",
            Name = "影響程度",
            Type = FieldType.SingleSelect,
            Options =
            [
                new FieldOption { Key = "low", Name = "低", Color = OptionColor.Gray },
                new FieldOption { Key = "medium", Name = "中", Color = OptionColor.Yellow },
                new FieldOption { Key = "high", Name = "高", Color = OptionColor.Red },
            ],
        },
    ];

    private static IReadOnlyList<ItemType> AgencyItemTypes() =>
    [
        new ItemType { Key = "requirement", Name = "需求", Workflow = "delivery", Fields = ["quote_amount", "client_signoff_date"] },
        new ItemType
        {
            Key = "change_request",
            Name = "需求變更單",
            Workflow = "delivery",
            Fields = ["change_reason", "impact", "quote_amount", "client_signoff_date"],
        },
        new ItemType { Key = "task", Name = "任務", Workflow = "task_flow", Parents = ["requirement", "change_request"] },
    ];

    /// <summary>兩個範例共用的自動化，只差條件裡的狀態。</summary>
    private static Automation MergedToInternalQa(string whileIn) => new()
    {
        Key = "mr_merged_to_internal_qa",
        Name = "合併請求合併後轉內部驗收",
        Trigger = new AutomationTrigger { Source = TriggerSource.Gitlab, Event = "merge_request.merged" },
        Conditions = [new AutomationCondition { Field = "state", In = [whileIn] }],
        Actions = [new AutomationAction { Type = ActionType.Transition, To = "internal_qa" }],
    };

    /// <summary>兩個範例的專案經理只差項目權限；藍圖權限都是讀、提案、套用。</summary>
    private static RoleDefinition ProjectManager(params string[] itemPermissions) => new()
    {
        Key = "project_manager",
        Name = "專案經理",
        Permissions = [.. itemPermissions, "blueprint.read", "blueprint.propose", "blueprint.apply"],
    };
}
