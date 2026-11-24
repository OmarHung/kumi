import { useEffect } from 'react';
import type { Item, ItemPage } from '../../api/types';
import type { CalendarDateMode } from '../../lib/calendarDateMode';
import { CALENDAR_PAGE_SIZE } from './calendarData';

/**
 * 月曆上沒有位置的項目：依到期日排時是沒有到期日的；期間排法是開始日與到期日都沒有的
 * （只有開始日的畫得出來，是那一天；表格的「沒有到期日」比這個多出那些）。
 */
export function unplacedItems(items: readonly Item[], mode: CalendarDateMode): readonly Item[] {
  return mode === 'span' ? items.filter((item) => item.startDate === null) : items;
}

/** 期間排法最多讀這麼多頁來數「沒有開始日也沒有到期日」的：只為了一個數字，不把整份清單讀完。 */
export const UNPLACED_MAX_PAGES = 5;

/** useInfiniteQuery 的結果裡這裡用到的部分（專案與「我的工作」的讀法不同，形狀一樣）。 */
interface PagedResult {
  readonly data: { readonly pages: readonly ItemPage[] } | undefined;
  readonly hasNextPage: boolean;
  readonly isFetchingNextPage: boolean;
  readonly isFetchNextPageError: boolean;
  readonly fetchNextPage: () => Promise<unknown>;
}

/**
 * 月曆上沒有位置的項目數。依到期日排時「沒有到期日」就是它，讀一頁、滿了說「以上」。
 * 期間排法要再挑出沒有開始日的：讀回來的一頁可能全是只有開始日的（它們畫得出來），所以接著往下讀，
 * 最多 UNPLACED_MAX_PAGES 頁；讀到上限仍有下一頁就說「N 個以上」。載入下一頁失敗就停，不一直重試。
 */
export function useUnplacedCount(result: PagedResult, mode: CalendarDateMode): { readonly count: number; readonly hasMore: boolean } {
  const { data, hasNextPage, isFetchingNextPage, isFetchNextPageError, fetchNextPage } = result;
  const pages = data?.pages ?? [];
  const shouldFetchMore = mode === 'span' && hasNextPage && pages.length < UNPLACED_MAX_PAGES && !isFetchingNextPage && !isFetchNextPageError;
  useEffect(() => {
    if (shouldFetchMore) void fetchNextPage();
  }, [shouldFetchMore, fetchNextPage, pages.length]);
  if (mode === 'span') {
    return { count: unplacedItems(pages.flatMap((page) => page.items), mode).length, hasMore: hasNextPage };
  }
  const first = pages[0]?.items ?? [];
  return { count: first.length, hasMore: first.length >= CALENDAR_PAGE_SIZE && hasNextPage };
}
