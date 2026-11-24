import type { Cycle, Milestone } from '../../api/planning';
import type { CalendarDate, Item, ItemQuery, Priority } from '../../api/types';
import type { WeekStart } from '../../lib/weekStart';
import { dueQuery } from '../../domain/dueFilter';
import { fieldsQuery } from '../../domain/itemFilters';
import { cycleQuery, hasNoCurrentCycle, milestoneQuery } from '../../domain/planningFilter';
import type { DateViewFilters } from '../../domain/projectView';
import type { MonthGrid } from './calendarMonth';

/**
 * 行事曆與時間軸共用的篩選型別是 domain/projectView.ts 的 DateViewFilters（工具列上和看板同一套：
 * 搜尋、負責人、到期、優先度、週期、里程碑，再加表格也有的類型與狀態；流程只有看板用，這裡忽略）。
 * 兩種版面的元件介面一樣（ProjectDateViewProps，見 CalendarView.tsx）。
 */

/** 項目清單一頁最多這麼多筆（後端的上限）。 */
export const CALENDAR_PAGE_SIZE = 200;

/** 一段時間的全部項目，最多讀這麼多筆；超過的畫前面這些，上方說明。 */
export const CALENDAR_ITEM_LIMIT = 1000;

const PRIORITY_RANK: Readonly<Record<Priority, number>> = { urgent: 0, high: 1, medium: 2, low: 3, none: 4 };

/** 工具列上和到期無關的條件：類型、狀態、負責人、優先度、週期、里程碑、自訂欄位、搜尋。 */
function sharedConditions(filters: DateViewFilters, todayDate: CalendarDate, cycles: readonly Cycle[]): ItemQuery {
  return {
    ...(filters.type !== null && { type: [filters.type] }),
    ...(filters.state !== null && { state: [filters.state] }),
    ...(filters.assignee !== null && { assignee: filters.assignee }),
    ...(filters.priority !== null && { priority: [filters.priority] }),
    ...cycleQuery(filters.cycle, cycles, todayDate),
    ...milestoneQuery(filters.milestone),
    ...fieldsQuery(filters.fields),
    ...(filters.q !== '' && { q: filters.q }),
  };
}

export function laterOf(a: CalendarDate, b: CalendarDate | undefined): CalendarDate {
  return b !== undefined && b > a ? b : a;
}

export function earlierOf(a: CalendarDate, b: CalendarDate | undefined): CalendarDate {
  return b !== undefined && b < a ? b : a;
}

/**
 * 畫面上看得到的那幾週的項目：依到期日讀，工具列的到期條件和畫面的範圍取交集。
 * 回 null 表示不必讀、結果一定是空的：到期條件落在畫面之外，或是「沒有到期日」（月曆上沒有那一天）。
 */
export function visibleItemsQuery(
  filters: DateViewFilters,
  grid: MonthGrid,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  cycles: readonly Cycle[],
): ItemQuery | null {
  if (hasNoCurrentCycle(filters.cycle, cycles, todayDate)) return null;
  const due = dueQuery(filters.due, todayDate, weekStart);
  if (due.noDue === true) return null;
  const dueFrom = laterOf(grid.start, due.dueFrom);
  const dueTo = earlierOf(grid.end, due.dueTo);
  if (dueFrom > dueTo) return null;
  return {
    ...sharedConditions(filters, todayDate, cycles),
    ...(due.stateCategory !== undefined && { stateCategory: due.stateCategory }),
    dueFrom,
    dueTo,
    sort: { field: 'due_date', direction: 'asc' },
    limit: CALENDAR_PAGE_SIZE,
  };
}

/**
 * 「期間」排法：讀和畫面那幾週期間重疊的項目（datesFrom、datesTo，時間軸用的同一組條件）。
 * 工具列的到期條件照帶、不和畫面的範圍取交集：期間跨進畫面的項目，到期日可以在畫面之後。
 * 「沒有到期日」後端不能和日期範圍一起用：不帶，讀回來再由 CalendarFrame 把有到期日的拿掉。
 * 只有「目前的週期」而現在沒有週期時回 null：結果一定是空的。
 */
export function visibleSpanQuery(
  filters: DateViewFilters,
  grid: MonthGrid,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  cycles: readonly Cycle[],
): ItemQuery | null {
  if (hasNoCurrentCycle(filters.cycle, cycles, todayDate)) return null;
  const due = filters.due === 'none' ? {} : dueQuery(filters.due, todayDate, weekStart);
  return {
    ...sharedConditions(filters, todayDate, cycles),
    ...due,
    datesFrom: grid.start,
    datesTo: grid.end,
    sort: { field: 'number', direction: 'asc' },
    limit: CALENDAR_PAGE_SIZE,
  };
}

/**
 * 「N 個項目沒有到期日」要數的項目：同樣的條件、只看沒有到期日的。
 * 到期條件是已過期、今天、本週時，沒有到期日的項目不會符合，回 null；只有「沒有到期日」或沒設到期條件時才有。
 */
export function noDueQuery(filters: DateViewFilters, todayDate: CalendarDate, cycles: readonly Cycle[]): ItemQuery | null {
  if (filters.due !== null && filters.due !== 'none') return null;
  if (hasNoCurrentCycle(filters.cycle, cycles, todayDate)) return null;
  return { ...sharedConditions(filters, todayDate, cycles), noDue: true, limit: CALENDAR_PAGE_SIZE };
}

export function compareWithinDay(a: Item, b: Item): number {
  return PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority] || a.number - b.number;
}

/** 依到期日分到每一天，每一天依優先度（急的在前）、再依編號。沒有到期日的不排。 */
export function itemsByDay(items: readonly Item[]): ReadonlyMap<CalendarDate, readonly Item[]> {
  const days = new Map<CalendarDate, Item[]>();
  for (const item of items) {
    if (item.dueDate === null) continue;
    days.set(item.dueDate, [...(days.get(item.dueDate) ?? []), item]);
  }
  return new Map([...days].map(([date, dayItems]) => [date, dayItems.toSorted(compareWithinDay)]));
}

export interface DayMarkers {
  readonly milestones: readonly Milestone[];
  readonly cycleStarts: readonly Cycle[];
  readonly cycleEnds: readonly Cycle[];
}

const NO_MARKERS: DayMarkers = { milestones: [], cycleStarts: [], cycleEnds: [] };

/** 里程碑標在到期日，週期標在開始日與結束日。 */
export function markersByDay(cycles: readonly Cycle[], milestones: readonly Milestone[]): ReadonlyMap<CalendarDate, DayMarkers> {
  const days = new Map<CalendarDate, DayMarkers>();
  const add = (date: CalendarDate, patch: Partial<DayMarkers>) => {
    const current = days.get(date) ?? NO_MARKERS;
    days.set(date, {
      milestones: [...current.milestones, ...(patch.milestones ?? [])],
      cycleStarts: [...current.cycleStarts, ...(patch.cycleStarts ?? [])],
      cycleEnds: [...current.cycleEnds, ...(patch.cycleEnds ?? [])],
    });
  };
  for (const milestone of milestones) add(milestone.dueDate, { milestones: [milestone] });
  for (const cycle of cycles) {
    add(cycle.startDate, { cycleStarts: [cycle] });
    add(cycle.endDate, { cycleEnds: [cycle] });
  }
  return days;
}

export function markersOf(markers: ReadonlyMap<CalendarDate, DayMarkers>, date: CalendarDate): DayMarkers {
  return markers.get(date) ?? NO_MARKERS;
}
