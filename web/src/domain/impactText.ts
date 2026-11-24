import type { Workflow } from '../api/blueprint';
import type { Impact } from '../api/types';
import { nameIn, roleName, type ChangeSetBlueprints } from './changeSetNames';
import { formatNumber } from './fields';
import { FIELD_TYPE_LABELS } from './labels';

function quoted(name: string): string {
  return `「${name}」`;
}

type StateMoveImpact = Extract<Impact, { kind: 'state_move' }>;

function hasState(workflow: Workflow, key: string): boolean {
  return workflow.states.some((state) => state.key === key);
}

function stateNameIn(workflow: Workflow, key: string): string {
  return workflow.states.find((state) => state.key === key)?.name ?? key;
}

/**
 * 這條搬移的舊狀態從哪些流程來（照基準版本的流程順序）：刪除狀態的是這個流程本身，類型換流程的是那些類型原本的流程。
 * 伺服器每個（流程, 舊狀態）只認一條指示，兩種情況同一個 key 時是同一條，兩邊都要列。
 */
function moveOrigins(impact: StateMoveImpact, { before, after }: ChangeSetBlueprints): readonly Workflow[] {
  const switchedFrom = new Set(
    before.itemTypes
      .filter((type) => type.workflow !== impact.workflow)
      .filter((type) => after.itemTypes.some((next) => next.key === type.key && next.workflow === impact.workflow))
      .map((type) => type.workflow),
  );
  return before.workflows.filter(
    (workflow) => hasState(workflow, impact.from) && (workflow.key === impact.workflow || switchedFrom.has(workflow.key)),
  );
}

/**
 * 搬移的兩端：被刪除的狀態只在基準版本的藍圖裡有，搬去的狀態在變更集的藍圖裡。
 * 舊狀態的名稱有歧義時帶流程的名稱：舊狀態在另一個流程（類型換流程，搬去的狀態也帶），或同一個 key 在好幾個流程都有。
 */
function moveEnds(impact: StateMoveImpact, context: ChangeSetBlueprints): readonly [string, string] {
  const { before, after } = context;
  const to = quoted(nameIn(after, before, 'state', impact.to, impact.workflow));
  const origins = moveOrigins(impact, context);
  const [only] = origins;
  if (only === undefined) {
    return [quoted(nameIn(before, after, 'state', impact.from, impact.workflow)), to];
  }
  const isShared = before.workflows.filter((workflow) => hasState(workflow, impact.from)).length > 1;
  const isElsewhere = origins.some((workflow) => workflow.key !== impact.workflow);
  if (origins.length === 1 && !isShared && !isElsewhere) {
    return [quoted(stateNameIn(only, impact.from)), to];
  }
  const from = origins.map((workflow) => `${quoted(stateNameIn(workflow, impact.from))}（${workflow.name}）`).join('、');
  return [from, isElsewhere ? `${to}（${nameIn(after, before, 'workflow', impact.workflow)}）` : to];
}

function stateMoveText(impact: StateMoveImpact, context: ChangeSetBlueprints, isCounted: boolean): string {
  const [from, to] = moveEnds(impact, context);
  return isCounted ? `${formatNumber(impact.affected)} 個項目會從${from}搬到${to}` : `停在${from}的項目搬到${to}`;
}

function conversionText(impact: Extract<Impact, { kind: 'field_conversion' }>, context: ChangeSetBlueprints, isCounted: boolean): string {
  const field = quoted(nameIn(context.after, context.before, 'field', impact.field));
  const types = `從${quoted(FIELD_TYPE_LABELS[impact.fromType])}轉成${quoted(FIELD_TYPE_LABELS[impact.toType])}`;
  const failure = {
    leave_empty: '，轉不過去的會清空（原本的值封存起來）',
    abort: '，有轉不過去的就整份不套用',
  };
  const tail = impact.onFailure === null ? '' : failure[impact.onFailure];
  return isCounted ? `${formatNumber(impact.affected)} 個項目的${field}會${types}${tail}` : `${field}的值${types}${tail}`;
}

/** 欄位從類型拿掉（欄位本身還在）：那個類型的項目的值會封存起來。 */
function detachText(impact: Extract<Impact, { kind: 'field_detach' }>, context: ChangeSetBlueprints, isCounted: boolean): string {
  const field = quoted(nameIn(context.after, context.before, 'field', impact.field));
  const type = quoted(nameIn(context.after, context.before, 'item_type', impact.itemType));
  return isCounted ? `${formatNumber(impact.affected)} 個${type}的${field}會被封存` : `${field}從${type}拿掉，值封存起來`;
}

/** 類型刪除、上層限制縮小、角色刪除：數量是擋住套用的項目（或成員），是 0 才能套用。 */
function blockingText(impact: Impact, context: ChangeSetBlueprints, isCounted: boolean): string {
  const count = formatNumber(impact.affected);
  const typeName = (key: string) => quoted(nameIn(context.before, context.after, 'item_type', key));
  switch (impact.kind) {
    case 'item_type_removal':
      if (!isCounted) return `刪除類型${typeName(impact.itemType)}`;
      return impact.affected > 0
        ? `還有 ${count} 個${typeName(impact.itemType)}：要先在項目頁刪除這些項目或改成別的類型，才能刪除這個類型`
        : `沒有${typeName(impact.itemType)}，可以直接刪除這個類型`;
    case 'parent_removal':
      if (!isCounted) return `${typeName(impact.itemType)}不能再掛在${typeName(impact.parent)}底下`;
      return impact.affected > 0
        ? `還有 ${count} 個${typeName(impact.itemType)}掛在${typeName(impact.parent)}底下，要先移開`
        : `沒有${typeName(impact.itemType)}掛在${typeName(impact.parent)}底下`;
    case 'role_removal':
      if (!isCounted) return `刪除角色${quoted(roleName(context, impact.role))}`;
      return impact.affected > 0
        ? `還有 ${count} 位成員是${quoted(roleName(context, impact.role))}角色，要先換成別的角色`
        : `沒有成員是${quoted(roleName(context, impact.role))}角色`;
    default:
      return '';
  }
}

/**
 * 一項資料影響說成一句話加數量，例如「12 個項目會從「待驗收」搬到「內部驗收」」。
 * isCounted 是 false 時（不是等待核准的變更集，伺服器不再算數量）只說會做什麼。
 */
export function describeImpact(impact: Impact, context: ChangeSetBlueprints, isCounted: boolean): string {
  switch (impact.kind) {
    case 'state_move':
      return stateMoveText(impact, context, isCounted);
    case 'field_drop': {
      const field = quoted(nameIn(context.before, context.after, 'field', impact.field));
      return isCounted ? `${formatNumber(impact.affected)} 個項目的${field}會被封存` : `${field}的值封存起來`;
    }
    case 'field_conversion':
      return conversionText(impact, context, isCounted);
    case 'field_detach':
      return detachText(impact, context, isCounted);
    default:
      return blockingText(impact, context, isCounted);
  }
}

const TOUCHING_KINDS: ReadonlySet<Impact['kind']> = new Set(['state_move', 'field_drop', 'field_detach', 'field_conversion']);

/** 這一項會改到項目的資料（搬移、封存、轉換）；其他的（類型、上層、角色）是擋住套用的條件。 */
export function touchesItems(impact: Impact): boolean {
  return TOUCHING_KINDS.has(impact.kind);
}

/** 會被改到資料的項目數加總（搬移、封存、轉換）。同一個項目可能同時被搬移又封存，所以這是上限。 */
export function itemsTouched(impacts: readonly Impact[]): number {
  return impacts.filter(touchesItems).reduce((sum, impact) => sum + impact.affected, 0);
}

/** 一行影響：文字，以及它是不是擋住套用。 */
export interface ImpactLine {
  readonly text: string;
  readonly blocksApply: boolean;
}

function isIdleMove(impact: Impact): impact is StateMoveImpact {
  return impact.kind === 'state_move' && impact.affected === 0;
}

/**
 * 影響的清單，一項一行。等待核准時，沒有項目要搬的狀態合成一行，放在其他搬移的後面：
 * 類型換流程時舊流程的每個狀態都列一條，大多是 0，逐條列出來反而看不到真的會動到的。
 */
export function impactLines(impacts: readonly Impact[], context: ChangeSetBlueprints, isCounted: boolean): readonly ImpactLine[] {
  const lineOf = (impact: Impact) => ({ text: describeImpact(impact, context, isCounted), blocksApply: impact.blocksApply });
  const idle = isCounted ? impacts.filter(isIdleMove) : [];
  if (idle.length === 0) {
    return impacts.map(lineOf);
  }
  // 標成 boolean：不然 TypeScript 會推成「不是搬移」，但數量不是 0 的搬移也在這裡。
  const busy = impacts.filter((impact): boolean => !isIdleMove(impact));
  const lastMove = busy.findLastIndex((impact) => impact.kind === 'state_move');
  const names = idle.map((impact) => moveEnds(impact, context)[0]).join('、');
  const merged = {
    text: `${lastMove < 0 ? '' : '其他 '}${formatNumber(idle.length)} 個狀態沒有項目要搬：${names}`,
    blocksApply: idle.some((impact) => impact.blocksApply),
  };
  const lines = busy.map(lineOf);
  return [...lines.slice(0, lastMove + 1), merged, ...lines.slice(lastMove + 1)];
}
