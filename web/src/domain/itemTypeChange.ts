import type { ProjectBlueprint, Workflow, WorkflowState } from '../api/blueprint';
import type { Item, Member } from '../api/types';
import { findItemType, itemTypeName, workflowForType } from './blueprint';
import { fieldDefinition, formatFieldValue } from './fields';
import { PERMISSION_LABELS } from './labels';

/**
 * 項目頁改類型之前的預測：狀態怎麼搬、哪些欄位的值會封存、上下層會不會擋下。
 * 規則照 internal/HANDOFF-week4-admin-ops.md 3A.1（伺服器決定狀態，呼叫端不能指定），和後端的 TypeChange 一致。
 * 前端只知道一部分（上層的類型要讀了才知道、下層可能只載入一頁），最後以伺服器為準。
 */

/** 算預測要用的專案內容。 */
export interface TypeChangeContext {
  readonly blueprint: ProjectBlueprint;
  readonly members: readonly Member[];
  /** 自己有沒有 item.transition：沒有時改類型一律回到起始狀態，不能借改類型改狀態。 */
  readonly canTransition: boolean;
}

/**
 * 狀態照哪一條規則決定：
 * kept 同 key、同類別，留著；same_category／first_state 新流程不限制轉換、自己可以改狀態，搬到第一個同類別的（沒有就第一個）；
 * restricted 新流程有限制轉換、no_permission 自己不能改狀態，兩者都回到起始狀態（不能借改類型跳過轉換的限制）。
 */
export type StateRule = 'kept' | 'same_category' | 'first_state' | 'restricted' | 'no_permission';

/** 會封存的一個欄位值。value 是給人看的文字；長文字不顯示內容，是 null。 */
export interface ArchivedValue {
  readonly key: string;
  readonly name: string;
  readonly value: string | null;
}

/** 已經知道的上下層。 */
export interface TypeChangeKnowledge {
  /** 上層項目的類型；沒有上層是 null，還不知道是 undefined。 */
  readonly parentType: string | null | undefined;
  /** 已經載入的下層（可能只有一部分）。 */
  readonly children: readonly Item[];
  /** 下層還有沒載入的（只讀了第一頁）。 */
  readonly hasMoreChildren: boolean;
}

export type ParentProblem =
  | { readonly kind: 'no_parents' }
  | { readonly kind: 'not_allowed'; readonly parentType: string };

export interface TypeChangePreview {
  readonly toType: string;
  /** 目前的狀態（舊流程裡的）。 */
  readonly stateFrom: WorkflowState | undefined;
  /** 改類型之後的狀態（新流程裡的）。 */
  readonly stateTo: WorkflowState | undefined;
  readonly movesState: boolean;
  readonly stateRule: StateRule;
  readonly archived: readonly ArchivedValue[];
  readonly parentProblem: ParentProblem | null;
  /** 已經知道的下層裡，類型不能掛在新類型底下的。 */
  readonly blockedChildren: readonly Item[];
  /** 只檢查了一部分的下層時，檢查了幾個；全部都檢查了是 null。 */
  readonly checkedChildren: number | null;
}

interface StateTarget {
  readonly state: WorkflowState | undefined;
  readonly rule: StateRule;
}

/** 照 3A.1 的三條規則決定改類型之後的狀態。 */
function targetState(context: TypeChangeContext, item: Item, toType: string): StateTarget {
  const workflow = workflowForType(context.blueprint, toType);
  const states = workflow?.states ?? [];
  const kept = states.find((state) => state.key === item.state && state.category === item.stateCategory);
  if (kept !== undefined) return { state: kept, rule: 'kept' };
  const first = states[0];
  // transitions 有寫（即使是空陣列）就是限制轉換；省略才是任何兩個狀態都可以互轉。
  if (workflow?.transitions !== undefined) return { state: first, rule: 'restricted' };
  if (!context.canTransition) return { state: first, rule: 'no_permission' };
  const sameCategory = states.find((state) => state.category === item.stateCategory);
  return sameCategory === undefined ? { state: first, rule: 'first_state' } : { state: sameCategory, rule: 'same_category' };
}

/** 和伺服器的 StringComparer.Ordinal 一樣，照 UTF-16 的編碼比。 */
function ordinal(a: string, b: string): number {
  if (a === b) return 0;
  return a < b ? -1 : 1;
}

/** 有值、新類型沒有的自訂欄位：先照舊類型定義的順序，再接其他有值的（照 key 排序），和伺服器記進活動的順序一樣。 */
function archivedValues(context: TypeChangeContext, item: Item, toType: string): readonly ArchivedValue[] {
  const { blueprint, members } = context;
  const kept = new Set(findItemType(blueprint, toType)?.fields ?? []);
  const ordered = (findItemType(blueprint, item.type)?.fields ?? []).filter((key) => item.fields[key] !== undefined);
  const others = Object.keys(item.fields).filter((key) => !ordered.includes(key)).toSorted(ordinal);
  return [...ordered, ...others].flatMap((key) => {
    const raw = item.fields[key];
    if (raw === undefined || kept.has(key)) return [];
    const definition = fieldDefinition(blueprint, key);
    const isContent = definition?.type === 'long_text';
    const value = isContent ? null : definition === undefined ? String(raw) : formatFieldValue(definition, raw, members);
    return [{ key, name: definition?.name ?? key, value }];
  });
}

function parentProblemOf(blueprint: ProjectBlueprint, item: Item, toType: string, parentType: string | null | undefined): ParentProblem | null {
  if (item.parentKey === null) return null;
  const allowed = findItemType(blueprint, toType)?.parents ?? [];
  if (allowed.length === 0) return { kind: 'no_parents' };
  return parentType === null || parentType === undefined || allowed.includes(parentType) ? null : { kind: 'not_allowed', parentType };
}

export function previewTypeChange(
  context: TypeChangeContext,
  item: Item,
  toType: string,
  knowledge: TypeChangeKnowledge,
): TypeChangePreview {
  const { blueprint } = context;
  const stateFrom = workflowForType(blueprint, item.type)?.states.find((state) => state.key === item.state);
  const target = targetState(context, item, toType);
  const blockedChildren = knowledge.children.filter(
    (child) => !(findItemType(blueprint, child.type)?.parents ?? []).includes(toType),
  );
  return {
    toType,
    stateFrom,
    stateTo: target.state,
    movesState: target.state !== undefined && target.state.key !== item.state,
    stateRule: target.rule,
    archived: archivedValues(context, item, toType),
    parentProblem: parentProblemOf(blueprint, item, toType, knowledge.parentType),
    blockedChildren,
    checkedChildren: knowledge.hasMoreChildren ? knowledge.children.length : null,
  };
}

/** 不限制轉換時為什麼搬：新流程沒有這個狀態，或有同 key 的、但類別不同。 */
function movedReason(workflow: Workflow | undefined, preview: TypeChangePreview): string {
  const workflowName = `「${workflow?.name ?? preview.toType}」`;
  const sameKey = workflow?.states.find((state) => state.key === preview.stateFrom?.key);
  if (sameKey !== undefined) return `${workflowName}的「${sameKey.name}」是另一個類別`;
  const from = preview.stateFrom?.name ?? '';
  return preview.stateRule === 'first_state' ? `${workflowName}沒有「${from}」，也沒有同類別的狀態` : `${workflowName}沒有「${from}」`;
}

/**
 * 狀態會怎麼樣，說得出為什麼：
 * 「狀態維持「開發」。」「狀態會從「內部驗收」搬到「進行中」：「任務流程」沒有「內部驗收」。」
 * 「狀態會從「進行中」回到起始狀態：「交付流程」有限制轉換，改類型之後從起始狀態「需求確認」開始，再照一般的方式改狀態。」
 */
export function stateSentence(blueprint: ProjectBlueprint, preview: TypeChangePreview): string {
  const from = preview.stateFrom?.name ?? '';
  const to = preview.stateTo?.name ?? '';
  if (!preview.movesState) {
    return `狀態維持「${to === '' ? from : to}」。`;
  }
  const workflow = workflowForType(blueprint, preview.toType);
  switch (preview.stateRule) {
    case 'restricted':
      return `狀態會從「${from}」回到起始狀態：「${workflow?.name ?? preview.toType}」有限制轉換，改類型之後從起始狀態「${to}」開始，再照一般的方式改狀態。`;
    case 'no_permission':
      return `狀態會從「${from}」回到起始狀態：你沒有「${PERMISSION_LABELS['item.transition']}」的權限，改類型之後從起始狀態「${to}」開始。`;
    default:
      return `狀態會從「${from}」搬到「${to}」：${movedReason(workflow, preview)}。`;
  }
}

/** 下層只載入了一部分時的提醒；全部都檢查了是空字串。 */
export function childrenNote(preview: TypeChangePreview): string {
  return preview.checkedChildren === null ? '' : `只檢查了前 ${String(preview.checkedChildren)} 個下層，送出後以伺服器為準。`;
}

/** 上層擋下的說明；沒有問題是空字串。 */
export function parentSentence(blueprint: ProjectBlueprint, item: Item, preview: TypeChangePreview): string {
  const problem = preview.parentProblem;
  const typeName = `「${itemTypeName(blueprint, preview.toType)}」`;
  if (problem === null) return '';
  if (problem.kind === 'no_parents') {
    return `${typeName}不能掛在任何類型底下，這個項目目前在 ${item.parentKey ?? ''} 底下：要先拿掉上層項目才能改。`;
  }
  const parentName = `「${itemTypeName(blueprint, problem.parentType)}」`;
  return `${typeName}不能掛在${parentName}底下（上層 ${item.parentKey ?? ''} 是${parentName}）：要先換掉上層項目才能改。`;
}
