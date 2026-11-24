using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Tests.Validation;

/// <summary>改壞範例藍圖用的小工具。每個方法都回傳新的 record，不動原本的物件。</summary>
internal static class ValidationTestExtensions
{
    public static (string Code, string Path)[] CodesAndPaths(this IEnumerable<ValidationIssue> issues) =>
        issues.Select(issue => (issue.Code, issue.Path)).ToArray();

    public static IReadOnlyList<T> ReplaceAt<T>(this IReadOnlyList<T> list, int index, T item) =>
        [.. list.Take(index), item, .. list.Skip(index + 1)];

    public static IReadOnlyList<T> WithoutAt<T>(this IReadOnlyList<T> list, int index) =>
        [.. list.Take(index), .. list.Skip(index + 1)];

    public static ProjectBlueprint WithField(
        this ProjectBlueprint blueprint, int index, Func<FieldDefinition, FieldDefinition> change) =>
        blueprint with { Fields = blueprint.Fields.ReplaceAt(index, change(blueprint.Fields[index])) };

    public static ProjectBlueprint WithWorkflow(
        this ProjectBlueprint blueprint, int index, Func<Workflow, Workflow> change) =>
        blueprint with { Workflows = blueprint.Workflows.ReplaceAt(index, change(blueprint.Workflows[index])) };

    public static ProjectBlueprint WithItemType(
        this ProjectBlueprint blueprint, int index, Func<ItemType, ItemType> change) =>
        blueprint with { ItemTypes = blueprint.ItemTypes.ReplaceAt(index, change(blueprint.ItemTypes[index])) };

    public static ProjectBlueprint WithView(
        this ProjectBlueprint blueprint, int index, Func<ViewDefinition, ViewDefinition> change) =>
        blueprint with { Views = blueprint.Views.ReplaceAt(index, change(blueprint.Views[index])) };

    public static ProjectBlueprint WithAutomation(
        this ProjectBlueprint blueprint, int index, Func<Automation, Automation> change) =>
        blueprint with { Automations = blueprint.Automations.ReplaceAt(index, change(blueprint.Automations[index])) };

    public static ProjectBlueprint WithAction(
        this ProjectBlueprint blueprint, int automation, AutomationAction action) =>
        blueprint.WithAutomation(automation, a => a with { Actions = [action] });

    public static Workflow WithTransition(
        this Workflow workflow, int index, Func<WorkflowTransition, WorkflowTransition> change) =>
        workflow with { Transitions = workflow.Transitions!.ReplaceAt(index, change(workflow.Transitions![index])) };

    public static ChangeSetRequest WithBlueprint(
        this ChangeSetRequest changeSet, Func<ProjectBlueprint, ProjectBlueprint> change) =>
        changeSet with { Blueprint = change(changeSet.Blueprint) };
}
