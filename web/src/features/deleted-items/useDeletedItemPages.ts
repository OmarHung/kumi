import { useInfiniteQuery, type InfiniteData } from '@tanstack/react-query';
import { listDeletedItems, type DeletedItemPage, type DeletedItemSummary } from '../../api/itemRecovery';
import { queryKeys } from '../../api/queryKeys';

export type DeletedItemPages = InfiniteData<DeletedItemPage>;

/** 已刪除的項目，最近刪除的在前，一頁一頁往後接。 */
export function useDeletedItemPages(projectKey: string) {
  return useInfiniteQuery({
    queryKey: queryKeys.deletedItems(projectKey),
    queryFn: ({ pageParam, signal }) =>
      listDeletedItems(projectKey, pageParam === null ? {} : { cursor: pageParam }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
  });
}

/** 各頁接起來；重抓時邊界上可能重複，以編號去重（留先出現的）。 */
export function flattenDeletedItems(data: DeletedItemPages | undefined): readonly DeletedItemSummary[] {
  const seen = new Set<string>();
  return (data?.pages ?? []).flatMap((page) =>
    page.items.filter((item) => {
      if (seen.has(item.key)) return false;
      seen.add(item.key);
      return true;
    }),
  );
}

/** 拿掉一個項目（復原成功之後馬上從畫面上消失，不等重抓）。 */
export function withoutDeletedItem(data: DeletedItemPages | undefined, itemKey: string): DeletedItemPages | undefined {
  if (data === undefined) return data;
  return { ...data, pages: data.pages.map((page) => ({ ...page, items: page.items.filter((item) => item.key !== itemKey) })) };
}

/** 拿掉 itemKey 之後焦點要去的那一列：下一列，沒有就上一列；都沒有是 null（去頁面標題）。 */
export function focusTargetAfterRemoving(items: readonly DeletedItemSummary[], itemKey: string): string | null {
  const index = items.findIndex((item) => item.key === itemKey);
  if (index < 0) return null;
  return (items[index + 1] ?? items[index - 1])?.key ?? null;
}
