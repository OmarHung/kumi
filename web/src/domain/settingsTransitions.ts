import type { ProjectBlueprint, WorkflowState, WorkflowTransition } from '../api/blueprint';
import { mapWorkflow, restoreInOrder, sameSet, type SettingsDraft, type WorkflowDraft } from './settingsDraft';

/**
 * 設定頁的轉換：每個流程可以限制（只有列出的轉換能走）或不限制（任何兩個狀態都能互轉）。
 * 草稿和「目前藍圖允許的轉換」比：有列的就是列出的那些，不限制的當成每兩個狀態都能互轉，
 * 這樣從不限制切成限制的當下沒有任何一條算修改，使用者取消不要的才算「要刪除」。
 */

export type TransitionStatus = 'unchanged' | 'added' | 'modified' | 'removed';

/** 允許的角色、要先填的欄位；沒給的不改。 */
export interface TransitionPatch {
  readonly allowedRoles?: readonly string[];
  readonly requireFields?: readonly string[];
}

/** 要先填的欄位能選的核心欄位：說明與上層項目。其他核心欄位（標題等）不列：標題一定有，其餘的需求還沒出現。 */
export const REQUIRABLE_CORE_FIELDS: readonly string[] = ['description', 'parent'];

/** 和伺服器的差異一樣，轉換以「起點->終點」為身分。 */
export function transitionKey(transition: Pick<WorkflowTransition, 'from' | 'to'>): string {
  return `${transition.from}->${transition.to}`;
}

/** 新的轉換和 API 回的形狀一樣，兩個陣列都帶。 */
export function newTransition(from: string, to: string): WorkflowTransition {
  return { from, to, allowedRoles: [], requireFields: [] };
}

/** 每兩個不同的狀態都可以互轉：照狀態的順序，n×(n−1) 條。 */
export function allPairs(states: readonly WorkflowState[]): readonly WorkflowTransition[] {
  return states.flatMap((from) => states.filter((to) => to.key !== from.key).map((to) => newTransition(from.key, to.key)));
}

function baseWorkflow(base: ProjectBlueprint, workflowKey: string) {
  return base.workflows.find((workflow) => workflow.key === workflowKey);
}

/** 目前藍圖裡這個流程允許的轉換：有列的是列出的那些；不限制的當成每兩個狀態都可以互轉。 */
export function baseTransitions(base: ProjectBlueprint, workflowKey: string): readonly WorkflowTransition[] {
  const workflow = baseWorkflow(base, workflowKey);
  return workflow?.transitions ?? allPairs(workflow?.states ?? []);
}

export function findTransition(
  transitions: readonly WorkflowTransition[] | undefined,
  from: string,
  to: string,
): WorkflowTransition | undefined {
  return transitions?.find((transition) => transition.from === from && transition.to === to);
}

/** 兩端都還在草稿裡的轉換：進出被刪除的狀態的不顯示，送出時也拿掉。不限制時是 undefined。 */
export function visibleTransitions(workflow: WorkflowDraft): readonly WorkflowTransition[] | undefined {
  const keys = new Set(workflow.states.map((state) => state.key));
  return workflow.transitions?.filter((transition) => keys.has(transition.from) && keys.has(transition.to));
}

/** 允許的角色與要先填的欄位都是同一組（不看先後）。 */
export function sameLimits(left: WorkflowTransition, right: WorkflowTransition): boolean {
  return sameSet(left.allowedRoles, right.allowedRoles) && sameSet(left.requireFields, right.requireFields);
}

/**
 * 勾起或取消一條轉換（只在有限制的流程）。勾起目前藍圖就允許的那一條時，放回原本的位置、帶回原本的限制，
 * 送出的才是原本的物件；新的接在最後。
 */
export function setTransition(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string, from: string, to: string, isAllowed: boolean): SettingsDraft {
  return mapWorkflow(draft, workflowKey, (workflow) => {
    const current = workflow.transitions;
    if (current === undefined || isAllowed === (findTransition(current, from, to) !== undefined)) {
      return workflow;
    }
    if (!isAllowed) {
      return { ...workflow, transitions: current.filter((transition) => transition.from !== from || transition.to !== to) };
    }
    const original = baseTransitions(base, workflowKey);
    const before = findTransition(original, from, to);
    const transitions = before === undefined ? [...current, newTransition(from, to)] : restoreInOrder(current, original, before, transitionKey);
    return { ...workflow, transitions };
  });
}

export function updateTransition(draft: SettingsDraft, workflowKey: string, from: string, to: string, patch: TransitionPatch): SettingsDraft {
  return mapWorkflow(draft, workflowKey, (workflow) => ({
    ...workflow,
    transitions: workflow.transitions?.map((transition) =>
      transition.from === from && transition.to === to ? { ...transition, ...patch } : transition,
    ),
  }));
}

/**
 * 切換限制。限制時：目前藍圖本來就有列轉換的回到那些；本來不限制的從「每兩個狀態都可以互轉」開始，
 * 切換的當下行為不變，使用者再取消不要的。不限制時整個 transitions 不送。
 */
export function setRestricted(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string, isRestricted: boolean): SettingsDraft {
  return mapWorkflow(draft, workflowKey, (workflow) => {
    if (!isRestricted) {
      return { ...workflow, transitions: undefined };
    }
    return { ...workflow, transitions: baseWorkflow(base, workflowKey)?.transitions ?? allPairs(workflow.states) };
  });
}

/** 新增狀態時順手加的轉換：從 incoming 轉進來、轉去 outgoing。不限制的流程不必加。 */
export function addStateTransitions(
  draft: SettingsDraft,
  workflowKey: string,
  stateKey: string,
  incoming: readonly string[],
  outgoing: readonly string[],
): SettingsDraft {
  return mapWorkflow(draft, workflowKey, (workflow) => {
    if (workflow.transitions === undefined) {
      return workflow;
    }
    const added = [...incoming.map((from) => newTransition(from, stateKey)), ...outgoing.map((to) => newTransition(stateKey, to))];
    return { ...workflow, transitions: [...workflow.transitions, ...added] };
  });
}

function draftWorkflow(draft: SettingsDraft, workflowKey: string): WorkflowDraft | undefined {
  return draft.workflows.find((workflow) => workflow.key === workflowKey);
}

/** 一條轉換還沒送出的修改。不限制的流程不比個別的轉換（切換限制本身算一項）。 */
export function transitionStatus(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string, from: string, to: string): TransitionStatus {
  const current = draftWorkflow(draft, workflowKey)?.transitions;
  if (current === undefined) {
    return 'unchanged';
  }
  const transition = findTransition(current, from, to);
  const original = findTransition(baseTransitions(base, workflowKey), from, to);
  if (transition === undefined) {
    return original === undefined ? 'unchanged' : 'removed';
  }
  if (original === undefined) {
    return 'added';
  }
  return sameLimits(transition, original) ? 'unchanged' : 'modified';
}

/** 切換了限制（有限制 ↔ 不限制）。 */
export function isRestrictionChanged(base: ProjectBlueprint, workflow: WorkflowDraft): boolean {
  return (baseWorkflow(base, workflow.key)?.transitions === undefined) !== (workflow.transitions === undefined);
}

/** 兩端都還在的轉換裡，目前藍圖允許、草稿取消了的。 */
export function removedTransitions(base: ProjectBlueprint, workflow: WorkflowDraft): readonly WorkflowTransition[] {
  const current = workflow.transitions;
  if (current === undefined) {
    return [];
  }
  const keys = new Set(workflow.states.map((state) => state.key));
  return baseTransitions(base, workflow.key).filter(
    (transition) => keys.has(transition.from) && keys.has(transition.to) && findTransition(current, transition.from, transition.to) === undefined,
  );
}

/**
 * 一端是剛新增的狀態（目前藍圖的這個流程裡沒有）：新狀態本身已經算一項，它的轉換不另外算。
 * 否則「先新增狀態再切成限制」（新狀態的每一對都算新增）和「先切成限制再新增」（只算勾的那幾條）計數會不一樣。
 */
function touchesNewState(base: ProjectBlueprint, workflowKey: string, transition: WorkflowTransition): boolean {
  const states = baseWorkflow(base, workflowKey)?.states ?? [];
  const isOriginal = (key: string) => states.some((state) => state.key === key);
  return !isOriginal(transition.from) || !isOriginal(transition.to);
}

/**
 * 還沒送出的轉換修改有幾項：切換限制算一項；有限制時每條新增、刪除、修改的各一項
 * （兩端有狀態被刪除、或有剛新增的狀態的，不另外算）。
 */
export function transitionChangeCount(base: ProjectBlueprint, draft: SettingsDraft): number {
  return draft.workflows.reduce((count, workflow) => {
    const toggled = isRestrictionChanged(base, workflow) ? 1 : 0;
    const changed = (visibleTransitions(workflow) ?? []).filter(
      (transition) =>
        !touchesNewState(base, workflow.key, transition) &&
        transitionStatus(base, draft, workflow.key, transition.from, transition.to) !== 'unchanged',
    );
    return count + toggled + changed.length + removedTransitions(base, workflow).length;
  }, 0);
}
