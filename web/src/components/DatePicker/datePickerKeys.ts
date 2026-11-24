import type { CalendarDate } from '../../api/types';
import type { WeekStart } from '../../lib/weekStart';
import { addMonths, addYears, endOfWeekOf, monthOf, PERIOD_COLUMNS, PERIOD_PAGE_SIZE, shiftDays, shiftMonthsKeepingDay, shiftYearsKeepingDay, startOfWeekOf, yearOf } from './calendarDates';

const DAYS_PER_WEEK = 7;

/**
 * 日曆裡的方向鍵移到哪一天（照 WAI-ARIA APG 的 Date Picker）：←→ 一天、↑↓ 一週、Home／End 這一週的第一天與最後一天（一週從哪天開始照偏好）、
 * PageUp／PageDown 一個月、加 Shift 是一年（日子超出就停在月底）。其他的鍵回傳 null（不處理）。
 */
export function gridMoveTarget(key: string, isShift: boolean, from: CalendarDate, weekStart: WeekStart): CalendarDate | null {
  switch (key) {
    case 'ArrowLeft':
      return shiftDays(from, -1);
    case 'ArrowRight':
      return shiftDays(from, 1);
    case 'ArrowUp':
      return shiftDays(from, -DAYS_PER_WEEK);
    case 'ArrowDown':
      return shiftDays(from, DAYS_PER_WEEK);
    case 'Home':
      return startOfWeekOf(from, weekStart);
    case 'End':
      return endOfWeekOf(from, weekStart);
    case 'PageUp':
      return isShift ? addYears(from, -1) : addMonths(from, -1);
    case 'PageDown':
      return isShift ? addYears(from, 1) : addMonths(from, 1);
    default:
      return null;
  }
}

/** 3×4 的格子裡，第 index 格往哪裡移幾格：←→ 一格、↑↓ 一列、Home／End 這一列的頭尾、PageUp／PageDown 一頁（12 格）。 */
function periodOffset(key: string, index: number): number | null {
  const column = index % PERIOD_COLUMNS;
  switch (key) {
    case 'ArrowLeft':
      return -1;
    case 'ArrowRight':
      return 1;
    case 'ArrowUp':
      return -PERIOD_COLUMNS;
    case 'ArrowDown':
      return PERIOD_COLUMNS;
    case 'Home':
      return -column;
    case 'End':
      return PERIOD_COLUMNS - 1 - column;
    case 'PageUp':
      return -PERIOD_PAGE_SIZE;
    case 'PageDown':
      return PERIOD_PAGE_SIZE;
    default:
      return null;
  }
}

/**
 * 月份畫面的按鍵移到哪一天：一格是一個月（PageUp／PageDown 是一年）。日子用進月份畫面時的那一天（day），
 * 再夾到月底。走出今年就換到別的年份（畫面跟著換）。
 */
export function monthMoveTarget(key: string, from: CalendarDate, day: number): CalendarDate | null {
  const offset = periodOffset(key, monthOf(from) - 1);
  return offset === null ? null : shiftMonthsKeepingDay(from, offset, day);
}

/** 年份畫面的按鍵移到哪一天：一格是一年（PageUp／PageDown 是 12 年）；這一列的頭尾照這一頁的排法算。 */
export function yearMoveTarget(key: string, from: CalendarDate, day: number, pageStart: number): CalendarDate | null {
  const index = (((yearOf(from) - pageStart) % PERIOD_PAGE_SIZE) + PERIOD_PAGE_SIZE) % PERIOD_PAGE_SIZE;
  const offset = periodOffset(key, index);
  return offset === null ? null : shiftYearsKeepingDay(from, offset, day);
}
