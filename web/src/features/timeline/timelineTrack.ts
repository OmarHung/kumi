import type { CalendarDate } from '../../api/types';
import { addDays } from '../../domain/dates';
import { daysBetween, type Placement, type TimelineWindow } from './timelineRange';

const DAYS_PER_WEEK = 7;

/** 「今天」的膠囊蓋住它兩側各多少天、一個週標籤向右寫多少天（照 390 寬的畫面量的）：標籤在這個範圍內就略過。 */
const PILL_HALF_DAYS = 2.5;
const LABEL_DAYS = 4.5;

/**
 * 軌道上的一段：位置與寬度用「第幾天、幾天」表示，由 CSS 除以範圍的天數換成百分比
 * （--col、--span、--days，不寫死 px，畫面多寬都對得上；內容安全政策不准行內 style，只有 At 設變數）。
 * 項目在範圍之前就開始了／之後才結束時，那一端是平的，表示往那邊還有。
 */
export interface TrackSegment {
  readonly col: number;
  readonly span: number;
  readonly isStartFlat: boolean;
  readonly isEndFlat: boolean;
}

/** 項目在範圍裡的位置（placeRange 已經把兩端截在範圍內）換成軌道上的一段。 */
export function trackSegment(placement: Placement): TrackSegment {
  return { col: placement.col, span: placement.span, isStartFlat: placement.isClippedStart, isEndFlat: placement.isClippedEnd };
}

export interface WeekTick {
  readonly col: number;
  readonly label: string;
}

/**
 * 週的刻度：範圍的第一天一律是一週的第一天，所以每隔 7 天一個。標籤一律只寫月/日：年份已經在上面的範圍裡，
 * 跨年時帶年份的標籤（2027/01/04）比一週的寬度長，在手機上會疊到下一個。
 */
export function weekTicks(window: TimelineWindow): readonly WeekTick[] {
  return Array.from({ length: Math.ceil(window.days / DAYS_PER_WEEK) }, (_, index) => {
    const col = index * DAYS_PER_WEEK;
    const day = addDays(window.from, col);
    return { col, label: `${day.slice(5, 7)}/${day.slice(8, 10)}` };
  });
}

/** 今天是範圍的第幾天（0 起算）；不在範圍內是 null。畫的時候落在那一天的正中間。 */
export function todayCol(window: TimelineWindow, todayDate: CalendarDate): number | null {
  if (todayDate < window.from || todayDate > window.to) return null;
  return daysBetween(window.from, todayDate);
}

/** 這個刻度的標籤會不會寫在「今天」的膠囊底下（刻度的線照畫，只略過字）。 */
export function isTickUnderToday(tickCol: number, today: number): boolean {
  return tickCol + LABEL_DAYS > today + 0.5 - PILL_HALF_DAYS && tickCol < today + 0.5 + PILL_HALF_DAYS;
}
