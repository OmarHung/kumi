import type {
  ItemTypeDefinition,
  ProjectBlueprint,
  StateCategory,
  Workflow,
  WorkflowState,
} from '../api/blueprint';
import { STATE_CATEGORY_LABELS } from './labels';

export function findItemType(
  blueprint: ProjectBlueprint,
  typeKey: string,
): ItemTypeDefinition | undefined {
  return blueprint.itemTypes.find((itemType) => itemType.key === typeKey);
}

/** 狀態是依類型的流程定義的，同一個 key 在不同流程可以是不同的名稱。 */
export function workflowForType(
  blueprint: ProjectBlueprint,
  typeKey: string,
): Workflow | undefined {
  const itemType = findItemType(blueprint, typeKey);
  return itemType && blueprint.workflows.find((workflow) => workflow.key === itemType.workflow);
}

export function findState(
  blueprint: ProjectBlueprint,
  typeKey: string,
  stateKey: string,
): WorkflowState | undefined {
  return workflowForType(blueprint, typeKey)?.states.find((state) => state.key === stateKey);
}

export function itemTypeName(blueprint: ProjectBlueprint, typeKey: string): string {
  return findItemType(blueprint, typeKey)?.name ?? typeKey;
}

/** 狀態的顯示名稱。藍圖還沒載入或找不到時，退回狀態類別的中文，畫面至少看得懂。 */
export function stateLabel(
  blueprint: ProjectBlueprint | undefined,
  typeKey: string,
  stateKey: string,
  category: StateCategory,
): string {
  const state = blueprint && findState(blueprint, typeKey, stateKey);
  return state?.name ?? STATE_CATEGORY_LABELS[category];
}
