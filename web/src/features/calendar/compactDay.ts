import type { CalendarDate, Item } from '../../api/types';
import { addDays, isOverdue, isTerminal } from '../../domain/dates';
import type { MonthKey } from './calendarMonth';

/** 小月曆一格底下的點：過期、其他（進行中或還沒開始）、完成，以及週期與里程碑的標示（空心）。 */
export type DayDot = 'overdue' | 'active' | 'done' | 'marker';

/** 一格最多畫幾個點：再多也只是「有很多」，格子只有 44px 寬。 */
export const MAX_DOTS = 3;

const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function isDateIn(date: string | null, month: MonthKey): date is CalendarDate {
  if (date === null || !DATE_PATTERN.test(date) || !date.startsWith(month)) return false;
  // 2026-11-31 這種格式對、日子不存在的，Date 會換成下個月的 12-01，和原本的字串對不上。
  return date === addDays(date, 0);
}

/** 選的那一天：網址的 day 在顯示的月份裡就用它；沒有時本月是今天、別的月是 1 號。 */
export function selectedDayOf(param: string | null, month: MonthKey, todayDate: CalendarDate): CalendarDate {
  if (isDateIn(param, month)) return param;
  return todayDate.startsWith(month) ? todayDate : `${month}-01`;
}

/** 依序：過期的、其他的、完成的項目，最後是標示；超過 MAX_DOTS 只留前面的。 */
export function dotsOfDay(items: readonly Item[], markerCount: number, todayDate: CalendarDate): readonly DayDot[] {
  const dotOf = (item: Item): DayDot => {
    if (isOverdue(item.dueDate, item.stateCategory, todayDate)) return 'overdue';
    return isTerminal(item.stateCategory) ? 'done' : 'active';
  };
  const order: readonly DayDot[] = ['overdue', 'active', 'done'];
  const itemDots = items.map(dotOf).toSorted((a, b) => order.indexOf(a) - order.indexOf(b));
  return [...itemDots, ...Array.from({ length: markerCount }, (): DayDot => 'marker')].slice(0, MAX_DOTS);
}

/** 每一格自己的名稱：「11 月 17 日（今天），2 個項目」；點的顏色不是唯一的線索，數字在名稱裡。 */
export function dayButtonName(date: CalendarDate, todayDate: CalendarDate, itemCount: number, markerCount: number): string {
  const day = `${String(Number(date.slice(5, 7)))} 月 ${String(Number(date.slice(8)))} 日${date === todayDate ? '（今天）' : ''}`;
  const items = itemCount === 0 ? '沒有項目' : `${String(itemCount)} 個項目`;
  return `${day}，${items}${markerCount > 0 ? `，${String(markerCount)} 個里程碑或週期標示` : ''}`;
}

const STEPS: Readonly<Record<string, number>> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };

/** 方向鍵移動選的日子（左右一天、上下一週，Home、End 到月初月底）；不離開這個月，到頭了留在原地。其他鍵回 null。 */
export function moveSelectedDay(date: CalendarDate, key: string, month: MonthKey): CalendarDate | null {
  if (key === 'Home') return `${month}-01`;
  if (key === 'End') return lastDayOf(month);
  const step = STEPS[key];
  if (step === undefined) return null;
  const next = addDays(date, step);
  return next.startsWith(month) ? next : date;
}

function lastDayOf(month: MonthKey): CalendarDate {
  const [year = 0, monthNumber = 1] = month.split('-').map(Number);
  return `${month}-${String(new Date(year, monthNumber, 0).getDate()).padStart(2, '0')}`;
}
