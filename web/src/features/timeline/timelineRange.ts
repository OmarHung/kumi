import type { CalendarDate } from '../../api/types';
import { addDays, startOfWeek, toCalendarDate } from '../../domain/dates';
import type { WeekStart } from '../../lib/weekStart';

/** 時間軸的刻度：週是一天約 2rem 看六週，月是一天約 0.5rem 看半年（寬度寫在 Timeline.module.css）。 */
export type TimelineScale = 'week' | 'month';

export const DEFAULT_SCALE: TimelineScale = 'week';

const SCALE_PARAM = 'scale';
const FROM_PARAM = 'from';
const DAYS_PER_WEEK = 7;

/** 畫面（讀資料與畫出來的範圍）有幾天：整數個星期，格線才對得上一週的第一天。 */
export const SCALE_DAYS: Readonly<Record<TimelineScale, number>> = { week: 6 * DAYS_PER_WEEK, month: 26 * DAYS_PER_WEEK };

/** 「往前」「往後」一次移動幾天：比畫面短，前後兩段重疊兩週（週）或十三週（月），不會一下子看不到剛剛的東西。 */
export const SCALE_STEP_DAYS: Readonly<Record<TimelineScale, number>> = { week: 4 * DAYS_PER_WEEK, month: 13 * DAYS_PER_WEEK };

/** 量不到畫面寬度時（測試環境）用 SCALE_STEP_DAYS；量得到時用可見的天數（stepDaysFor）。 */
const MIN_STEP_DAYS = DAYS_PER_WEEK;

const DATE_TEXT = /^\d{4}-\d{2}-\d{2}$/;

function parts(date: CalendarDate): [number, number, number] {
  const [year = 0, month = 1, day = 1] = date.split('-').map(Number);
  return [year, month, day];
}

/** 網址可能是手打的：格式對、而且真的有這一天（2 月 31 日不算）才收。 */
function isRealDate(text: string): text is CalendarDate {
  if (!DATE_TEXT.test(text)) return false;
  const [year, month, day] = parts(text);
  return toCalendarDate(new Date(year, month - 1, day)) === text;
}

export function scaleFromSearchParams(params: URLSearchParams): TimelineScale {
  return params.get(SCALE_PARAM) === 'month' ? 'month' : DEFAULT_SCALE;
}

/** 沒有 from 時：今天往前一週所在的那一週的第一天（週一或週日，照個人偏好），今天前後都看得到，今天落在畫面左邊三分之一以內。 */
export function defaultFrom(todayDate: CalendarDate, weekStart: WeekStart): CalendarDate {
  return startOfWeek(addDays(todayDate, -DAYS_PER_WEEK), weekStart);
}

/** 畫面的第一天一律是一週的第一天：給的不是就對齊到那一週的第一天，格線（每週一條）才不會歪；換偏好時網址上的 from 也跟著對齊。 */
export function fromFromSearchParams(params: URLSearchParams, todayDate: CalendarDate, weekStart: WeekStart): CalendarDate {
  const text = params.get(FROM_PARAM)?.trim() ?? '';
  return isRealDate(text) ? startOfWeek(text, weekStart) : defaultFrom(todayDate, weekStart);
}

export interface TimelinePosition {
  readonly scale: TimelineScale;
  readonly from: CalendarDate;
}

/** 新的查詢字串：只換 scale 與 from，預設的值不寫（網址短一點、「今天」跟著日期走），其他參數（篩選、面板）原封不動。 */
export function timelineParams(params: URLSearchParams, next: TimelinePosition, todayDate: CalendarDate, weekStart: WeekStart): URLSearchParams {
  const result = new URLSearchParams(params);
  if (next.scale === DEFAULT_SCALE) result.delete(SCALE_PARAM);
  else result.set(SCALE_PARAM, next.scale);
  if (next.from === defaultFrom(todayDate, weekStart)) result.delete(FROM_PARAM);
  else result.set(FROM_PARAM, next.from);
  return result;
}

export interface TimelineWindow extends TimelinePosition {
  /** 畫面的最後一天（含）。 */
  readonly to: CalendarDate;
  readonly days: number;
}

export function windowOf(scale: TimelineScale, from: CalendarDate): TimelineWindow {
  const days = SCALE_DAYS[scale];
  return { scale, from, to: addDays(from, days - 1), days };
}

/** 兩個日期差幾天（後面的減前面的）。用 UTC 算，日光節約時間不會讓一天變成 23 或 25 小時。 */
export function daysBetween(from: CalendarDate, to: CalendarDate): number {
  const [fromYear, fromMonth, fromDay] = parts(from);
  const [toYear, toMonth, toDay] = parts(to);
  const millisecondsPerDay = 86_400_000;
  return Math.round((Date.UTC(toYear, toMonth - 1, toDay) - Date.UTC(fromYear, fromMonth - 1, fromDay)) / millisecondsPerDay);
}

export interface Placement {
  /** 從畫面的第幾天開始（0 起算）。 */
  readonly col: number;
  /** 佔幾天（含頭尾）。 */
  readonly span: number;
  /** 期間在畫面之前就開始了／在畫面之後才結束：在邊上截斷，並標示往左或往右還有。 */
  readonly isClippedStart: boolean;
  readonly isClippedEnd: boolean;
}

/** 一段期間（含頭尾）在畫面上的位置；完全在畫面外是 null。 */
export function placeRange(window: TimelineWindow, start: CalendarDate, end: CalendarDate): Placement | null {
  if (end < window.from || start > window.to) return null;
  const isClippedStart = start < window.from;
  const isClippedEnd = end > window.to;
  const col = isClippedStart ? 0 : daysBetween(window.from, start);
  const last = isClippedEnd ? window.days - 1 : daysBetween(window.from, end);
  return { col, span: last - col + 1, isClippedStart, isClippedEnd };
}

function shortDate(date: CalendarDate): string {
  const [, month, day] = parts(date);
  return `${String(month).padStart(2, '0')}/${String(day).padStart(2, '0')}`;
}

/** 「11/18～11/28」，同一天只寫「11/28」：名稱裡要有日期，位置對報讀軟體沒有意義。 */
export function formatRange(start: CalendarDate, end: CalendarDate): string {
  return start === end ? shortDate(start) : `${shortDate(start)}～${shortDate(end)}`;
}

/**
 * 「往前」「往後」一次移動幾天。量得到畫面看得到幾天（visibleDays）時：看得到的天數減一週重疊，
 * 湊成整週（起點一律對齊一週的第一天，不是整週會被拉回去，往前與往後就不對稱），至少一週、不超過畫面的天數減一週。
 * 理由：固定 4 週在 1440 寬只看得到約 25 天、面板開著約 15 天，按「往後」會跳過沒看過的那幾天。
 * 量不到時（visibleDays 是 null）用固定的天數。
 */
export function stepDaysFor(scale: TimelineScale, visibleDays: number | null): number {
  if (visibleDays === null || !Number.isFinite(visibleDays)) return SCALE_STEP_DAYS[scale];
  const weeks = Math.floor((visibleDays - DAYS_PER_WEEK) / DAYS_PER_WEEK);
  const widest = SCALE_DAYS[scale] - DAYS_PER_WEEK;
  return Math.min(widest, Math.max(MIN_STEP_DAYS, weeks * DAYS_PER_WEEK));
}

function longDate(date: CalendarDate): string {
  return date.replaceAll('-', '/');
}

/** 畫面跨年時日期要帶年份，報讀才不會有歧義（12/28 是哪一年）；同一年照舊不帶。 */
export function formatRangeIn(window: TimelineWindow, start: CalendarDate, end: CalendarDate): string {
  if (window.from.slice(0, 4) === window.to.slice(0, 4)) return formatRange(start, end);
  return start === end ? longDate(start) : `${longDate(start)}～${longDate(end)}`;
}
