import { useEffect } from 'react';
import type { Item, ItemQuery } from '../../api/types';
import { flattenItemPages } from '../item-table/itemCache';
import { CALENDAR_ITEM_LIMIT } from './calendarData';
import { useDateViewPages, type DateViewSource } from './dateViewSource';

const NO_ITEMS: readonly Item[] = [];

/**
 * 一段時間的全部項目：用游標一頁一頁讀完，最多 CALENDAR_ITEM_LIMIT 筆。
 * 行事曆要的是「這幾週裡所有的項目」，不能像表格那樣等使用者按「載入更多」。
 * 走的是行事曆與時間軸專用的查詢鍵（api/queries.ts useDateViewItems）：項目面板改了日期等條件，快取（itemCache.ts）會讓它重抓。
 * query 是 null 表示不必讀（結果一定是空的）。
 */
export function useAllItems(source: DateViewSource, query: ItemQuery | null) {
  const result = useDateViewPages(source, query ?? {}, query !== null);
  const { hasNextPage, isFetchingNextPage, isFetchNextPageError, fetchNextPage } = result;
  const loaded = query === null ? NO_ITEMS : flattenItemPages(result.data);
  const isFull = loaded.length >= CALENDAR_ITEM_LIMIT;
  // 載入下一頁失敗就停：不然會一直重打；使用者按「重試」才再讀。
  const shouldFetchMore = query !== null && hasNextPage && !isFull && !isFetchingNextPage && !isFetchNextPageError;
  useEffect(() => {
    if (shouldFetchMore) void fetchNextPage();
  }, [shouldFetchMore, fetchNextPage, loaded.length]);

  const isTruncated = isFull && hasNextPage;
  return {
    items: loaded.length > CALENDAR_ITEM_LIMIT ? loaded.slice(0, CALENDAR_ITEM_LIMIT) : loaded,
    isTruncated,
    // 還在讀（第一頁，或是後面幾頁）：後面幾頁還沒到時畫面上的項目不齊全，不要說「沒有項目」。
    isLoading: query !== null && (result.isPending || shouldFetchMore || isFetchingNextPage),
    // 換了月份、新的結果還沒回來，暫時顯示舊的。
    isStale: query !== null && result.isPlaceholderData,
    isError: query !== null && (result.isError || isFetchNextPageError),
    error: result.error,
    retry: () => {
      void (isFetchNextPageError ? fetchNextPage() : result.refetch());
    },
  };
}
