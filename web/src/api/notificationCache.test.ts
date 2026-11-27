import { QueryClient, type InfiniteData } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { markAllReadInCache } from './notificationCache';
import { queryKeys } from './queryKeys';
import type { NotificationPage } from './types';
import { notification } from '../test/notifications';

type Pages = InfiniteData<NotificationPage, string | null>;

function pagesOf(...ids: readonly ReturnType<typeof notification>[]): Pages {
  return { pages: [{ items: [...ids], nextCursor: null }], pageParams: [null] };
}

function readAtsOf(client: QueryClient, key: readonly unknown[]): (string | null)[] {
  return (client.getQueryData<Pages>(key)?.pages[0]?.items ?? []).map((entry) => entry.readAt);
}

describe('markAllReadInCache', () => {
  it('所有「全部」的清單（不分分類與搜尋）原位換成已讀，「未讀」的清單重抓、不原位改', () => {
    // Arrange
    const client = new QueryClient();
    const unread = notification();
    const allKey = queryKeys.notifications(false);
    const mentionsKey = queryKeys.notifications(false, 'mentions');
    const searchKey = queryKeys.notifications(false, undefined, '登入');
    const unreadKey = queryKeys.notifications(true, 'assigned');
    for (const key of [allKey, mentionsKey, searchKey, unreadKey]) client.setQueryData(key, pagesOf(unread));
    client.setQueryData(queryKeys.unreadCount, { count: 1 });

    // Act
    markAllReadInCache(client, '2026-11-27T09:00:00Z');

    // Assert
    for (const key of [allKey, mentionsKey, searchKey]) {
      expect(readAtsOf(client, key)).toEqual(['2026-11-27T09:00:00Z']);
      expect(client.getQueryState(key)?.isInvalidated).toBe(false);
    }
    expect(readAtsOf(client, unreadKey)).toEqual([null]);
    expect(client.getQueryState(unreadKey)?.isInvalidated).toBe(true);
    expect(client.getQueryData(queryKeys.unreadCount)).toEqual({ count: 0 });
  });
});
