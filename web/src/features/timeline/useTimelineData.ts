import { useEffect } from 'react';
import type { CalendarDate, Item, ItemQuery } from '../../api/types';
import type { WeekStart } from '../../lib/weekStart';
import { dueQuery } from '../../domain/dueFilter';
import { fieldsQuery } from '../../domain/itemFilters';
import { cycleQuery, hasNoCurrentCycle, milestoneQuery } from '../../domain/planningFilter';
import type { DateViewFilters } from '../../domain/projectView';
import { useDateViewPages, type DateViewSource } from '../calendar/dateViewSource';
import { flattenItemPages } from '../item-table/itemCache';
import type { Cycle } from '../../api/planning';
import type { TimelineWindow } from './timelineRange';

/** 一個請求讀這麼多個（API 的上限）。 */
const PAGE_SIZE = 200;
/** 一段時間最多畫這麼多個：再多也看不清楚，要用篩選縮小範圍。 */
export const TIMELINE_ITEM_LIMIT = 1000;

/** 篩選條件換成 API 的參數（不含日期）；「沒有到期日」後端不能和日期一起用，改在畫面上篩（見 isKept）。 */
function filterQuery(filters: DateViewFilters, todayDate: CalendarDate, weekStart: WeekStart, cycles: readonly Cycle[]): ItemQuery {
  const due = filters.due === 'none' ? {} : dueQuery(filters.due, todayDate, weekStart);
  return {
    ...(filters.type !== null && { type: [filters.type] }),
    ...(filters.state !== null && { state: [filters.state] }),
    ...(filters.assignee !== null && { assignee: filters.assignee }),
    ...(filters.priority !== null && { priority: [filters.priority] }),
    ...due,
    ...cycleQuery(filters.cycle, cycles, todayDate),
    ...milestoneQuery(filters.milestone),
    ...fieldsQuery(filters.fields),
    ...(filters.q !== '' && { q: filters.q }),
    sort: { field: 'number', direction: 'asc' },
    limit: PAGE_SIZE,
  };
}

export function datedQuery(filters: DateViewFilters, window: TimelineWindow, todayDate: CalendarDate, weekStart: WeekStart, cycles: readonly Cycle[]): ItemQuery {
  return { ...filterQuery(filters, todayDate, weekStart, cycles), datesFrom: window.from, datesTo: window.to };
}

/** 沒有到期日的：沒有日期的項目要從這裡數（到期日以外的篩選一樣帶著）。 */
export function undatedQuery(filters: DateViewFilters, todayDate: CalendarDate, weekStart: WeekStart, cycles: readonly Cycle[]): ItemQuery {
  return { ...filterQuery({ ...filters, due: null }, todayDate, weekStart, cycles), noDue: true };
}

interface PagedItems {
  readonly items: readonly Item[];
  readonly isPending: boolean;
  readonly isError: boolean;
  readonly error: unknown;
  readonly isFetching: boolean;
  readonly isTruncated: boolean;
  readonly isMoreFailed: boolean;
  readonly retry: () => void;
}

/**
 * 項目清單一頁一頁讀完（最多 TIMELINE_ITEM_LIMIT 個）：時間軸要一段時間內的全部項目，沒有「載入更多」可以按。
 * 用行事曆與時間軸專用的查詢鍵（dateViewSource.ts），項目在面板裡改了日期或狀態時，這份整份重抓（itemCache.ts）。
 * 載入更多失敗時停下來，不一直重試；整份失敗時 retry 重抓。
 */
export function usePagedItems(source: DateViewSource, query: ItemQuery, isEnabled: boolean): PagedItems {
  const result = useDateViewPages(source, query, isEnabled);
  const all = flattenItemPages(result.data);
  const { hasNextPage, isFetchingNextPage, isFetchNextPageError, fetchNextPage } = result;
  const needsMore = hasNextPage && !isFetchingNextPage && !isFetchNextPageError && all.length < TIMELINE_ITEM_LIMIT;
  // 頁數也是依賴：一頁很快讀完時畫面可能沒看過「正在讀」，needsMore 一直是 true，只看它不會再讀下一頁。
  const pageCount = result.data?.pages.length ?? 0;
  useEffect(() => {
    if (needsMore) void fetchNextPage();
  }, [needsMore, pageCount, fetchNextPage]);
  return {
    items: all.slice(0, TIMELINE_ITEM_LIMIT),
    isPending: result.isPending && isEnabled,
    isError: result.isError && result.data === undefined,
    error: result.error,
    isFetching: result.isFetching,
    isTruncated: all.length > TIMELINE_ITEM_LIMIT || (all.length >= TIMELINE_ITEM_LIMIT && hasNextPage),
    isMoreFailed: isFetchNextPageError,
    retry: () => void result.refetch(),
  };
}

export interface TimelineData {
  /** 畫面這一段時間裡的項目（已套上畫面上才能篩的條件）。 */
  readonly dated: PagedItems & { readonly visible: readonly Item[] };
  /** 沒有任何日期的項目數；到期篩選不是「沒有到期日」時沒有意義（一定是 0），不讀。 */
  readonly undatedCount: number;
  readonly isUndatedTruncated: boolean;
}

/**
 * 時間軸要讀的兩份清單：畫面這段時間裡的（dated），和沒有任何日期的（undated，只為了數有幾個）。
 * null 是不必讀：結果一定是空的，或這個到期條件下沒有意義。
 */
export interface TimelineQueries {
  readonly dated: ItemQuery | null;
  readonly undated: ItemQuery | null;
  /** 到期條件是「沒有到期日」：後端不能和日期範圍一起用，改在畫面上把有到期日的拿掉。 */
  readonly dropsDueItems: boolean;
}

/** 專案頁的條件：「目前的週期」而現在沒有時，結果一定是空的，不送請求（不送假編號，否則變成全部的項目）。 */
export function projectTimelineQueries(
  filters: DateViewFilters,
  window: TimelineWindow,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  cycles: readonly Cycle[],
): TimelineQueries {
  const isBlocked = hasNoCurrentCycle(filters.cycle, cycles, todayDate);
  const wantsUndated = filters.due === null || filters.due === 'none';
  return {
    dated: isBlocked ? null : datedQuery(filters, window, todayDate, weekStart, cycles),
    undated: isBlocked || !wantsUndated ? null : undatedQuery(filters, todayDate, weekStart, cycles),
    dropsDueItems: filters.due === 'none',
  };
}

export function useTimelineData(source: DateViewSource, queries: TimelineQueries, isReady: boolean): TimelineData {
  const dated = usePagedItems(source, queries.dated ?? {}, isReady && queries.dated !== null);
  const undated = usePagedItems(source, queries.undated ?? {}, isReady && queries.undated !== null);
  const visible = dated.items.filter((item) => !queries.dropsDueItems || item.dueDate === null);
  // 數的是開始日與到期日都沒有的：連結去的表格（due=none）還會多出只有開始日的，那些畫得進時間軸（是點）。
  const noDates = undated.items.filter((item) => item.startDate === null && item.dueDate === null);
  return {
    dated: { ...dated, visible },
    undatedCount: queries.undated !== null ? noDates.length : 0,
    isUndatedTruncated: undated.isTruncated,
  };
}
