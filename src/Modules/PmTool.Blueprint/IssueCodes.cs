namespace PmTool.Blueprint;

/// <summary>驗證問題的代碼。這些字串是對外合約，改名等於破壞相容性。</summary>
public static class IssueCodes
{
    public const string DocumentEmpty = "document.empty";
    public const string DocumentTooLarge = "document.too_large";
    public const string DocumentSyntax = "document.syntax";
    public const string DocumentMultiple = "document.multiple_documents";
    public const string DocumentAliasNotAllowed = "document.alias_not_allowed";
    public const string DocumentTooDeep = "document.too_deep";
    public const string DocumentTooManyNodes = "document.too_many_nodes";

    public const string StructureType = "structure.type";
    public const string StructureRequired = "structure.required";
    public const string StructureAdditionalProperty = "structure.additional_property";
    public const string StructureEnum = "structure.enum";
    public const string StructureConst = "structure.const";

    public const string KeyFormat = "key.format";
    public const string KeyDuplicate = "key.duplicate";

    public const string FieldCoreNameConflict = "field.core_name_conflict";
    public const string FieldOptionsRequired = "field.options_required";
    public const string FieldOptionsNotAllowed = "field.options_not_allowed";
    public const string FieldCurrencyRequired = "field.currency_required";
    public const string FieldCurrencyNotAllowed = "field.currency_not_allowed";
    public const string FieldCurrencyCode = "field.currency_code";

    public const string RoleBuiltInConflict = "role.builtin_conflict";

    public const string WorkflowNoStates = "workflow.no_states";
    public const string WorkflowNoDoneState = "workflow.no_done_state";
    public const string WorkflowUnreachableState = "workflow.unreachable_state";
    public const string WorkflowDeadEndState = "workflow.dead_end_state";

    public const string TransitionUnknownState = "transition.unknown_state";
    public const string TransitionSelf = "transition.self";
    public const string TransitionDuplicate = "transition.duplicate";
    public const string TransitionUnknownRole = "transition.unknown_role";
    public const string TransitionUnknownField = "transition.unknown_field";
    public const string TransitionFieldMissingOnType = "transition.field_missing_on_type";

    public const string ItemTypeUnknownWorkflow = "item_type.unknown_workflow";
    public const string ItemTypeUnknownField = "item_type.unknown_field";
    public const string ItemTypeDuplicateField = "item_type.duplicate_field";
    public const string ItemTypeUnknownParent = "item_type.unknown_parent";
    public const string ItemTypeParentCycle = "item_type.parent_cycle";

    public const string ViewInvalidGroupBy = "view.invalid_group_by";
    public const string ViewUnknownField = "view.unknown_field";
    public const string ViewUnknownItemType = "view.unknown_item_type";
    public const string ViewUnknownState = "view.unknown_state";
    public const string ViewUnknownRole = "view.unknown_role";

    public const string AutomationMissingBinding = "automation.missing_binding";
    public const string AutomationNoActions = "automation.no_actions";
    public const string AutomationUnknownEvent = "automation.unknown_event";
    public const string AutomationInvalidConditionField = "automation.invalid_condition_field";
    public const string AutomationUnknownState = "automation.unknown_state";
    public const string AutomationUnknownItemType = "automation.unknown_item_type";
    public const string AutomationActionParameters = "automation.action_parameters";
    public const string AutomationUnknownField = "automation.unknown_field";
    public const string AutomationInvalidUrl = "automation.invalid_url";

    public const string LimitExceeded = "limit.exceeded";

    public const string ChangeSetInvalidBaseVersion = "changeset.invalid_base_version";
    public const string ChangeSetMoveUnknownWorkflow = "changeset.move_unknown_workflow";
    public const string ChangeSetMoveUnknownTarget = "changeset.move_unknown_target";
    public const string ChangeSetMoveSourceStillExists = "changeset.move_source_still_exists";
    public const string ChangeSetDropFieldStillExists = "changeset.drop_field_still_exists";
    public const string ChangeSetConvertUnknownField = "changeset.convert_unknown_field";

    public const string PlanBaseVersionMismatch = "plan.base_version_mismatch";
    public const string PlanStateMoveRequired = "plan.state_move_required";
    public const string PlanFieldDropRequired = "plan.field_drop_required";
    public const string PlanFieldConversionRequired = "plan.field_conversion_required";
    public const string PlanFieldConversionNotSupported = "plan.field_conversion_not_supported";
    public const string PlanOptionRemovalNotSupported = "plan.option_removal_not_supported";
    public const string PlanUnusedMigration = "plan.unused_migration";
}
