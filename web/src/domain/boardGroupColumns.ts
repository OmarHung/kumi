import type { FieldDefinition, ProjectBlueprint, StateCategory } from '../api/blueprint';
import type { Cycle } from '../api/planning';
import type { CalendarDate, Item, ItemPatch, ItemQuery, Member } from '../api/types';
import type { WeekStart } from '../lib/weekStart';
import type { BoardGroup } from './boardGroup';
import { dueQuery } from './dueFilter';
import { typeHasField } from './fields';
import { FIELD_NONE } from './fieldFilters';
import { fieldsQuery } from './itemFilters';
import { cycleQuery, milestoneQuery } from './planningFilter';
import type { BoardFilters } from './projectView';

/**
 * 看板依負責人、依單選欄位分欄時的規則（docs/DESIGN.md 7.2「分組」）。全部是純邏輯：
 * 欄怎麼排、卡片在哪一欄、拖到哪一欄要改什麼、哪些放不下去。
 */

/** 分組的看板只放還沒結束的項目：完成與取消的不佔欄位。 */
export const OPEN_CATEGORIES: readonly StateCategory[] = ['backlog', 'not_started', 'in_progress'];

/** 分組時一次查一份清單，不是一欄查一次：客戶維運的「客戶」可能有幾十個選項。 */
export const BOARD_GROUP_PAGE_SIZE = 200;

/** 「沒有負責人」「沒有填」那一欄的 key。選項的 key 一定是小寫字母開頭、人員是編號，不會撞到。 */
export const NO_VALUE_KEY = FIELD_NONE;

export interface GroupColumn {
  readonly key: string;
  readonly name: string;
  /** 是「沒有負責人」「沒有填」那一欄：永遠在最後、永遠有。 */
  readonly isEmpty: boolean;
  /** 能把卡片放進來：不是專案成員的負責人、藍圖裡已經沒有的選項只是讓項目有地方放，不能當落點。 */
  readonly isTarget: boolean;
}

/** 分組的清單條件：看板的篩選，加上沒結束的類別，不分類型與狀態。 */
export function boardGroupQuery(
  filters: BoardFilters,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  cycles: readonly Cycle[] = [],
): ItemQuery {
  return {
    stateCategory: OPEN_CATEGORIES,
    ...(filters.assignee !== null && { assignee: filters.assignee }),
    ...(filters.priority !== null && { priority: [filters.priority] }),
    ...dueQuery(filters.due, todayDate, weekStart),
    ...cycleQuery(filters.cycle, cycles, todayDate),
    ...milestoneQuery(filters.milestone),
    ...fieldsQuery(filters.fields),
    ...(filters.q !== '' && { q: filters.q }),
    sort: { field: 'number', direction: 'asc' },
    limit: BOARD_GROUP_PAGE_SIZE,
  };
}

function fieldOf(blueprint: ProjectBlueprint, key: string): FieldDefinition | undefined {
  return blueprint.fields.find((field) => field.key === key);
}

/** 分組的名稱：「負責人」或欄位的名稱（給選單、說明與報讀用）。 */
export function groupName(group: BoardGroup, blueprint: ProjectBlueprint): string {
  if (group.kind === 'assignee') return '負責人';
  if (group.kind === 'field') return fieldOf(blueprint, group.key)?.name ?? group.key;
  return '狀態';
}

/** 「沒有負責人」「沒有填」那一欄的名稱。 */
export function emptyColumnName(group: BoardGroup): string {
  return group.kind === 'assignee' ? '沒有負責人' : '沒有填';
}

/** 這張卡片在哪一欄（依狀態分組時不用這個）。欄位的值不是字串（資料壞了）當成沒有填。 */
export function columnKeyOf(item: Item, group: BoardGroup): string {
  if (group.kind === 'assignee') return item.assignee?.personId ?? NO_VALUE_KEY;
  if (group.kind === 'field') {
    const value = item.fields[group.key];
    return typeof value === 'string' && value !== '' ? value : NO_VALUE_KEY;
  }
  return item.state;
}

function emptyColumn(group: BoardGroup): GroupColumn {
  return { key: NO_VALUE_KEY, name: emptyColumnName(group), isEmpty: true, isTarget: true };
}

function assigneeColumns(members: readonly Member[], items: readonly Item[]): readonly GroupColumn[] {
  const memberIds = new Set(members.map((member) => member.personId));
  // 已經不是成員、但還有項目的人：欄留著，項目才有地方放；它不是拖得過去的落點（沒有成員資格）。
  const former = new Map(
    items.flatMap((item) => (item.assignee !== null && !memberIds.has(item.assignee.personId) ? [[item.assignee.personId, item.assignee.displayName] as const] : [])),
  );
  return [
    ...members.map((member) => ({ key: member.personId, name: member.displayName, isEmpty: false, isTarget: true })),
    ...[...former].map(([key, name]) => ({ key, name, isEmpty: false, isTarget: false })),
  ];
}

function optionColumns(field: FieldDefinition | undefined, items: readonly Item[], group: BoardGroup): readonly GroupColumn[] {
  const used = new Set(items.map((item) => columnKeyOf(item, group)));
  const options = field?.options ?? [];
  const known = new Set(options.map((option) => option.key));
  return [
    ...options.filter((option) => used.has(option.key)).map((option) => ({ key: option.key, name: option.name, isEmpty: false, isTarget: true })),
    // 藍圖裡已經沒有的選項、但項目還帶著：照原本的 key 列出，不讓項目消失。
    ...[...used].filter((key) => key !== NO_VALUE_KEY && !known.has(key)).map((key) => ({ key, name: key, isEmpty: false, isTarget: false })),
  ];
}

/**
 * 看板的欄。負責人：每一位專案成員一欄（沒有項目也要有，才拖得過去），再來是不是成員的負責人，最後是「沒有負責人」；
 * 單選欄位：只列有項目的選項（照選項的順序），最後是「沒有填」。其他選項用卡片的「移到…」選。
 */
export function groupColumns(group: BoardGroup, blueprint: ProjectBlueprint, members: readonly Member[], items: readonly Item[]): readonly GroupColumn[] {
  if (group.kind === 'assignee') return [...assigneeColumns(members, items), emptyColumn(group)];
  if (group.kind === 'field') return [...optionColumns(fieldOf(blueprint, group.key), items, group), emptyColumn(group)];
  return [];
}

/** 不能當落點的欄（不是成員的負責人、藍圖裡已經沒有的選項）放不進去的原因。 */
export function notTargetReason(group: BoardGroup): string {
  return group.kind === 'assignee' ? '不是這個專案的成員' : '這個選項已經不在欄位裡';
}

/** 這張卡片放不進這一欄的原因；放得進去是 null。類型沒有這個欄位的項目，只能在「沒有填」。 */
export function dropBlockedReason(item: Item, group: BoardGroup, columnKey: string, blueprint: ProjectBlueprint): string | null {
  if (group.kind !== 'field' || columnKey === NO_VALUE_KEY || typeHasField(blueprint, item.type, group.key)) return null;
  return `這個類型沒有「${groupName(group, blueprint)}」`;
}

/** 移到這一欄要送的修改：負責人是 `assigneeId`，欄位是 `fields`；「沒有…」那一欄是 null（清掉）。 */
export function groupPatch(group: BoardGroup, columnKey: string): ItemPatch {
  const value = columnKey === NO_VALUE_KEY ? null : columnKey;
  if (group.kind === 'assignee') return { assigneeId: value };
  if (group.kind === 'field') return { fields: { [group.key]: value } };
  return {};
}

/** 送出中的卡片先畫在新的那一欄：照這次修改改好的樣子（伺服器的結果回來之前的暫時畫面）。 */
export function movedItem(item: Item, group: BoardGroup, columnKey: string, members: readonly Member[]): Item {
  if (group.kind === 'assignee') {
    const member = members.find((candidate) => candidate.personId === columnKey);
    return { ...item, assignee: member === undefined ? null : { personId: member.personId, displayName: member.displayName } };
  }
  if (group.kind === 'field') {
    const rest = Object.fromEntries(Object.entries(item.fields).filter(([key]) => key !== group.key));
    return { ...item, fields: columnKey === NO_VALUE_KEY ? rest : { ...rest, [group.key]: columnKey } };
  }
  return item;
}

/**
 * 「移到…」能選的所有目標（不只是看板上有欄的）：負責人是每一位成員，欄位是每一個選項，最後是「沒有…」。
 * 沒有欄的選項靠這裡選。
 */
export function optionTargets(group: BoardGroup, blueprint: ProjectBlueprint, members: readonly Member[]): readonly GroupColumn[] {
  const none = emptyColumn(group);
  if (group.kind === 'assignee') {
    return [...members.map((member) => ({ key: member.personId, name: member.displayName, isEmpty: false, isTarget: true })), none];
  }
  if (group.kind === 'field') {
    return [...(fieldOf(blueprint, group.key)?.options ?? []).map((option) => ({ key: option.key, name: option.name, isEmpty: false, isTarget: true })), none];
  }
  return [];
}
