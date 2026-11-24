import type { QueryKey } from '@tanstack/react-query';
import type { Item, ItemSortField } from '../../api/types';
import { sortFieldKey } from '../../domain/fieldFilters';

/**
 * 一份項目清單的條件與排序（查詢鍵的第四段，queryKeys.items、queryKeys.boardColumn），只取判斷要用的。
 * 沒有這個條件時是 undefined。
 */
export interface ListConditions {
  readonly type: readonly string[] | undefined;
  readonly state: readonly string[] | undefined;
  readonly stateCategory: readonly string[] | undefined;
  readonly assignee: string | undefined;
  readonly priority: readonly string[] | undefined;
  readonly dueFrom: string | undefined;
  readonly dueTo: string | undefined;
  readonly noDue: boolean | undefined;
  readonly datesFrom: string | undefined;
  readonly datesTo: string | undefined;
  readonly cycleId: string | undefined;
  readonly noCycle: boolean | undefined;
  readonly milestoneId: string | undefined;
  readonly noMilestone: boolean | undefined;
  readonly q: string | undefined;
  readonly parent: string | undefined;
  /** 自訂欄位的條件：欄位的 key 對到值（`ItemQuery.fields`）。 */
  readonly fields: Readonly<Record<string, readonly string[]>> | undefined;
  readonly sortField: ItemSortField | undefined;
}

type CoreSortField = Exclude<ItemSortField, `field.${string}`>;

const SORT_VALUES: Readonly<Record<CoreSortField, (item: Item) => unknown>> = {
  number: (item) => item.number,
  title: (item) => item.title,
  state: (item) => item.state,
  priority: (item) => item.priority,
  start_date: (item) => item.startDate,
  due_date: (item) => item.dueDate,
  estimate: (item) => item.estimate,
  created_at: (item) => item.createdAt,
  updated_at: (item) => item.updatedAt,
};

function isCoreSortField(value: string): value is CoreSortField {
  return Object.hasOwn(SORT_VALUES, value);
}

function isSortField(value: unknown): value is ItemSortField {
  return typeof value === 'string' && (isCoreSortField(value) || sortFieldKey(value) !== null);
}

/** 排序看的值；自訂欄位看 item.fields 裡那一個（比較用 JSON，多選的陣列也比得出有沒有變）。 */
function sortValueOf(field: ItemSortField): (item: Item) => unknown {
  const key = sortFieldKey(field);
  if (key !== null) return (item) => JSON.stringify(item.fields[key] ?? null);
  return isCoreSortField(field) ? SORT_VALUES[field] : () => null;
}

function fieldConditionsOf(value: unknown): Readonly<Record<string, readonly string[]>> | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const entries = Object.entries(value).flatMap(([key, values]): [string, readonly string[]][] => {
    const strings = stringsOf(values);
    return strings === undefined ? [] : [[key, strings]];
  });
  return entries.length === 0 ? undefined : Object.fromEntries(entries);
}

function stringsOf(value: unknown): readonly string[] | undefined {
  return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string') : undefined;
}

function textOf(value: unknown): string | undefined {
  return typeof value === 'string' && value !== '' ? value : undefined;
}

/** queryIndex：條件在鍵的第幾段（專案的清單是第 3 段，「我的工作」的是第 2 段）。 */
export function conditionsOf(queryKey: QueryKey, queryIndex = 3): ListConditions {
  const query = queryKey[queryIndex];
  const read = (target: unknown, name: string): unknown =>
    typeof target === 'object' && target !== null ? Reflect.get(target, name) : undefined;
  const sortField = read(read(query, 'sort'), 'field');
  return {
    type: stringsOf(read(query, 'type')),
    state: stringsOf(read(query, 'state')),
    stateCategory: stringsOf(read(query, 'stateCategory')),
    assignee: textOf(read(query, 'assignee')),
    priority: stringsOf(read(query, 'priority')),
    dueFrom: textOf(read(query, 'dueFrom')),
    dueTo: textOf(read(query, 'dueTo')),
    noDue: read(query, 'noDue') === true ? true : undefined,
    datesFrom: textOf(read(query, 'datesFrom')),
    datesTo: textOf(read(query, 'datesTo')),
    cycleId: textOf(read(query, 'cycleId')),
    noCycle: read(query, 'noCycle') === true ? true : undefined,
    milestoneId: textOf(read(query, 'milestoneId')),
    noMilestone: read(query, 'noMilestone') === true ? true : undefined,
    q: textOf(read(query, 'q')),
    parent: textOf(read(query, 'parent')),
    fields: fieldConditionsOf(read(query, 'fields')),
    sortField: isSortField(sortField) ? sortField : undefined,
  };
}

/**
 * 這次修改有沒有動到清單的條件或排序看的欄位：有的話，項目可能進出這份清單或換了位置。
 * 後端的游標是位移量，已載入的範圍裡多一個、少一個或換位置，「載入更多」就會重複或漏掉。
 */
export function touchesConditions(conditions: ListConditions, before: Item, after: Item): boolean {
  const checks: readonly (readonly [unknown, (item: Item) => unknown])[] = [
    [conditions.type, (item) => item.type],
    [conditions.state, (item) => item.state],
    [conditions.stateCategory, (item) => item.stateCategory],
    [conditions.assignee, (item) => item.assignee?.personId ?? null],
    [conditions.priority, (item) => item.priority],
    [conditions.dueFrom, (item) => item.dueDate],
    [conditions.dueTo, (item) => item.dueDate],
    [conditions.noDue, (item) => item.dueDate],
    // 時間軸的「沒有日期」只數沒有開始日的：開始日變了也算動到條件。
    [conditions.noDue, (item) => item.startDate],
    // 期間和這段時間有沒有重疊，看開始日與到期日。
    [conditions.datesFrom, (item) => item.startDate],
    [conditions.datesFrom, (item) => item.dueDate],
    [conditions.datesTo, (item) => item.startDate],
    [conditions.datesTo, (item) => item.dueDate],
    [conditions.cycleId, (item) => item.cycleId],
    [conditions.noCycle, (item) => item.cycleId],
    [conditions.milestoneId, (item) => item.milestoneId],
    [conditions.noMilestone, (item) => item.milestoneId],
    [conditions.q, (item) => item.title],
    [conditions.parent, (item) => item.parentKey],
    [conditions.sortField, conditions.sortField === undefined ? () => null : sortValueOf(conditions.sortField)],
  ];
  const fieldChecks = Object.keys(conditions.fields ?? {}).map(
    (key) => [true, (item: Item) => JSON.stringify(item.fields[key] ?? null)] as const,
  );
  return [...checks, ...fieldChecks].some(
    ([condition, valueOf]) => condition !== undefined && valueOf(before) !== valueOf(after),
  );
}

/**
 * 看板的欄：狀態與類型前端判斷得出來。依負責人、欄位分欄的看板是一份清單、不分狀態（只有「沒結束」的狀態類別），
 * 沒有狀態條件時看類別。
 */
export function acceptsStateAndType(conditions: ListConditions, item: Item): boolean {
  const hasState = conditions.state?.includes(item.state) ?? conditions.stateCategory?.includes(item.stateCategory) ?? false;
  return hasState && (conditions.type === undefined || conditions.type.includes(item.type));
}

/**
 * 負責人（「我」是誰、「沒有負責人」）、搜尋（標題的部分比對、項目編號）、優先度、到期（「今天」「本週」已換成日期，
 * 但日期的比對與「沒有到期日」仍以伺服器為準）、「已過期」帶的狀態類別，自訂欄位的條件，以及週期與里程碑（「目前的週期」已換成編號，
 * 但週期是不是還在、屬於哪個專案以伺服器為準）由後端判斷，前端不猜：
 * 有這些條件的清單要不要放進某個項目，交給重抓。
 */
export function dependsOnServer(conditions: ListConditions): boolean {
  return (
    conditions.assignee !== undefined ||
    conditions.q !== undefined ||
    conditions.priority !== undefined ||
    conditions.dueFrom !== undefined ||
    conditions.dueTo !== undefined ||
    conditions.noDue !== undefined ||
    conditions.datesFrom !== undefined ||
    conditions.datesTo !== undefined ||
    conditions.cycleId !== undefined ||
    conditions.noCycle !== undefined ||
    conditions.milestoneId !== undefined ||
    conditions.noMilestone !== undefined ||
    conditions.stateCategory !== undefined ||
    // 欄位值的比對（選項、人員、多選的「或」）交給後端，前端不猜。
    Object.keys(conditions.fields ?? {}).length > 0
  );
}
