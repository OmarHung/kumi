import { keepPreviousData, useInfiniteQuery } from '@tanstack/react-query';
import { listItemsAcrossProjects } from '../../api/items';
import { queryKeys } from '../../api/queryKeys';
import type { ItemQuery } from '../../api/types';
import { MY_WORK_PAGE_SIZE, MY_WORK_SORT } from '../../domain/myWorkView';

/**
 * 「我的工作」表格的清單（跨專案，GET /v1/items），「載入更多」一頁一頁往下接。
 * conditions 是 myWorkConditions 的結果：null 是條件一定沒有結果，不送請求（呼叫端自己當成沒有項目）。
 * isEnabled 是 false 時也不送（選了專案、專案的內容還沒回來）。換條件時先留著舊的結果，表格不會閃成「載入中」。
 */
export function useMyWorkItems(conditions: ItemQuery | null, isEnabled: boolean) {
  const query: ItemQuery = { ...conditions, sort: MY_WORK_SORT, limit: MY_WORK_PAGE_SIZE };
  return useInfiniteQuery({
    queryKey: queryKeys.myWorkItems(query),
    queryFn: ({ pageParam, signal }) => listItemsAcrossProjects(pageParam === null ? query : { ...query, cursor: pageParam }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
    placeholderData: keepPreviousData,
    enabled: conditions !== null && isEnabled,
  });
}
