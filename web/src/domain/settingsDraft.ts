import type {
  FieldDefinition,
  FieldOption,
  ItemTypeDefinition,
  ProjectBlueprint,
  RoleDefinition,
  WorkflowState,
  WorkflowTransition,
} from '../api/blueprint';
import type { StateMove } from '../api/types';

/**
 * 設定頁的草稿：使用者可以改很多處，最後一次送出成一份變更集。
 * 只放設定頁能改的東西（每個流程的狀態與轉換、自訂欄位與它套用到哪些類型、類型、自訂角色）；其他部分送出時照目前的藍圖帶過去，
 * 類型的 fields、被刪除的東西在別處的引用由 settingsChangeSet.ts 依草稿推出來。每個函式都回傳新的草稿，不改原本的。
 */

/** 草稿和目前的藍圖比起來，每一列是什麼狀況。moved 是只換了先後（和其他兩版都有的比），內容沒改。 */
export type DraftStatus = 'unchanged' | 'added' | 'modified' | 'moved';

/** 被刪除的狀態（目前的藍圖裡有的）停著的項目要搬去哪裡；還沒選是空字串。 */
export interface PendingMove {
  readonly from: string;
  readonly to: string;
}

export interface WorkflowDraft {
  readonly key: string;
  /** 照順序；第一個是新項目的起始狀態。 */
  readonly states: readonly WorkflowState[];
  readonly moves: readonly PendingMove[];
  /**
   * 列出的轉換。undefined 是不限制（送出時整個屬性不送）；空陣列是一條路都沒有。
   * 刪除狀態時不在這裡拿掉進出它的轉換，組變更集時才拿掉：復原狀態時轉換跟著回來。
   */
  readonly transitions: readonly WorkflowTransition[] | undefined;
}

export interface FieldDraft extends FieldDefinition {
  /** 套用到哪些類型（類型的 key），送出時寫進類型的 fields。 */
  readonly appliesTo: readonly string[];
}

/** 類型能改的部分。fields 由欄位的 appliesTo 推出來；其他屬性（例如 icon）送出時照目前的藍圖帶過去。 */
export interface ItemTypeDraft {
  readonly key: string;
  readonly name: string;
  readonly workflow: string;
  /** 可以掛在哪些類型底下；空的是不能有上層。 */
  readonly parents: readonly string[];
}

export interface SettingsDraft {
  readonly workflows: readonly WorkflowDraft[];
  readonly fields: readonly FieldDraft[];
  readonly itemTypes: readonly ItemTypeDraft[];
  /** 自訂角色（內建的四個不在這裡），權限照 PERMISSION_ORDER 的順序。 */
  readonly roles: readonly RoleDefinition[];
  /**
   * 類型換流程時，使用者為舊狀態選的新狀態（workflow 是新的流程、from 是舊的狀態）。
   * 伺服器每個（新流程, 舊狀態）只認一條指示，所以以它為鍵、多個類型共用；沒選過的用預設（settingsTypes.ts）。
   */
  readonly typeMoves: readonly StateMove[];
}

export type StatePatch = Partial<Pick<WorkflowState, 'name' | 'category'>>;

/** 名稱只差前後的空白不算修改：送出時會去掉（settingsChangeSet.ts）。 */
export function sameName(left: string, right: string): boolean {
  return left.trim() === right.trim();
}

/** 兩組 key 是不是同一組（不看先後）：允許的角色、要先填的欄位、上層、權限都是勾選出來的，先後不算修改。 */
export function sameSet(left: readonly string[] | undefined, right: readonly string[] | undefined): boolean {
  const before = left ?? [];
  const after = right ?? [];
  return before.length === after.length && before.every((key) => after.includes(key));
}

export function byKey(entry: { readonly key: string }): string {
  return entry.key;
}

export function typeDraftOf(type: ItemTypeDefinition): ItemTypeDraft {
  return { key: type.key, name: type.name, workflow: type.workflow, parents: type.parents ?? [] };
}

export function draftFromBlueprint(blueprint: ProjectBlueprint): SettingsDraft {
  return {
    workflows: blueprint.workflows.map((workflow) => ({
      key: workflow.key,
      states: workflow.states,
      moves: [],
      transitions: workflow.transitions,
    })),
    fields: blueprint.fields.map((field) => ({
      ...field,
      appliesTo: blueprint.itemTypes.filter((type) => type.fields?.includes(field.key)).map((type) => type.key),
    })),
    itemTypes: blueprint.itemTypes.map(typeDraftOf),
    roles: blueprint.roles ?? [],
    typeMoves: [],
  };
}

export function mapWorkflow(
  draft: SettingsDraft,
  workflowKey: string,
  update: (workflow: WorkflowDraft) => WorkflowDraft,
): SettingsDraft {
  return {
    ...draft,
    workflows: draft.workflows.map((workflow) => (workflow.key === workflowKey ? update(workflow) : workflow)),
  };
}

/**
 * 拿掉每條轉換裡對某個東西的引用。只用在剛新增、還沒送出的狀態、欄位、角色被刪掉時：它們不能復原，
 * 引用留著的話，之後用同一個 key 新增時舊的轉換與限制會跑回來。目前藍圖裡就有的刪掉時不拿（復原時引用跟著回來）。
 */
export function stripTransitionReferences(
  draft: SettingsDraft,
  update: (transition: WorkflowTransition) => WorkflowTransition,
): SettingsDraft {
  return {
    ...draft,
    workflows: draft.workflows.map((workflow) =>
      workflow.transitions === undefined
        ? workflow
        : { ...workflow, transitions: workflow.transitions.map(update) },
    ),
  };
}

function mapField(draft: SettingsDraft, fieldKey: string, update: (field: FieldDraft) => FieldDraft): SettingsDraft {
  return { ...draft, fields: draft.fields.map((field) => (field.key === fieldKey ? update(field) : field)) };
}

export function insertAt<T>(list: readonly T[], index: number, entry: T): readonly T[] {
  const position = Math.max(0, Math.min(index, list.length));
  return [...list.slice(0, position), entry, ...list.slice(position)];
}

/**
 * 把一個原本有的項目放回草稿：放在原本排在它前面、而且還在草稿裡的那一個後面；前面都不在了就放最前面。
 * 這樣其他項目的先後不變，送出時不會多出「順序變了」的差異。
 */
export function restoreInOrder<T>(list: readonly T[], original: readonly T[], entry: T, keyOf: (item: T) => string): readonly T[] {
  const key = keyOf(entry);
  const before = original.slice(0, original.findIndex((candidate) => keyOf(candidate) === key));
  const anchor = before.findLast((candidate) => list.some((present) => keyOf(present) === keyOf(candidate)));
  const index = anchor === undefined ? 0 : list.findIndex((present) => keyOf(present) === keyOf(anchor)) + 1;
  return insertAt(list, index, entry);
}

export function addState(draft: SettingsDraft, workflowKey: string, state: WorkflowState, index: number): SettingsDraft {
  return mapWorkflow(draft, workflowKey, (workflow) => ({ ...workflow, states: insertAt(workflow.states, index, state) }));
}

export function updateState(draft: SettingsDraft, workflowKey: string, stateKey: string, patch: StatePatch): SettingsDraft {
  return mapWorkflow(draft, workflowKey, (workflow) => ({
    ...workflow,
    states: workflow.states.map((state) => (state.key === stateKey ? { ...state, ...patch } : state)),
  }));
}

/** 往前（-1）或往後（1）移一格；已經在頭或尾就不動。 */
export function moveState(draft: SettingsDraft, workflowKey: string, stateKey: string, direction: -1 | 1): SettingsDraft {
  const workflow = draft.workflows.find((candidate) => candidate.key === workflowKey);
  const index = workflow?.states.findIndex((state) => state.key === stateKey) ?? -1;
  const target = index + direction;
  if (workflow === undefined || index < 0 || target < 0 || target >= workflow.states.length) {
    return draft;
  }
  const states = workflow.states.map((state, position) => {
    if (position === index) return workflow.states[target] ?? state;
    if (position === target) return workflow.states[index] ?? state;
    return state;
  });
  return mapWorkflow(draft, workflowKey, (current) => ({ ...current, states }));
}

/**
 * 刪除狀態。原本就有的狀態要指定項目搬去哪裡（先記成還沒選）；剛新增的直接拿掉。
 * 別的被刪除的狀態原本選了它當目標的，目標清掉重新選。
 */
export function removeState(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string, stateKey: string): SettingsDraft {
  const original = base.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
  const wasInBlueprint = original.some((state) => state.key === stateKey);
  return mapWorkflow(draft, workflowKey, (workflow) => {
    const moves = workflow.moves.map((move) => (move.to === stateKey ? { ...move, to: '' } : move));
    const touches = (transition: WorkflowTransition) => transition.from === stateKey || transition.to === stateKey;
    return {
      ...workflow,
      states: workflow.states.filter((state) => state.key !== stateKey),
      moves: wasInBlueprint ? [...moves, { from: stateKey, to: '' }] : moves,
      transitions: wasInBlueprint ? workflow.transitions : workflow.transitions?.filter((transition) => !touches(transition)),
    };
  });
}

export function setMoveTarget(draft: SettingsDraft, workflowKey: string, from: string, to: string): SettingsDraft {
  return mapWorkflow(draft, workflowKey, (workflow) => ({
    ...workflow,
    moves: workflow.moves.map((move) => (move.from === from ? { ...move, to } : move)),
  }));
}

/** 復原刪除的狀態：名稱與類別回到目前藍圖裡的樣子，放回原本的位置。 */
export function restoreState(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string, stateKey: string): SettingsDraft {
  const original = base.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
  const state = original.find((candidate) => candidate.key === stateKey);
  if (state === undefined) {
    return draft;
  }
  return mapWorkflow(draft, workflowKey, (workflow) => ({
    ...workflow,
    states: restoreInOrder(workflow.states, original, state, byKey),
    moves: workflow.moves.filter((move) => move.from !== stateKey),
  }));
}

export function addField(draft: SettingsDraft, field: FieldDraft): SettingsDraft {
  return { ...draft, fields: [...draft.fields, field] };
}

export function renameField(draft: SettingsDraft, fieldKey: string, name: string): SettingsDraft {
  return mapField(draft, fieldKey, (field) => ({ ...field, name }));
}

export function setFieldTypes(draft: SettingsDraft, fieldKey: string, appliesTo: readonly string[]): SettingsDraft {
  return mapField(draft, fieldKey, (field) => ({ ...field, appliesTo }));
}

export function addOption(draft: SettingsDraft, fieldKey: string, option: FieldOption): SettingsDraft {
  return mapField(draft, fieldKey, (field) => ({ ...field, options: [...(field.options ?? []), option] }));
}

export function renameOption(draft: SettingsDraft, fieldKey: string, optionKey: string, name: string): SettingsDraft {
  return mapField(draft, fieldKey, (field) => ({
    ...field,
    options: field.options?.map((option) => (option.key === optionKey ? { ...option, name } : option)),
  }));
}

/** 只給還沒送出的新選項用：既有的選項不能刪除（已經用到它的項目會對不到值），畫面不提供這個操作。 */
export function removeOption(draft: SettingsDraft, fieldKey: string, optionKey: string): SettingsDraft {
  return mapField(draft, fieldKey, (field) => ({
    ...field,
    options: field.options?.filter((option) => option.key !== optionKey),
  }));
}

export function removeField(base: ProjectBlueprint, draft: SettingsDraft, fieldKey: string): SettingsDraft {
  const removed = { ...draft, fields: draft.fields.filter((field) => field.key !== fieldKey) };
  if (base.fields.some((field) => field.key === fieldKey)) {
    return removed;
  }
  return stripTransitionReferences(removed, (transition) =>
    transition.requireFields?.includes(fieldKey) === true
      ? { ...transition, requireFields: transition.requireFields.filter((field) => field !== fieldKey) }
      : transition,
  );
}

/** 復原刪除的欄位：定義與套用的類型都回到目前藍圖裡的樣子，放回原本的位置。 */
export function restoreField(base: ProjectBlueprint, draft: SettingsDraft, fieldKey: string): SettingsDraft {
  const original = draftFromBlueprint(base).fields;
  const field = original.find((candidate) => candidate.key === fieldKey);
  return field === undefined ? draft : { ...draft, fields: restoreInOrder(draft.fields, original, field, byKey) };
}
