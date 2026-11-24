import type {
  ItemTypeDefinition,
  ProjectBlueprint,
  Workflow,
  WorkflowState,
  WorkflowTransition,
} from '../api/blueprint';
import { workflowForType } from './blueprint';

// 和後端一樣：只有沒有 transitions 這個屬性（後端是 null，API 省略）才是「任何兩個不同的狀態都可以互轉」；
// 空陣列是一條路都沒有（藍圖驗證也這樣看：其他狀態走不到）。
function hasListedTransitions(workflow: Workflow): boolean {
  return workflow.transitions !== undefined;
}

export function findTransition(
  workflow: Workflow,
  from: string,
  to: string,
): WorkflowTransition | undefined {
  return workflow.transitions?.find((transition) => transition.from === from && transition.to === to);
}

/** 從目前狀態走得到的狀態，順序照流程裡的定義。不含目前的狀態。 */
export function reachableStates(workflow: Workflow, from: string): readonly WorkflowState[] {
  const others = workflow.states.filter((state) => state.key !== from);
  if (!hasListedTransitions(workflow)) {
    return others;
  }
  return others.filter((state) => findTransition(workflow, from, state.key) !== undefined);
}

/**
 * 篩選用的狀態清單。選了類型時是那個類型的流程；沒選時是所有流程的聯集，
 * 同一個 key 只列一次（後端用 key 篩選，不分流程）。
 */
export function filterableStates(
  blueprint: ProjectBlueprint,
  typeKey: string | null,
): readonly WorkflowState[] {
  if (typeKey !== null) {
    return workflowForType(blueprint, typeKey)?.states ?? [];
  }
  const all = blueprint.workflows.flatMap((workflow) => workflow.states);
  return all.filter((state, index) => all.findIndex((other) => other.key === state.key) === index);
}

/** 看板可以選的流程：有類型在用的，照第一個用到它的類型的順序。第一個就是預設的流程。 */
export function boardWorkflows(blueprint: ProjectBlueprint): readonly Workflow[] {
  const keys = blueprint.itemTypes.map((itemType) => itemType.workflow);
  return keys
    .filter((key, index) => keys.indexOf(key) === index)
    .flatMap((key) => blueprint.workflows.find((workflow) => workflow.key === key) ?? []);
}

/** 用這個流程的類型，照藍圖的順序。 */
export function workflowTypes(blueprint: ProjectBlueprint, workflowKey: string): readonly ItemTypeDefinition[] {
  return blueprint.itemTypes.filter((itemType) => itemType.workflow === workflowKey);
}

/** 移到某個狀態走不走得到。roles 是轉換允許的角色 key。 */
export type MoveAvailability =
  | { readonly kind: 'open' }
  | { readonly kind: 'no_path' }
  | { readonly kind: 'role'; readonly roles: readonly string[] };

export interface MoveTarget {
  readonly state: WorkflowState;
  readonly availability: MoveAvailability;
}

const OPEN: MoveAvailability = { kind: 'open' };
const NO_PATH: MoveAvailability = { kind: 'no_path' };

/**
 * 依藍圖的轉換與自己在專案裡的角色預先判斷。要先填的欄位要看項目完整的值（清單沒有說明），
 * 這裡不判斷，最後一律以後端為準。allowedRoles 看的是被指派的角色，組織管理員不是成員（null）也走不到。
 */
export function moveAvailability(
  workflow: Workflow,
  from: string,
  to: string,
  myRole: string | null,
): MoveAvailability {
  if (!hasListedTransitions(workflow)) {
    return OPEN;
  }
  const transition = findTransition(workflow, from, to);
  if (transition === undefined) {
    return NO_PATH;
  }
  const roles = transition.allowedRoles ?? [];
  const isAllowed = roles.length === 0 || (myRole !== null && roles.includes(myRole));
  return isAllowed ? OPEN : { kind: 'role', roles };
}

/** 卡片「移到…」的選項：流程裡其他的狀態，走得到的在前，各自照流程的順序。 */
export function moveTargets(workflow: Workflow, from: string, myRole: string | null): readonly MoveTarget[] {
  const targets = workflow.states
    .filter((state) => state.key !== from)
    .map((state) => ({ state, availability: moveAvailability(workflow, from, state.key, myRole) }));
  const isOpen = (target: MoveTarget) => target.availability.kind === 'open';
  return [...targets.filter(isOpen), ...targets.filter((target) => !isOpen(target))];
}
