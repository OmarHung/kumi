import type { Cycle, Milestone } from '../../api/planning';
import type { CalendarDate, Item } from '../../api/types';
import { assigneeNote } from '../../domain/assigneeNote';
import { addDays, isOverdue } from '../../domain/dates';
import { daysBetween, formatRangeIn, placeRange, type Placement, type TimelineWindow } from './timelineRange';

/** 條：有開始日也有到期日（同一天也是一天寬的條）。點：只有其中一個。 */
export type RowKind = 'bar' | 'point';

export interface TimelineRow {
  readonly item: Item;
  readonly kind: RowKind;
  readonly start: CalendarDate;
  readonly end: CalendarDate;
  readonly placement: Placement;
  readonly isOverdue: boolean;
  /** 日期範圍的文字（畫面跨年時帶年份）。 */
  readonly range: string;
  /** 連結的名稱：編號 標題，日期（過期的最後多一句）。 */
  readonly name: string;
  /** 滑過時的提示：aria-hidden 的條照樣看得到；不含「已過期」（過期有自己的標示）。 */
  readonly title: string;
}

function spanOf(item: Item): { readonly start: CalendarDate; readonly end: CalendarDate } | null {
  const { startDate, dueDate } = item;
  if (startDate === null && dueDate === null) return null;
  const first = startDate ?? dueDate ?? '';
  const last = dueDate ?? startDate ?? '';
  // 資料裡開始日比到期日晚時當成同一段，畫出來才不會是負的長度。
  return first <= last ? { start: first, end: last } : { start: last, end: first };
}

function rowOf(item: Item, window: TimelineWindow, todayDate: CalendarDate, isAssigneeShown: boolean): TimelineRow | null {
  const span = spanOf(item);
  const placement = span === null ? null : placeRange(window, span.start, span.end);
  if (span === null || placement === null) return null;
  const overdue = isOverdue(item.dueDate, item.stateCategory, todayDate);
  const range = formatRangeIn(window, span.start, span.end);
  const title = `${item.key} ${item.title}，${range}${isAssigneeShown ? `，${assigneeNote(item.assignee)}` : ''}`;
  return {
    item,
    kind: item.startDate !== null && item.dueDate !== null ? 'bar' : 'point',
    ...span,
    placement,
    isOverdue: overdue,
    range,
    title,
    name: `${title}${overdue ? '，已過期' : ''}`,
  };
}

function compareRows(a: TimelineRow, b: TimelineRow): number {
  if (a.start !== b.start) return a.start < b.start ? -1 : 1;
  if (a.item.projectKey !== b.item.projectKey) return a.item.projectKey < b.item.projectKey ? -1 : 1;
  return a.item.number - b.item.number;
}

/**
 * 項目變成時間軸的列：照開始日（沒有就用到期日）排，同一天照編號。
 * 完全在畫面外的不畫：伺服器的結果是照這個範圍讀的，但項目剛改了日期、快取還沒重抓時可能多一個。
 * isAssigneeShown：名稱與提示多一句負責人（「我的工作」看「我建立的」時）。
 */
export function timelineRows(items: readonly Item[], window: TimelineWindow, todayDate: CalendarDate, isAssigneeShown = false): readonly TimelineRow[] {
  return items.flatMap((item) => rowOf(item, window, todayDate, isAssigneeShown) ?? []).toSorted(compareRows);
}

export interface HeaderCell {
  readonly key: string;
  readonly label: string;
  readonly col: number;
  readonly span: number;
  readonly isToday: boolean;
}

/** 月份那一排：頭尾的月份只佔畫面裡有的天數。 */
export function monthCells(window: TimelineWindow): readonly HeaderCell[] {
  const cells: HeaderCell[] = [];
  for (let col = 0; col < window.days; ) {
    const day = addDays(window.from, col);
    const [year = '', month = ''] = day.split('-');
    const next = firstOfNextMonth(day);
    const span = Math.min(daysBetween(day, next), window.days - col);
    cells.push({ key: `${year}-${month}`, label: `${year} 年 ${String(Number(month))} 月`, col, span, isToday: false });
    col += span;
  }
  return cells;
}

function firstOfNextMonth(day: CalendarDate): CalendarDate {
  const [year = 0, month = 1] = day.split('-').map(Number);
  const nextMonth = month === 12 ? 1 : month + 1;
  const nextYear = month === 12 ? year + 1 : year;
  return `${String(nextYear)}-${String(nextMonth).padStart(2, '0')}-01`;
}

/** 日期那一排：週的刻度每天一格；月的刻度一天太窄，每週一格、寫那週第一天的日期。 */
export function dayCells(window: TimelineWindow, todayDate: CalendarDate): readonly HeaderCell[] {
  const step = window.scale === 'week' ? 1 : 7;
  return Array.from({ length: window.days / step }, (_, index) => {
    const col = index * step;
    const day = addDays(window.from, col);
    const last = addDays(day, step - 1);
    return {
      key: day,
      label: String(Number(day.slice(8))),
      col,
      span: step,
      isToday: day <= todayDate && todayDate <= last,
    };
  });
}

export interface CycleSegment {
  readonly cycle: Cycle;
  readonly placement: Placement;
  readonly range: string;
}

export function cycleSegments(cycles: readonly Cycle[], window: TimelineWindow): readonly CycleSegment[] {
  return cycles
    .flatMap((cycle) => {
      const placement = placeRange(window, cycle.startDate, cycle.endDate);
      return placement === null ? [] : [{ cycle, placement, range: formatRangeIn(window, cycle.startDate, cycle.endDate) }];
    })
    .toSorted((a, b) => a.placement.col - b.placement.col);
}

export interface MilestoneMark {
  readonly milestone: Milestone;
  readonly col: number;
  /** 名稱可以用的寬度（天數）：到下一個里程碑為止，不壓到它的名稱。 */
  readonly labelSpan: number;
}

export function milestoneMarks(milestones: readonly Milestone[], window: TimelineWindow): readonly MilestoneMark[] {
  const placed = milestones
    .flatMap((milestone) => {
      const placement = placeRange(window, milestone.dueDate, milestone.dueDate);
      return placement === null ? [] : [{ milestone, col: placement.col }];
    })
    .toSorted((a, b) => a.col - b.col);
  return placed.map((mark, index) => ({ ...mark, labelSpan: (placed[index + 1]?.col ?? window.days) - mark.col }));
}
