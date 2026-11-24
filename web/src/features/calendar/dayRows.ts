import type { Cycle, Milestone } from '../../api/planning';
import type { Item } from '../../api/types';
import type { DayMarkers } from './calendarData';
import type { DayEntry } from './calendarSpans';

/** 月曆的一格最多這麼多行（里程碑、週期標示、項目、「還有 N 個」都算一行）。Calendar.module.css 的 .day 高度照這個算。 */
export const DAY_ROW_BUDGET = 3;

/** 一天裡的一行：里程碑、週期的開始或結束、項目。 */
export type DayRow =
  | { readonly kind: 'milestone'; readonly key: string; readonly milestone: Milestone }
  | { readonly kind: 'cycleStart' | 'cycleEnd'; readonly key: string; readonly cycle: Cycle }
  /** range：期間排法裡跨天的項目的日期範圍（「11/2～11/6」），其餘是 null 或不給。 */
  | { readonly kind: 'item'; readonly key: string; readonly item: Item; readonly range?: string | null };

/** 里程碑在最上面，接著週期的開始、結束。 */
export function markerRowsOfDay(markers: DayMarkers): readonly DayRow[] {
  return [
    ...markers.milestones.map((milestone): DayRow => ({ kind: 'milestone', key: `m-${milestone.id}`, milestone })),
    ...markers.cycleStarts.map((cycle): DayRow => ({ kind: 'cycleStart', key: `s-${cycle.id}`, cycle })),
    ...markers.cycleEnds.map((cycle): DayRow => ({ kind: 'cycleEnd', key: `e-${cycle.id}`, cycle })),
  ];
}

/** 這一天的全部：里程碑與週期的標示在最上面，最後是項目（已經依優先度、編號排好）。 */
export function rowsOfDay(markers: DayMarkers, items: readonly Item[]): readonly DayRow[] {
  return [...markerRowsOfDay(markers), ...items.map((item): DayRow => ({ kind: 'item', key: item.key, item }))];
}

/** 期間排法裡一天的全部：標示在最上面，接著是碰到這一天的項目（依車道排），跨天的帶日期範圍。 */
export function rowsOfSpanDay(markers: DayMarkers, entries: readonly DayEntry[]): readonly DayRow[] {
  return [...markerRowsOfDay(markers), ...entries.map((entry): DayRow => ({ kind: 'item', key: entry.item.key, item: entry.item, range: entry.range }))];
}

export interface DayRowSplit {
  readonly shown: readonly DayRow[];
  /** 收進「還有 N 個」的行數；0 是全部畫得下，不畫那一行。 */
  readonly hiddenCount: number;
}

/**
 * 放得下預算就全畫；放不下時畫預算減一行（留一行給「還有 N 個」），其餘收起來。
 * 「還有 N 個」本身佔一行，格子的高度才固定：最多 DAY_ROW_BUDGET 行，不因為某一天很滿就被撐高。
 */
export function splitRows(rows: readonly DayRow[]): DayRowSplit {
  if (rows.length <= DAY_ROW_BUDGET) return { shown: rows, hiddenCount: 0 };
  const shown = rows.slice(0, DAY_ROW_BUDGET - 1);
  return { shown, hiddenCount: rows.length - shown.length };
}

/**
 * 期間排法：項目在上面三條車道（calendarSpans.ts），這一格底下只有一行給標示與「還有 N 個」。
 * 放得下（沒有收起來的項目、標示不超過一個）就畫標示；否則整行換成「還有 N 個」，浮層裡列出這一天的全部。
 * N 的算法：這一格裡**沒畫出來**的行數 = 標示數 + 收起來的項目數（上面三條車道已經畫出來的不算，也就不是浮層清單的長度，
 * 浮層為了看全貌才連畫出來的一起列）。和依到期日排時一樣：「還有」是相對於格子上看得到的。標示也算進去，因為整行換成了按鈕，
 * 標示本身已經看不到。理由：這一行固定一行高，格子的高度才固定，不因為某一天很滿就被撐開。
 */
export function splitSpanRows(markerRows: readonly DayRow[], hiddenItems: number): DayRowSplit {
  if (hiddenItems === 0 && markerRows.length <= 1) return { shown: markerRows, hiddenCount: 0 };
  return { shown: [], hiddenCount: markerRows.length + hiddenItems };
}
