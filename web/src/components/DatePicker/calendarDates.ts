import type { CalendarDate } from '../../api/types';
import { dayOfWeekIndex, endOfWeek, formatDate, startOfWeek, toCalendarDate } from '../../domain/dates';
import type { WeekStart } from '../../lib/weekStart';

/*
 * 日曆用的日期計算與打字的解析（docs/DESIGN.md 7.3「日期」）。日期一律是沒有時區的 YYYY-MM-DD（CalendarDate），
 * 用本地時間的 Date 建構子加減，月底、年底、閏年都交給 Date。
 */

const DAYS_PER_WEEK = 7;
/** 月曆固定六列：每個月都一樣高，換月時日曆不會跳動。 */
const GRID_WEEKS = 6;
const MONTHS_PER_YEAR = 12;
/** 年份要四位數：小於 1000 的（0999）當成解析不了，不會變成不是 ISO 的「999-01-01」。 */
const MIN_YEAR = 1000;

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
// 打字接受 2026/12/15、2026-12-15、2026.12.15（月、日可以是一位數）與 20261215。
const SEPARATED_DATE = /^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$/;
const COMPACT_DATE = /^(\d{4})(\d{2})(\d{2})$/;

/** 週一到週日的名稱；表頭照一週從哪天開始排（weekdaysFrom），和我的工作「這一週」的分組一致（domain/dates.ts 的 endOfWeek）。 */
export const WEEKDAYS: readonly { readonly short: string; readonly long: string }[] = [
  { short: '一', long: '星期一' },
  { short: '二', long: '星期二' },
  { short: '三', long: '星期三' },
  { short: '四', long: '星期四' },
  { short: '五', long: '星期五' },
  { short: '六', long: '星期六' },
  { short: '日', long: '星期日' },
];

/** 表頭的順序：從週日開始就把週日挪到最前面。 */
export function weekdaysFrom(weekStart: WeekStart): readonly { readonly short: string; readonly long: string }[] {
  const sunday = WEEKDAYS.slice(-1);
  return weekStart === 'monday' ? WEEKDAYS : [...sunday, ...WEEKDAYS.slice(0, -1)];
}

/** 年、月、日的數字；不存在的日期（2 月 30 日）與年份小於 1000 的回傳 null。 */
function dateOf(year: number, month: number, day: number): CalendarDate | null {
  if (year < MIN_YEAR) return null;
  const date = new Date(year, month - 1, day);
  const isSame = date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
  return isSame ? toCalendarDate(date) : null;
}

function partsOf(date: CalendarDate): readonly [number, number, number] {
  const [year = 0, month = 1, day = 1] = date.split('-').map(Number);
  return [year, month, day];
}

/** 存在的 YYYY-MM-DD。 */
export function isCalendarDate(value: string): boolean {
  const match = ISO_DATE.exec(value);
  return match !== null && dateOf(Number(match[1]), Number(match[2]), Number(match[3])) === value;
}

/**
 * 打的字解析成日期；解析不了（打到一半、不存在的日期、別的格式）回傳 null。先做 NFKC：
 * 中文輸入法打出的全形數字與「／」也認得（和下拉的篩選一樣）。
 */
export function parseDateInput(text: string): CalendarDate | null {
  const normalized = text.normalize('NFKC').trim();
  const match = SEPARATED_DATE.exec(normalized) ?? COMPACT_DATE.exec(normalized);
  if (match === null) return null;
  return dateOf(Number(match[1]), Number(match[2]), Number(match[3]));
}

/** 打的字對應的值：空白是沒有值（''），解析得了是 ISO，解析不了的原封不動（呼叫端自己判斷不合法）。 */
export function normalizeTyped(text: string): string {
  if (text.trim() === '') return '';
  return parseDateInput(text) ?? text;
}

/** 輸入框裡顯示的字：日期顯示成 2026/12/15；不是日期的值（打到一半的字）照原樣。 */
export function displayText(value: string): string {
  return isCalendarDate(value) ? formatDate(value) : value;
}

export function shiftDays(date: CalendarDate, days: number): CalendarDate {
  const [year, month, day] = partsOf(date);
  return toCalendarDate(new Date(year, month - 1, day + days));
}

/** 加減月份；日子超出那個月就停在月底（1 月 31 日的下一個月是 2 月 28 日），不會滑到再下一個月。 */
export function addMonths(date: CalendarDate, months: number): CalendarDate {
  const [year, month, day] = partsOf(date);
  const lastDay = new Date(year, month - 1 + months + 1, 0).getDate();
  return toCalendarDate(new Date(year, month - 1 + months, Math.min(day, lastDay)));
}

export function addYears(date: CalendarDate, years: number): CalendarDate {
  return addMonths(date, years * MONTHS_PER_YEAR);
}

/** 這一週的第一天與最後一天，一週從哪天開始照偏好（domain/dates.ts，和「本週」同一套）。 */
export function startOfWeekOf(date: CalendarDate, weekStart: WeekStart): CalendarDate {
  return startOfWeek(date, weekStart);
}

export function endOfWeekOf(date: CalendarDate, weekStart: WeekStart): CalendarDate {
  return endOfWeek(date, weekStart);
}

export function isSameMonth(a: CalendarDate, b: CalendarDate): boolean {
  return a.slice(0, 7) === b.slice(0, 7);
}

/** 這個日期所在的月份的月曆：六列 42 天，從 1 日所在那一週的第一天開始，前後補上別的月份。 */
export function monthGrid(date: CalendarDate, weekStart: WeekStart): readonly CalendarDate[] {
  const start = startOfWeekOf(`${date.slice(0, 7)}-01`, weekStart);
  return Array.from({ length: GRID_WEEKS * DAYS_PER_WEEK }, (_, index) => shiftDays(start, index));
}

/** 月曆分成一週一列。 */
export function weeksOf(days: readonly CalendarDate[]): readonly (readonly CalendarDate[])[] {
  return Array.from({ length: Math.ceil(days.length / DAYS_PER_WEEK) }, (_, week) => days.slice(week * DAYS_PER_WEEK, (week + 1) * DAYS_PER_WEEK));
}

export function monthLabel(date: CalendarDate): string {
  const [year, month] = partsOf(date);
  return `${year} 年 ${month} 月`;
}

export function dayOfMonth(date: CalendarDate): number {
  return partsOf(date)[2];
}

/** 每一格的名稱：完整的日期加星期（「2026 年 11 月 17 日 星期二」），報讀軟體不必再對照表頭與標題。 */
export function dayLabel(date: CalendarDate): string {
  const [year, month, day] = partsOf(date);
  return `${year} 年 ${month} 月 ${day} 日 ${WEEKDAYS[dayOfWeekIndex(date, 'monday')]?.long ?? ''}`;
}

/** 年份畫面一頁幾年、月份與年份畫面一列幾格（3×4 的格子）。 */
export const PERIOD_PAGE_SIZE = 12;
export const PERIOD_COLUMNS = 3;
/** 年份畫面的第一頁從顯示的年份往前幾年開始：看得到去年、前年，也看得到明年之後的幾年（到期日常要跳到明年）。 */
const YEARS_BEFORE = 2;

/** 月份與年份畫面的一格：key 是給焦點找格子用的（YYYY-MM、YYYY），label 是名稱，short 是看得到的字。 */
export interface PeriodCell {
  readonly key: string;
  readonly label: string;
  readonly short: string;
}

export function yearOf(date: CalendarDate): number {
  return partsOf(date)[0];
}

export function monthOf(date: CalendarDate): number {
  return partsOf(date)[1];
}

/** 某年某月的某一天；那個月沒有這一天（2 月 30 日）就停在月底。 */
export function dateIn(year: number, month: number, day: number): CalendarDate {
  const lastDay = new Date(year, month, 0).getDate();
  return toCalendarDate(new Date(year, month - 1, Math.min(day, lastDay)));
}

/** 加減月份，日子用 day（進月份畫面時的那一天）再夾到月底：經過 2 月走到 3 月，31 日還是 31 日。 */
export function shiftMonthsKeepingDay(date: CalendarDate, months: number, day: number): CalendarDate {
  const [year, month] = partsOf(date);
  const target = new Date(year, month - 1 + months, 1);
  return dateIn(target.getFullYear(), target.getMonth() + 1, day);
}

/** 加減年份，日子用 day 再夾到月底（2028 年 2 月 29 日的下一年是 2029 年 2 月 28 日）。 */
export function shiftYearsKeepingDay(date: CalendarDate, years: number, day: number): CalendarDate {
  return shiftMonthsKeepingDay(date, years * MONTHS_PER_YEAR, day);
}

export function yearLabel(date: CalendarDate): string {
  return `${yearOf(date)} 年`;
}

/** 這一年的 12 個月。 */
export function monthsOfYear(date: CalendarDate): readonly PeriodCell[] {
  const year = yearOf(date);
  return Array.from({ length: MONTHS_PER_YEAR }, (_, index) => {
    const month = index + 1;
    return { key: `${year}-${String(month).padStart(2, '0')}`, label: `${year} 年 ${month} 月`, short: `${month} 月` };
  });
}

export function yearPageStartFor(year: number): number {
  return year - YEARS_BEFORE;
}

/** 年份走出這一頁時，頁面一次換 12 年（不跟著每一步滑動：滑動的話方向鍵每按一次，所有年份都換位置）。 */
export function yearPageContaining(year: number, pageStart: number): number {
  return pageStart + Math.floor((year - pageStart) / PERIOD_PAGE_SIZE) * PERIOD_PAGE_SIZE;
}

export function yearsFrom(pageStart: number): readonly PeriodCell[] {
  return Array.from({ length: PERIOD_PAGE_SIZE }, (_, index) => {
    const year = pageStart + index;
    return { key: String(year), label: `${year} 年`, short: String(year) };
  });
}

export function yearRangeLabel(pageStart: number): string {
  return `${pageStart}～${pageStart + PERIOD_PAGE_SIZE - 1}`;
}
