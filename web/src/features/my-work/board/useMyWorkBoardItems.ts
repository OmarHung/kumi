import { useInfiniteQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { listItemsAcrossProjects } from '../../../api/items';
import { queryKeys } from '../../../api/queryKeys';
import type { ItemQuery } from '../../../api/types';
import { MY_WORK_PAGE_SIZE, MY_WORK_SORT } from '../../../domain/myWorkView';
import { flattenItemPages } from '../../item-table/itemCache';
import { useMyWorkRows } from '../useMyWorkRows';

/**
 * 看板的資料：一次讀一份跨專案的清單（鍵 myWorkBoard），在前端分欄；「載入更多」往下接，載完重新分欄。
 * conditions 是 null（條件一定是空的，例如「完成」加「已過期」）時不送請求、當成沒有項目。
 * 項目接上剛改好的那一份；這次看的期間改成完成、取消而伺服器已經不給的，留在原位直到離開這一頁（useMyWorkRows，和表格一樣）。
 */
export function useMyWorkBoardItems(conditions: ItemQuery | null) {
  const request = useMemo<ItemQuery>(() => ({ ...conditions, sort: MY_WORK_SORT, limit: MY_WORK_PAGE_SIZE }), [conditions]);
  const result = useInfiniteQuery({
    queryKey: queryKeys.myWorkBoard(request),
    queryFn: ({ pageParam, signal }) => listItemsAcrossProjects(pageParam === null ? request : { ...request, cursor: pageParam }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
    enabled: conditions !== null,
  });
  const listed = useMemo(() => flattenItemPages(result.data), [result.data]);
  const rows = useMyWorkRows(listed);
  const items = useMemo(() => rows.map((row) => row.item), [rows]);
  return { result, items, isLoading: conditions !== null && result.isPending };
}
