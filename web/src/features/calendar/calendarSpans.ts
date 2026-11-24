import type { CalendarDate, Item } from '../../api/types';
import { compareWithinDay } from './calendarData';

/** 月曆的一格最多畫這麼多條車道；再多的收進那一天底下的「還有 N 個」。 */
export const SPAN_LANE_BUDGET = 3;

/** 一個項目在月曆上佔的日子，含頭尾。 */
export interface DateSpan {
  readonly start: CalendarDate;
  readonly end: CalendarDate;
}

/**
 * 只有到期日的是那一天，只有開始日的是開始那一天；開始日晚於到期日是資料有誤，當成只有到期日（畫出一條倒著的條沒有意義）。
 * 兩個日期都沒有就沒有位置（null）。
 */
export function spanOf(item: Item): DateSpan | null {
  const { startDate, dueDate } = item;
  if (dueDate === null) return startDate === null ? null : { start: startDate, end: startDate };
  if (startDate === null || startDate > dueDate) return { start: dueDate, end: dueDate };
  return { start: startDate, end: dueDate };
}

function parts(date: CalendarDate): readonly [number, number, number] {
  const [year = 0, month = 1, day = 1] = date.split('-').map(Number);
  return [year, month, day];
}

/** 「11/2」；跨年時帶年份「2026/12/28」。月與日不補零，和連結名稱的說法一致。 */
function shortDate(date: CalendarDate, withYear: boolean): string {
  const [year, month, day] = parts(date);
  const monthAndDay = `${String(month)}/${String(day)}`;
  return withYear ? `${String(year)}/${monthAndDay}` : monthAndDay;
}

/** 「11/2～11/6」，跨年時兩邊都帶年份：名稱裡要有日期，條的位置對報讀軟體沒有意義。 */
export function formatSpanRange(span: DateSpan): string {
  const withYear = span.start.slice(0, 4) !== span.end.slice(0, 4);
  return `${shortDate(span.start, withYear)}～${shortDate(span.end, withYear)}`;
}

/** 一週裡的一段：整個項目在這一週的部分。 */
export interface WeekSegment {
  readonly item: Item;
  /** 從這一週的第幾天開始（0 起算）、佔幾天。 */
  readonly col: number;
  readonly length: number;
  readonly lane: number;
  /** 項目在這一週之前就開始了／在這一週之後才結束：端點畫成平的並加「‹」「›」。 */
  readonly continuesBefore: boolean;
  readonly continuesAfter: boolean;
  /** 項目佔不只一天：畫成條；單天的長得和一般的項目一樣。 */
  readonly isBar: boolean;
  /** 日期範圍的說法（連結名稱用）；單天的項目是 null。 */
  readonly range: string | null;
  /** 同一個項目好幾段時，只有第一個畫得出來的那一段在 Tab 的順序裡。 */
  readonly isTabbable: boolean;
}

export interface DayEntry {
  readonly item: Item;
  readonly range: string | null;
}

export interface DayCover {
  /** 這一天碰到、但車道放不下而沒畫的項目數。 */
  readonly hiddenCount: number;
  /** 這一天碰到的全部項目（含沒畫的），依車道排：「還有 N 個」的浮層用。 */
  readonly entries: readonly DayEntry[];
}

export interface WeekLayout {
  /** 畫得出來的（車道在預算內）。 */
  readonly segments: readonly WeekSegment[];
  readonly days: readonly DayCover[];
}

interface Placed {
  readonly item: Item;
  readonly span: DateSpan;
  readonly col: number;
  readonly length: number;
  readonly continuesBefore: boolean;
  readonly continuesAfter: boolean;
}

/** 先開始的在上面（接續上一週的算從第一天開始），一樣早的看真正的開始日，再來長的在上面，最後和每一天一樣依優先度、編號。 */
function comparePlaced(a: Placed, b: Placed): number {
  return (
    a.col - b.col ||
    a.span.start.localeCompare(b.span.start) ||
    b.span.end.localeCompare(a.span.end) ||
    compareWithinDay(a.item, b.item)
  );
}

function placeInWeek(week: readonly CalendarDate[], item: Item): Placed | null {
  const span = spanOf(item);
  const first = week[0];
  const last = week.at(-1);
  if (span === null || first === undefined || last === undefined || span.end < first || span.start > last) return null;
  const clippedStart = span.start < first ? first : span.start;
  const clippedEnd = span.end > last ? last : span.end;
  const col = week.indexOf(clippedStart);
  return {
    item,
    span,
    col,
    length: week.indexOf(clippedEnd) - col + 1,
    continuesBefore: span.start < first,
    continuesAfter: span.end > last,
  };
}

/** 依序放進第一條放得下的車道（前一個的最後一天在這一個的第一天之前）。 */
function assignLanes(placed: readonly Placed[]): readonly number[] {
  const laneEnds: number[] = [];
  return placed.map((entry) => {
    const free = laneEnds.findIndex((end) => end < entry.col);
    const lane = free === -1 ? laneEnds.length : free;
    laneEnds[lane] = entry.col + entry.length - 1;
    return lane;
  });
}

interface LaidOut extends Placed {
  readonly lane: number;
}

function layoutWeek(week: readonly CalendarDate[], items: readonly Item[]): readonly LaidOut[] {
  const placed = items.flatMap((item) => placeInWeek(week, item) ?? []).toSorted(comparePlaced);
  const lanes = assignLanes(placed);
  return placed.map((entry, index) => ({ ...entry, lane: lanes[index] ?? 0 }));
}

function coverOf(laidOut: readonly LaidOut[], dayIndex: number): DayCover {
  const covering = laidOut.filter((entry) => entry.col <= dayIndex && dayIndex < entry.col + entry.length).toSorted((a, b) => a.lane - b.lane);
  return {
    hiddenCount: covering.filter((entry) => entry.lane >= SPAN_LANE_BUDGET).length,
    entries: covering.map((entry) => ({ item: entry.item, range: rangeOf(entry.span) })),
  };
}

function rangeOf(span: DateSpan): string | null {
  return span.start === span.end ? null : formatSpanRange(span);
}

/**
 * 月曆每一週的條：項目在每一週各排一段，同一週裡用車道排（先開始的在上面）。
 * 只有第一個畫得出來的那一段進 Tab 的順序（isTabbable）：被收起來的段落不算，不然項目整個沒辦法用鍵盤走到。
 */
export function layoutWeeks(weeks: readonly (readonly CalendarDate[])[], items: readonly Item[]): readonly WeekLayout[] {
  const reached = new Set<string>();
  return weeks.map((week) => {
    const laidOut = layoutWeek(week, items);
    const segments = laidOut
      .filter((entry) => entry.lane < SPAN_LANE_BUDGET)
      .map((entry): WeekSegment => {
        const isTabbable = !reached.has(entry.item.key);
        reached.add(entry.item.key);
        return {
          item: entry.item,
          col: entry.col,
          length: entry.length,
          lane: entry.lane,
          continuesBefore: entry.continuesBefore,
          continuesAfter: entry.continuesAfter,
          isBar: entry.span.start !== entry.span.end,
          range: rangeOf(entry.span),
          isTabbable,
        };
      });
    return { segments, days: week.map((_date, index) => coverOf(laidOut, index)) };
  });
}

/** 面板的「上一個、下一個」走的順序：照開始日（只有到期日的用到期日），再照編號；沒有日期的不在月曆上，不列。 */
export function itemSequence(items: readonly Item[]): readonly string[] {
  return items
    .flatMap((item) => {
      const span = spanOf(item);
      return span === null ? [] : [{ item, start: span.start }];
    })
    .toSorted((a, b) => a.start.localeCompare(b.start) || a.item.number - b.item.number)
    .map((entry) => entry.item.key);
}
