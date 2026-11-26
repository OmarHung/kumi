import type { StateCategory } from '../api/blueprint';
import type { CalendarDate, ItemQuery, Priority } from '../api/types';
import { addDays, endOfWeek, isOverdue, isTerminal } from './dates';
import type { WeekStart } from '../lib/weekStart';
import { PRIORITY_LABELS } from './labels';
import { DUE_SOON_DAYS } from './overviewRules';

/**
 * 篩選的「到期」：已過期、今天、本週（今天到這一週的最後一天，一週從哪天開始看個人偏好）、
 * 7 天內（今天到 6 天後，專案總覽的「快到期」，和一週從哪天開始無關）、沒有到期日。
 */
export type DueFilter = 'overdue' | 'today' | 'week' | 'soon' | 'none';

export const DUE_FILTERS: readonly DueFilter[] = ['overdue', 'today', 'week', 'soon', 'none'];

export const DUE_FILTER_LABELS: Readonly<Record<DueFilter, string>> = {
  overdue: '已過期',
  today: '今天到期',
  week: '本週到期',
  soon: `${String(DUE_SOON_DAYS)} 天內到期`,
  none: '沒有到期日',
};

export const PRIORITIES: readonly Priority[] = ['urgent', 'high', 'medium', 'low', 'none'];

/** 優先度下拉的選項文字：和項目上顯示的同一套（domain/labels.ts），篩選與項目不要一個說「沒有」一個說「未設定」。 */
export const PRIORITY_FILTER_LABELS: Readonly<Record<Priority, string>> = PRIORITY_LABELS;

export function parseDueFilter(value: string | null): DueFilter | null {
  return DUE_FILTERS.find((due) => due === value) ?? null;
}

export function parsePriority(value: string | null): Priority | null {
  return PRIORITIES.find((priority) => priority === value) ?? null;
}

/** 還沒完成的狀態類別：「已過期」不含完成與取消的（和表格上過期的標示一致）。 */
export const OPEN_CATEGORIES: readonly StateCategory[] = ['backlog', 'not_started', 'in_progress'];

type DueQuery = Pick<ItemQuery, 'dueFrom' | 'dueTo' | 'noDue' | 'stateCategory'>;

/**
 * 換成 API 的參數。「今天」由呼叫端給（使用者所在地的日期）：伺服器不知道時區，
 * 所以「已過期」「本週」都在這裡換成日期。
 */
export function dueQuery(due: DueFilter | null, todayDate: CalendarDate, weekStart: WeekStart): DueQuery {
  switch (due) {
    case 'overdue':
      return { dueTo: addDays(todayDate, -1), stateCategory: OPEN_CATEGORIES };
    case 'today':
      return { dueFrom: todayDate, dueTo: todayDate };
    case 'week':
      return { dueFrom: todayDate, dueTo: endOfWeek(todayDate, weekStart) };
    // 和總覽的「快到期」同一個定義（還沒結束），點進來的筆數才對得上。
    case 'soon':
      return { dueFrom: todayDate, dueTo: addDays(todayDate, DUE_SOON_DAYS - 1), stateCategory: OPEN_CATEGORIES };
    case 'none':
      return { noDue: true };
    case null:
      return {};
  }
}

/** 一個項目符不符合到期的條件，和 dueQuery 送給後端的是同一個意思（只用在還沒進伺服器結果的卡片）。 */
export function matchesDue(
  due: DueFilter | null,
  dueDate: CalendarDate | null,
  category: StateCategory,
  todayDate: CalendarDate,
  weekStart: WeekStart,
): boolean {
  switch (due) {
    case null:
      return true;
    case 'overdue':
      return isOverdue(dueDate, category, todayDate);
    case 'today':
      return dueDate === todayDate;
    case 'week':
      return dueDate !== null && dueDate >= todayDate && dueDate <= endOfWeek(todayDate, weekStart);
    case 'soon':
      return dueDate !== null && !isTerminal(category) && dueDate >= todayDate && dueDate <= addDays(todayDate, DUE_SOON_DAYS - 1);
    case 'none':
      return dueDate === null;
  }
}
