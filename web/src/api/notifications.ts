import { itemResourcePath } from './items';
import { apiRequest } from './http';
import type { ItemSubscription, NotificationPage, NotificationQuery, UnreadCount, UpdatedCount } from './types';

function queryString(query: NotificationQuery): string {
  const params = new URLSearchParams();
  if (query.unread === true) params.append('unread', 'true');
  if (query.category !== undefined) params.append('category', query.category);
  if (query.q !== undefined && query.q !== '') params.append('q', query.q);
  if (query.cursor !== undefined) params.append('cursor', query.cursor);
  if (query.limit !== undefined) params.append('limit', String(query.limit));
  const text = params.toString();
  return text === '' ? '' : `?${text}`;
}

/** 我的通知，新的在前。limit 超出 1～100 或游標看不懂是 400。 */
export function listNotifications(query: NotificationQuery, signal?: AbortSignal): Promise<NotificationPage> {
  return apiRequest<NotificationPage>(`/v1/notifications${queryString(query)}`, { signal });
}

export function getUnreadCount(signal?: AbortSignal): Promise<UnreadCount> {
  return apiRequest<UnreadCount>('/v1/notifications/unread-count', { signal });
}

/** 標成已讀（1～200 則）；已經讀過的不算在 updated 裡。 */
export function markNotificationsRead(notificationIds: readonly string[]): Promise<UpdatedCount> {
  return apiRequest<UpdatedCount>('/v1/notifications/read', { method: 'POST', body: { notificationIds } });
}

export function markAllNotificationsRead(): Promise<UpdatedCount> {
  return apiRequest<UpdatedCount>('/v1/notifications/read-all', { method: 'POST' });
}

/** 項目看不到或不存在是 404 item.not_found。 */
export function getItemSubscription(itemKey: string, signal?: AbortSignal): Promise<ItemSubscription> {
  return apiRequest<ItemSubscription>(`${itemResourcePath(itemKey)}/subscription`, { signal });
}

/** 關注或取消關注，回傳之後的狀態。 */
export function setItemSubscription(itemKey: string, following: boolean): Promise<ItemSubscription> {
  return apiRequest<ItemSubscription>(`${itemResourcePath(itemKey)}/subscription`, { method: 'PUT', body: { following } });
}
