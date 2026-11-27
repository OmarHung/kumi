import type { StateCategory } from '../api/blueprint';
import type { CalendarDate } from '../api/types';
import type { WeekStart } from '../lib/weekStart';

/** 到期日是日曆日期，沒有時區；「今天」要用使用者所在地的日期，不能用 UTC。 */
export function toCalendarDate(date: Date): CalendarDate {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${String(date.getFullYear())}-${month}-${day}`;
}

export function today(): CalendarDate {
  return toCalendarDate(new Date());
}

export function isTerminal(category: StateCategory): boolean {
  return category === 'done' || category === 'canceled';
}

// YYYY-MM-DD 的字串順序就是日期順序，不必轉成 Date。
export function isOverdue(
  dueDate: CalendarDate | null,
  category: StateCategory,
  todayDate: CalendarDate,
): boolean {
  return dueDate !== null && !isTerminal(category) && dueDate < todayDate;
}

export function formatDate(date: CalendarDate): string {
  return date.replaceAll('-', '/');
}

/**
 * 窄螢幕清單用的簡短寫法：和今天同一年的只寫月與日（11/05），別的年份照完整的寫。
 * 清單一列右邊的欄放「已過期 2026/11/17」會吃掉標題的寬度，年份在今年時是多餘的。
 */
export function formatShortDate(date: CalendarDate, todayDate: CalendarDate): string {
  return date.slice(0, 4) === todayDate.slice(0, 4) ? date.slice(5).replace('-', '/') : formatDate(date);
}

/** 我的工作照急迫程度分的組，順序固定（docs/DESIGN.md 8.2：先看最急的）。 */
export type DueGroup = 'overdue' | 'today' | 'thisWeek' | 'later' | 'none';

export const DUE_GROUPS: readonly DueGroup[] = ['overdue', 'today', 'thisWeek', 'later', 'none'];

const DAYS_PER_WEEK = 7;

/** 日曆日期加減天數。用本地時間的建構子，月底、年底與日光節約時間都由 Date 處理。 */
export function addDays(date: CalendarDate, days: number): CalendarDate {
  const [year = 0, month = 1, day = 1] = date.split('-').map(Number);
  return toCalendarDate(new Date(year, month - 1, day + days));
}

function weekdayOf(date: CalendarDate): number {
  const [year = 0, month = 1, day = 1] = date.split('-').map(Number);
  return new Date(year, month - 1, day).getDay();
}

/** 這一天是一週的第幾天（0 起算）：一週從週一開始時週一是 0、週日是 6；從週日開始時週日是 0、週六是 6。 */
export function dayOfWeekIndex(date: CalendarDate, weekStart: WeekStart): number {
  // getDay()：週日是 0。從週一開始就把週日挪到最後。
  const offset = weekStart === 'monday' ? DAYS_PER_WEEK - 1 : 0;
  return (weekdayOf(date) + offset) % DAYS_PER_WEEK;
}

/** 這一天所在的那一週的第一天（那一天本身若是第一天就是它自己）。 */
export function startOfWeek(date: CalendarDate, weekStart: WeekStart): CalendarDate {
  return addDays(date, -dayOfWeekIndex(date, weekStart));
}

/** 這一天所在的那一週的最後一天：從週一開始是週日，從週日開始是週六（今天就是最後一天時是今天）。 */
export function endOfWeek(date: CalendarDate, weekStart: WeekStart): CalendarDate {
  return addDays(date, DAYS_PER_WEEK - 1 - dayOfWeekIndex(date, weekStart));
}

/**
 * 只看到期日、不看狀態：剛改成完成的項目留在原本那一組（「已過期」不會因為完成了就跳到別組），
 * 是否標成過期由 isOverdue 另外判斷。
 */
export function dueGroupOf(dueDate: CalendarDate | null, todayDate: CalendarDate, weekStart: WeekStart): DueGroup {
  if (dueDate === null) return 'none';
  if (dueDate < todayDate) return 'overdue';
  if (dueDate === todayDate) return 'today';
  return dueDate <= endOfWeek(todayDate, weekStart) ? 'thisWeek' : 'later';
}

/** 分組與排序要用到的欄位（項目的子集）。 */
export interface DueSortable {
  readonly dueDate: CalendarDate | null;
  readonly projectKey: string;
  readonly number: number;
}

/** 先照到期日（沒有到期日的在最後），同一天照編號：專案代號，再照號碼的數字大小（WEB-2 在 WEB-10 前面）。 */
export function compareDueThenKey(a: DueSortable, b: DueSortable): number {
  if (a.dueDate !== b.dueDate) {
    if (a.dueDate === null) return 1;
    if (b.dueDate === null) return -1;
    return a.dueDate < b.dueDate ? -1 : 1;
  }
  if (a.projectKey !== b.projectKey) return a.projectKey < b.projectKey ? -1 : 1;
  return a.number - b.number;
}

export interface DueGroupOf<T> {
  readonly group: DueGroup;
  readonly items: readonly T[];
}

/** 照 DUE_GROUPS 的順序分組，空的組不回傳；組內照到期日再照編號。回傳新的陣列。 */
export function groupByDue<T extends DueSortable>(items: readonly T[], todayDate: CalendarDate, weekStart: WeekStart): readonly DueGroupOf<T>[] {
  const sorted = items.toSorted(compareDueThenKey);
  return DUE_GROUPS.map((group) => ({
    group,
    items: sorted.filter((item) => dueGroupOf(item.dueDate, todayDate, weekStart) === group),
  })).filter((entry) => entry.items.length > 0);
}

/** 依到期日由近到遠排，沒有到期日的放最後。回傳新的陣列。 */
export function sortByDueDate<T extends { readonly dueDate: CalendarDate | null }>(
  items: readonly T[],
): T[] {
  return items.toSorted((a, b) => {
    if (a.dueDate === b.dueDate) return 0;
    if (a.dueDate === null) return 1;
    if (b.dueDate === null) return -1;
    return a.dueDate < b.dueDate ? -1 : 1;
  });
}
