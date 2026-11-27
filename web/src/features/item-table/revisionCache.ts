import type { InfiniteData, QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../api/queryKeys';
import type { Item, ItemDetail, ItemPage } from '../../api/types';
import { projectKeys } from '../live/liveInvalidation';
import { removeItemFromCache } from './itemCache';

type ItemPages = InfiniteData<ItemPage, string | null>;

/**
 * 專案底下跟項目有關的查詢（清單含下層清單、上層選項、已刪除、週期與里程碑的進度，同即時更新的 projectKeys）、
 * 助理選項目的搜尋結果、我的工作與總覽。
 */
function refreshProject(queryClient: QueryClient, projectKey: string): void {
  projectKeys(projectKey).forEach((queryKey) => {
    void queryClient.invalidateQueries({ queryKey });
  });
  void queryClient.invalidateQueries({ queryKey: queryKeys.assistantItemChoiceLists(projectKey) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
  void queryClient.invalidateQueries({ queryKey: queryKeys.overviews });
}

/** 助理批次修改套用之後（成功或失敗都呼叫：失敗時伺服器可能已經改了一部分）。 */
export function refreshAfterItemsChanged(queryClient: QueryClient, projectKey: string, itemKeys: readonly string[]): void {
  itemKeys.forEach((itemKey) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.item(itemKey) });
  });
  refreshProject(queryClient, projectKey);
}

/** 刪除的項目在快取裡（單一項目或清單）記得的上層；快取裡沒有就查不到。 */
function cachedParentKey(queryClient: QueryClient, projectKey: string, itemKey: string): string | null {
  const single = queryClient.getQueryData<ItemDetail>(queryKeys.item(itemKey));
  if (single !== undefined) return single.parentKey;
  const lists = queryClient.getQueriesData<ItemPages>({ queryKey: queryKeys.items(projectKey) });
  const listed = lists
    .flatMap(([, data]) => data?.pages.flatMap((page) => page.items) ?? [])
    .find((candidate: Item) => candidate.key === itemKey);
  return listed?.parentKey ?? null;
}

/**
 * 助理刪除建議套用之後。真的刪掉的（deletedKeys）從清單與我的工作拿掉，它們自己的單一項目不重抓
 * （項目頁開著時重抓只會變 404），只標成過期；上層項目與下層清單重抓。
 * 失敗時 deletedKeys 傳空陣列：不知道刪了哪些，清單重抓、單一項目只標成過期。
 */
export function refreshAfterItemsDeleted(
  queryClient: QueryClient,
  projectKey: string,
  deletedKeys: readonly string[],
  attemptedKeys: readonly string[],
): void {
  const deleted = new Set(deletedKeys);
  const parents = new Set(
    deletedKeys.flatMap((key) => {
      const parentKey = cachedParentKey(queryClient, projectKey, key);
      return parentKey === null || deleted.has(parentKey) ? [] : [parentKey];
    }),
  );
  deletedKeys.forEach((key) => {
    removeItemFromCache(queryClient, projectKey, key);
  });
  attemptedKeys.forEach((key) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.item(key), refetchType: 'none' });
  });
  parents.forEach((parentKey) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.item(parentKey) });
  });
  refreshProject(queryClient, projectKey);
}
