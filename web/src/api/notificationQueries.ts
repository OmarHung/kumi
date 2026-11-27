import { infiniteQueryOptions, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getItemSubscription,
  getUnreadCount,
  listNotifications,
  markAllNotificationsRead,
  markNotificationsRead,
  setItemSubscription,
} from './notifications';
import { isApiError } from './http';
import { markAllReadInCache, markReadInCache } from './notificationCache';
import { queryKeys } from './queryKeys';
import type { ItemSubscription, NotificationCategory } from './types';

/** 後端的錯誤代碼：不是專案成員，不能關注（409）。 */
export const NOT_MEMBER_CODE = 'subscription.not_member';

/** 未讀數多久問一次（也在回到分頁時問一次，HANDOFF-pm1-collaboration.md 第 2 節第 6 點）。 */
export const UNREAD_POLL_MS = 60_000;

/**
 * 側欄與分頁標題、圖示的未讀數。staleTime 是 0：回到分頁時一定重抓（共用的預設是 30 秒內不重抓），之後每 60 秒一次。
 * 分頁在背景時照樣問：分頁的標題與圖示就是給看別的分頁的人看的，即時連線斷了的時候不能等到切回來才更新（一分鐘一次的小請求）。
 * 讀不到（沒有 items:read、網路）時側欄不顯示數字，不丟錯誤畫面。
 */
export function useUnreadCount() {
  return useQuery({
    queryKey: queryKeys.unreadCount,
    queryFn: ({ signal }) => getUnreadCount(signal),
    select: (data) => data.count,
    staleTime: 0,
    refetchInterval: UNREAD_POLL_MS,
    refetchIntervalInBackground: true,
    refetchOnWindowFocus: true,
  });
}

/** 一頁最多這麼多則（合約的預設與上限之間，載入更多時再接下一頁）。 */
export const NOTIFICATION_PAGE_SIZE = 50;

/** 收件匣的通知，新的在前，一頁一頁往下接。unread 是「只看未讀」；category 沒給是全部分類；q 是搜尋字（空的不搜尋）。 */
export function useNotifications(unread: boolean, category?: NotificationCategory, q = '') {
  return useInfiniteQuery(
    infiniteQueryOptions({
      queryKey: queryKeys.notifications(unread, category, q),
      queryFn: ({ pageParam, signal }) =>
        listNotifications(
          {
            unread,
            category,
            q,
            limit: NOTIFICATION_PAGE_SIZE,
            ...(pageParam === null ? {} : { cursor: pageParam }),
          },
          signal,
        ),
      initialPageParam: null as string | null,
      getNextPageParam: (lastPage) => lastPage.nextCursor,
    }),
  );
}

/** 標成已讀：畫面上先換掉（不等重抓），伺服器的數字照樣以重抓為準。 */
export function useMarkRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (notificationIds: readonly string[]) => markNotificationsRead(notificationIds),
    onSuccess: (_result, notificationIds) => {
      markReadInCache(queryClient, notificationIds, new Date().toISOString());
    },
  });
}

export function useMarkAllRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => markAllNotificationsRead(),
    onSuccess: () => {
      markAllReadInCache(queryClient, new Date().toISOString());
    },
  });
}

/** 這個人對這個項目的關注狀態。讀不到（看不到項目、沒有 items:read）時按鈕不出現。 */
export function useItemSubscription(itemKey: string) {
  return useQuery({
    queryKey: queryKeys.itemSubscription(itemKey),
    queryFn: ({ signal }) => getItemSubscription(itemKey, signal),
    // 後端會在留言、指派之後自動關注：回到頁面或重新掛上時一律問一次，不用 30 秒內的舊答案。
    staleTime: 0,
  });
}

/** 關注或取消關注：回應就是之後的狀態，直接寫進快取。 */
export function useSetSubscription(itemKey: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (following: boolean) => setItemSubscription(itemKey, following),
    onSuccess: (subscription) => {
      queryClient.setQueryData(queryKeys.itemSubscription(itemKey), subscription);
    },
    // 409 subscription.not_member：畫面上的狀態過時了（成員被移除、不是成員），按鈕改成「不能關注」。
    onError: (error) => {
      if (!isApiError(error) || error.code !== NOT_MEMBER_CODE) return;
      queryClient.setQueryData<ItemSubscription>(queryKeys.itemSubscription(itemKey), (current) =>
        current === undefined ? current : { following: false, reason: null, canFollow: false },
      );
    },
  });
}
