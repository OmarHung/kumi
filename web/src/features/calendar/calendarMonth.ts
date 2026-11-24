import type { CalendarDate } from '../../api/types';
import { addDays, endOfWeek, startOfWeek } from '../../domain/dates';
import type { WeekStart } from '../../lib/weekStart';

/** 月份，YYYY-MM。行事曆的網址參數 month 就是這個。 */
export type MonthKey = string;

const MONTH_PATTERN = /^(\d{4})-(0[1-9]|1[0-2])$/;
const DAYS_PER_WEEK = 7;
const WEEKDAY_NAMES = ['日', '一', '二', '三', '四', '五', '六'] as const;

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

function partsOf(month: MonthKey): { readonly year: number; readonly monthIndex: number } {
  const [year = 0, month1 = 1] = month.split('-').map(Number);
  return { year, monthIndex: month1 - 1 };
}

function dateOf(date: CalendarDate): Date {
  const [year = 0, month = 1, day = 1] = date.split('-').map(Number);
  return new Date(year, month - 1, day);
}

/** 網址的 month 合法就用它；沒給或打錯時是今天所在的月（打錯的網址不該是一頁空白）。 */
export function monthOf(param: string | null, todayDate: CalendarDate): MonthKey {
  return param !== null && MONTH_PATTERN.test(param) ? param : todayDate.slice(0, 7);
}

export function shiftMonth(month: MonthKey, delta: number): MonthKey {
  const { year, monthIndex } = partsOf(month);
  const shifted = new Date(year, monthIndex + delta, 1);
  return `${String(shifted.getFullYear())}-${pad(shifted.getMonth() + 1)}`;
}

export function monthTitle(month: MonthKey): string {
  const { year, monthIndex } = partsOf(month);
  return `${String(year)} 年 ${String(monthIndex + 1)} 月`;
}

/** 「11 月 18 日（週三）」：每一格的標題，報讀軟體靠它知道在哪一天。 */
export function dayHeading(date: CalendarDate): string {
  const value = dateOf(date);
  return `${String(value.getMonth() + 1)} 月 ${String(value.getDate())} 日（週${WEEKDAY_NAMES[value.getDay()] ?? ''}）`;
}

/** 週一到週日的表頭；從週日開始時把週日挪到最前面。 */
const WEEKDAY_HEADERS = ['週一', '週二', '週三', '週四', '週五', '週六', '週日'] as const;

export function weekdayHeaders(weekStart: WeekStart): readonly string[] {
  return weekStart === 'monday' ? WEEKDAY_HEADERS : [WEEKDAY_HEADERS[6], ...WEEKDAY_HEADERS.slice(0, 6)];
}

export interface MonthGrid {
  /** 一週七天，從 weekStart 那天開始。 */
  readonly weeks: readonly (readonly CalendarDate[])[];
  /** 畫面上的第一天與最後一天：項目依這個範圍讀。 */
  readonly start: CalendarDate;
  readonly end: CalendarDate;
}

/** 顯示整週：頭尾有上下個月的日子（畫面上字淡一點），一週從哪天開始照個人偏好，和「本週」同一套（domain/dates.ts）。 */
export function monthGrid(month: MonthKey, weekStart: WeekStart): MonthGrid {
  const { year, monthIndex } = partsOf(month);
  const first: CalendarDate = `${String(year)}-${pad(monthIndex + 1)}-01`;
  const lastDay = new Date(year, monthIndex + 1, 0);
  const last: CalendarDate = `${String(year)}-${pad(monthIndex + 1)}-${pad(lastDay.getDate())}`;
  const start = startOfWeek(first, weekStart);
  const end = endOfWeek(last, weekStart);
  const dayCount = Math.round((dateOf(end).getTime() - dateOf(start).getTime()) / 86_400_000) + 1;
  const days = Array.from({ length: dayCount }, (_, index) => addDays(start, index));
  const weeks = Array.from({ length: dayCount / DAYS_PER_WEEK }, (_, week) =>
    days.slice(week * DAYS_PER_WEEK, (week + 1) * DAYS_PER_WEEK),
  );
  return { weeks, start, end };
}
