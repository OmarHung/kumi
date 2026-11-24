import type { ProjectBlueprint, WorkflowState } from '../api/blueprint';
import { sameName, sameSet, type DraftStatus, type ItemTypeDraft, type SettingsDraft } from './settingsDraft';
import { visibleTransitions } from './settingsTransitions';

/**
 * 設定頁的類型：新增、改名、換流程、允許的上層。刪除類型這一版不做（底下有項目一定擋，而刪除項目、改類型都還沒有）。
 * 類型換流程時，舊流程有、新流程沒有的狀態要選搬到新流程的哪個狀態（伺服器的 StateMoveRules）。
 */

export type ItemTypePatch = Partial<Pick<ItemTypeDraft, 'name' | 'workflow' | 'parents'>>;

/** 類型換流程要搬的一個狀態：from 是舊流程（目前藍圖）的狀態，workflow 是新的流程，itemTypes 是需要它的類型（照草稿的順序）。 */
export interface TypeMove {
  readonly workflow: string;
  readonly from: WorkflowState;
  /** 新流程裡的狀態：使用者選的，沒選過就是預設的。 */
  readonly to: string;
  readonly itemTypes: readonly string[];
}

function mapItemType(draft: SettingsDraft, typeKey: string, update: (type: ItemTypeDraft) => ItemTypeDraft): SettingsDraft {
  return { ...draft, itemTypes: draft.itemTypes.map((type) => (type.key === typeKey ? update(type) : type)) };
}

/** 新類型接在最後，並套用 fieldKeys 這些自訂欄位（寫進欄位草稿的 appliesTo）。 */
export function addItemType(draft: SettingsDraft, type: ItemTypeDraft, fieldKeys: readonly string[]): SettingsDraft {
  return {
    ...draft,
    itemTypes: [...draft.itemTypes, type],
    fields: draft.fields.map((field) => (fieldKeys.includes(field.key) ? { ...field, appliesTo: [...field.appliesTo, type.key] } : field)),
  };
}

export function updateItemType(draft: SettingsDraft, typeKey: string, patch: ItemTypePatch): SettingsDraft {
  return mapItemType(draft, typeKey, (type) => ({ ...type, ...patch }));
}

/** 為（新流程, 舊狀態）選搬去的狀態；同一個鍵的類型共用這個選擇。 */
export function setTypeMoveTarget(draft: SettingsDraft, workflow: string, from: string, to: string): SettingsDraft {
  const others = draft.typeMoves.filter((move) => move.workflow !== workflow || move.from !== from);
  return { ...draft, typeMoves: [...others, { workflow, from, to }] };
}

/** 預設搬到新流程裡第一個同類別的狀態，沒有就新流程的第一個狀態。 */
function defaultTarget(from: WorkflowState, states: readonly WorkflowState[]): string {
  return (states.find((state) => state.category === from.category) ?? states[0])?.key ?? '';
}

/** 換了流程的類型：舊流程（目前藍圖）的狀態與新流程（草稿）。沒換、或是新類型時是 undefined。 */
function switchedType(base: ProjectBlueprint, draft: SettingsDraft, type: ItemTypeDraft) {
  const original = base.itemTypes.find((candidate) => candidate.key === type.key);
  const target = draft.workflows.find((workflow) => workflow.key === type.workflow);
  if (original === undefined || target === undefined || original.workflow === type.workflow) {
    return undefined;
  }
  return { oldStates: base.workflows.find((workflow) => workflow.key === original.workflow)?.states ?? [], target };
}

/** 一個類型換流程後要搬的舊狀態：新流程有同 key 的不用搬；新流程原本就有、在草稿裡刪掉的由「刪除狀態」的搬移處理。 */
function movesOfType(base: ProjectBlueprint, draft: SettingsDraft, type: ItemTypeDraft): readonly WorkflowState[] {
  const switched = switchedType(base, draft, type);
  if (switched === undefined) {
    return [];
  }
  const { oldStates, target } = switched;
  return oldStates
    .filter((state) => !target.states.some((candidate) => candidate.key === state.key))
    .filter((state) => !target.moves.some((move) => move.from === state.key));
}

/** 共用一條搬移的類型，各自在舊流程裡的那個狀態（key 相同，名稱與類別可能不同）。 */
interface MoveGroup {
  readonly workflow: string;
  readonly froms: readonly WorkflowState[];
  readonly itemTypes: readonly string[];
}

/**
 * 使用者選的；沒選過、或選的後來被刪掉了，就用預設。
 * 共用這條搬移的舊狀態類別不同時不預選：照哪一個類型的類別都可能搬錯，留給使用者選（送出前會擋）。
 */
function targetOf(draft: SettingsDraft, { workflow, froms }: MoveGroup): string {
  const [first] = froms;
  const states = draft.workflows.find((candidate) => candidate.key === workflow)?.states ?? [];
  const chosen = draft.typeMoves.find((move) => move.workflow === workflow && move.from === first?.key)?.to;
  if (chosen !== undefined && states.some((state) => state.key === chosen)) {
    return chosen;
  }
  const isMixed = froms.some((from) => from.category !== first?.category);
  return first === undefined || isMixed ? '' : defaultTarget(first, states);
}

function groupMoves(base: ProjectBlueprint, draft: SettingsDraft): readonly MoveGroup[] {
  return draft.itemTypes.reduce<readonly MoveGroup[]>((groups, type) => {
    return movesOfType(base, draft, type).reduce<readonly MoveGroup[]>((list, from) => {
      const index = list.findIndex((group) => group.workflow === type.workflow && group.froms[0]?.key === from.key);
      if (index < 0) {
        return [...list, { workflow: type.workflow, froms: [from], itemTypes: [type.key] }];
      }
      return list.map((group, position) =>
        position === index ? { ...group, froms: [...group.froms, from], itemTypes: [...group.itemTypes, type.key] } : group,
      );
    }, groups);
  }, []);
}

/**
 * 類型換流程要搬的狀態，照草稿裡類型的順序、再照舊流程裡狀態的順序（和伺服器一樣）。
 * 伺服器每個（新流程, 舊狀態）只認一條指示：兩個類型需要同一條時合成一條，記下兩個類型；列上的說明照第一個類型。
 */
export function requiredTypeMoves(base: ProjectBlueprint, draft: SettingsDraft): readonly TypeMove[] {
  return groupMoves(base, draft).flatMap((group) => {
    const [from] = group.froms;
    return from === undefined ? [] : [{ workflow: group.workflow, from, to: targetOf(draft, group), itemTypes: group.itemTypes }];
  });
}

/** 類型換流程時，舊狀態在新流程裡被刪除了：停在那裡的項目照「刪除狀態」的搬移走（to 還沒選是空字串）。 */
export interface CoveredMove {
  readonly workflow: string;
  /** 舊流程（目前藍圖）的狀態。 */
  readonly from: WorkflowState;
  /** 新流程裡被刪除、同 key 的狀態（目前藍圖）。 */
  readonly deleted: WorkflowState;
  readonly to: string;
}

/** 這個類型換流程後由「刪除狀態」的搬移涵蓋的舊狀態，照舊流程裡狀態的順序：列在類型那一列，使用者才知道不是漏了。 */
export function coveredTypeMoves(base: ProjectBlueprint, draft: SettingsDraft, typeKey: string): readonly CoveredMove[] {
  const type = draft.itemTypes.find((candidate) => candidate.key === typeKey);
  const switched = type === undefined ? undefined : switchedType(base, draft, type);
  if (type === undefined || switched === undefined) {
    return [];
  }
  const deletedStates = base.workflows.find((workflow) => workflow.key === type.workflow)?.states ?? [];
  return switched.oldStates.flatMap((from) => {
    const move = switched.target.moves.find((candidate) => candidate.from === from.key);
    const deleted = deletedStates.find((state) => state.key === from.key);
    return move === undefined || deleted === undefined ? [] : [{ workflow: type.workflow, from, deleted, to: move.to }];
  });
}

export function itemTypeStatus(base: ProjectBlueprint, draft: SettingsDraft, typeKey: string): DraftStatus {
  const original = base.itemTypes.find((type) => type.key === typeKey);
  const type = draft.itemTypes.find((candidate) => candidate.key === typeKey);
  if (original === undefined) {
    return 'added';
  }
  if (type === undefined) {
    return 'unchanged';
  }
  const isSame = sameName(type.name, original.name) && type.workflow === original.workflow && sameSet(type.parents, original.parents);
  return isSame ? 'unchanged' : 'modified';
}

/** 新類型預設要套用的自訂欄位：選定流程的轉換要求先填的（不套用的話那條轉換永遠走不過去）。照欄位的順序。 */
export function fieldsRequiredByWorkflow(draft: SettingsDraft, workflowKey: string): readonly string[] {
  const workflow = draft.workflows.find((candidate) => candidate.key === workflowKey);
  const transitions = workflow === undefined ? [] : (visibleTransitions(workflow) ?? []);
  const required = new Set(transitions.flatMap((transition) => transition.requireFields ?? []));
  return draft.fields.filter((field) => required.has(field.key)).map((field) => field.key);
}
