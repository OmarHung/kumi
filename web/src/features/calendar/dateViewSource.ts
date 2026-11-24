import { keepPreviousData, useInfiniteQuery, type QueryKey } from '@tanstack/react-query';
import { listItems, listItemsAcrossProjects } from '../../api/items';
import { queryKeys } from '../../api/queryKeys';
import type { ItemPage, ItemQuery } from '../../api/types';

/**
 * 行事曆與時間軸「去哪裡讀項目」：專案頁讀一個專案的清單，「我的工作」讀跨專案的清單。
 * 兩邊的查詢鍵都放在各自的前綴底下（專案的 ['project', 代號, 'items', …]、「我的工作」的 ['my-work', 'items', …]），
 * 最後一段都是 DATE_VIEW：項目在面板裡改了、刪除之後，itemCache.ts 認這一段，動到條件的整份重抓。
 */
export interface DateViewSource {
  readonly queryKey: (query: ItemQuery) => QueryKey;
  readonly fetchPage: (query: ItemQuery, signal?: AbortSignal) => Promise<ItemPage>;
}

export function projectDateViewSource(projectKey: string): DateViewSource {
  return {
    queryKey: (query) => queryKeys.dateView(projectKey, query),
    fetchPage: (query, signal) => listItems(projectKey, query, signal),
  };
}

export const MY_WORK_DATE_VIEW_SOURCE: DateViewSource = {
  queryKey: (query) => queryKeys.myWorkDateView(query),
  fetchPage: (query, signal) => listItemsAcrossProjects(query, signal),
};

/** 一頁一頁讀的清單（游標），換條件時先留著舊的結果（不閃成「載入中」）。isEnabled 是 false 時不送請求。 */
export function useDateViewPages(source: DateViewSource, query: ItemQuery, isEnabled: boolean) {
  return useInfiniteQuery({
    queryKey: source.queryKey(query),
    queryFn: ({ pageParam, signal }) => source.fetchPage(pageParam === null ? query : { ...query, cursor: pageParam }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
    placeholderData: keepPreviousData,
    enabled: isEnabled,
  });
}
