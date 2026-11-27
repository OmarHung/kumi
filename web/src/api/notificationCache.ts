import type { InfiniteData, QueryClient } from '@tanstack/react-query';
import { queryKeys } from './queryKeys';
import type { NotificationPage, UnreadCount } from './types';

type Pages = InfiniteData<NotificationPage, string | null>;

/**
 * 清單裡 ids 的那幾則換成已讀（原位，不移走：「未讀」分頁剛讀的留在原處，直到重新整理或換分頁）。
 * newlyRead 收集這次真的從未讀變成已讀的編號：「未讀」與「全部」兩份清單裡的同一則只算一次。
 */
function withRead(pages: Pages, ids: ReadonlySet<string> | null, at: string, newlyRead: Set<string>): Pages {
  return {
    ...pages,
    pages: pages.pages.map((page) => ({
      ...page,
      items: page.items.map((entry) => {
        if (entry.readAt !== null || (ids !== null && !ids.has(entry.notificationId))) return entry;
        newlyRead.add(entry.notificationId);
        return { ...entry, readAt: at };
      }),
    })),
  };
}

function lowerUnreadCount(queryClient: QueryClient, by: number): void {
  if (by === 0) return;
  queryClient.setQueryData<UnreadCount>(queryKeys.unreadCount, (current) =>
    current === undefined ? current : { count: Math.max(0, current.count - by) },
  );
}

/**
 * 標成已讀之後立刻更新畫面：快取裡的清單換掉、側欄的未讀數少掉這幾則；沒載入到的那些算不出來，
 * 所以還是重抓一次，以伺服器的數字為準。
 */
export function markReadInCache(queryClient: QueryClient, notificationIds: readonly string[], at: string): void {
  const ids = new Set(notificationIds);
  const newlyRead = new Set<string>();
  queryClient.setQueriesData<Pages>({ queryKey: queryKeys.notificationLists }, (pages) =>
    pages === undefined ? pages : withRead(pages, ids, at, newlyRead),
  );
  lowerUnreadCount(queryClient, newlyRead.size);
  void queryClient.invalidateQueries({ queryKey: queryKeys.unreadCount });
}

/** 全部標為已讀：所有「全部」的清單（不分分類與搜尋）原位換掉，所有「未讀」的清單重抓（變成空的或剩沒讀到的），未讀數歸零。 */
export function markAllReadInCache(queryClient: QueryClient, at: string): void {
  queryClient.setQueriesData<Pages>({ queryKey: queryKeys.notificationListsOf(false) }, (pages) =>
    pages === undefined ? pages : withRead(pages, null, at, new Set()),
  );
  queryClient.setQueryData<UnreadCount>(queryKeys.unreadCount, { count: 0 });
  void queryClient.invalidateQueries({ queryKey: queryKeys.notificationListsOf(true) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.unreadCount });
}

/**
 * 後端在你留言、指派別人或改說明之後，大約 2 秒內由通知模組自動把你加進關注（事件流的讀者，api 每 2 秒處理一批）。
 * 做完這些事之後延遲一點再重抓關注的狀態，按鈕才會從「關注」換成「關注中」；馬上重抓只會讀到舊的。
 * 計時器掛在 queryClient 上，元件在中間被換掉（換頁、關面板）也照樣執行。
 */
export const AUTO_FOLLOW_REFRESH_MS = 2500;

export function refreshSubscriptionLater(queryClient: QueryClient, itemKey: string): void {
  setTimeout(() => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.itemSubscription(itemKey) });
  }, AUTO_FOLLOW_REFRESH_MS);
}
