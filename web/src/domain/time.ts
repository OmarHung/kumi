import type { Instant } from '../api/types';
import { toCalendarDate, formatDate } from './dates';

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;
/** 超過這麼多天就直接寫日期：「23 天前」要再心算一次，日期反而好懂。 */
const RELATIVE_DAYS_LIMIT = 7;

const relativeFormat = new Intl.RelativeTimeFormat('zh-TW', { numeric: 'auto' });

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

function parse(at: Instant): Date | null {
  const date = new Date(at);
  return Number.isNaN(date.getTime()) ? null : date;
}

/**
 * 兩個時刻在使用者所在地的日曆上差幾天。用 UTC 算當地年月日的差，日光節約時間那天短一小時也不會算錯。
 */
function calendarDaysBetween(earlier: Date, later: Date): number {
  const start = Date.UTC(earlier.getFullYear(), earlier.getMonth(), earlier.getDate());
  const end = Date.UTC(later.getFullYear(), later.getMonth(), later.getDate());
  return Math.round((end - start) / DAY_MS);
}

/** 完整的時間，給 title 與時間戳記：使用者所在地的時間，例如 2026/11/03 09:05。 */
export function formatInstant(at: Instant): string {
  const date = parse(at);
  if (date === null) {
    return at;
  }
  return `${formatDate(toCalendarDate(date))} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/**
 * 相對時間，例如「5 分鐘前」「昨天」；一週以上寫日期。now 由呼叫端給，
 * 同一份清單用同一個「現在」，測試也才控制得了。
 * 不到一天說幾小時前；一天以上說的是日曆日（週一深夜的事，週三一早看是「前天」，不是「昨天」）。
 */
export function formatRelativeTime(at: Instant, now: number): string {
  const date = parse(at);
  if (date === null) {
    return at;
  }
  const elapsed = now - date.getTime();
  if (elapsed < MINUTE_MS) {
    return '剛剛';
  }
  if (elapsed < HOUR_MS) {
    return relativeFormat.format(-Math.floor(elapsed / MINUTE_MS), 'minute');
  }
  if (elapsed < DAY_MS) {
    return relativeFormat.format(-Math.floor(elapsed / HOUR_MS), 'hour');
  }
  const days = calendarDaysBetween(date, new Date(now));
  return days < RELATIVE_DAYS_LIMIT ? relativeFormat.format(-days, 'day') : formatDate(toCalendarDate(date));
}
