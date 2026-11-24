import type { ProjectBlueprint, Workflow } from '../../api/blueprint';
import type { Item } from '../../api/types';
import { workflowForType } from '../../domain/blueprint';
import { groupedStateChoices, type StateChoices } from '../work-items/stateChoices';

/** 選取的項目用到的流程，照藍圖的順序、每個流程一份。 */
function workflowsOf(blueprint: ProjectBlueprint, items: readonly Item[]): readonly Workflow[] {
  const types = new Set(items.map((item) => item.type));
  const used = new Set<string>();
  for (const itemType of blueprint.itemTypes) {
    if (!types.has(itemType.key)) continue;
    const workflow = workflowForType(blueprint, itemType.key);
    if (workflow !== undefined) used.add(workflow.key);
  }
  return blueprint.workflows.filter((workflow) => used.has(workflow.key));
}

/**
 * 批次改狀態的選項：選取的項目用到的流程的每個狀態。只有一個流程時就是那個流程的狀態；
 * 分屬不同流程時依流程分組（組名是流程的名稱），同 key 同名稱的狀態只列一次（放在第一個有它的流程底下）。
 * 每一筆能不能移過去由伺服器照它自己的流程判斷，不能的在結果裡列出原因。
 */
export function stateChoicesFor(blueprint: ProjectBlueprint, items: readonly Item[]): StateChoices {
  return groupedStateChoices(workflowsOf(blueprint, items).map((workflow) => ({ workflow, label: workflow.name })));
}
