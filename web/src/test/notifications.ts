import { http } from 'msw';
import type { AccountNotice, AccountSecurityNotification, ItemNotification } from '../api/notificationTypes';
import type { ItemSubscription, Notification, NotificationPage } from '../api/types';
import { failure, ok } from './api';
import { ownerRef } from './activity';
import { server } from './server';

let sequence = 0;

/** 和合約一樣的形狀：預設是 owner 把 SMK-7 指派給你，還沒讀。 */
export function notification(overrides: Partial<ItemNotification> = {}): ItemNotification {
  sequence += 1;
  return {
    notificationId: `0190b000-0000-7000-8000-${String(sequence).padStart(12, '0')}`,
    kind: 'assigned',
    projectKey: 'SMK',
    itemKey: 'SMK-7',
    itemTitle: '首頁設計稿',
    actor: ownerRef,
    fromStateName: null,
    toStateName: null,
    excerpt: null,
    commentId: null,
    createdAt: '2026-11-18T08:00:00Z',
    readAt: null,
    ...overrides,
  };
}

/** 預設是命令列在「MacBook-Pro」登入、建立了兩把權杖（來源 203.0.113.5），還沒讀；做事的人是本人。 */
export function accountNotification(
  account: Partial<AccountNotice> = {},
  overrides: Partial<AccountSecurityNotification> = {},
): AccountSecurityNotification {
  sequence += 1;
  return {
    notificationId: `0190b100-0000-7000-8000-${String(sequence).padStart(12, '0')}`,
    kind: 'account_security',
    projectKey: null,
    itemKey: null,
    itemTitle: null,
    actor: ownerRef,
    fromStateName: null,
    toStateName: null,
    excerpt: null,
    commentId: null,
    createdAt: '2026-11-18T08:00:00Z',
    readAt: null,
    account: {
      event: 'cli_login_tokens_issued',
      tokens: [
        { name: 'kumi（MacBook-Pro）', scopes: ['items:read', 'items:write'], expiresAt: null },
        { name: 'kumi 本機代理（MacBook-Pro）', scopes: ['agent:run'], expiresAt: '2027-02-23T00:00:00Z' },
      ],
      scopes: [],
      sourceAddress: '203.0.113.5',
      machineName: 'MacBook-Pro',
      clientName: null,
      clientHost: null,
      revokedTokens: 0,
      ...account,
    },
    ...overrides,
  };
}

export interface NotificationRequests {
  /** GET /v1/notifications 收到的網址。 */
  readonly lists: URL[];
  /** POST /v1/notifications/read 的內文。 */
  readonly reads: (readonly string[])[];
  /** POST /v1/notifications/read-all 的次數。 */
  readonly readAlls: { count: number };
  /** GET /v1/notifications/unread-count 的次數。 */
  readonly counts: { count: number };
}

interface ServeOptions {
  /** 一頁幾則（測「載入更多」用）；預設 50。 */
  readonly pageSize?: number;
}

/**
 * 有狀態的假收件匣：標成已讀之後清單與未讀數都跟著變，和後端一樣。新的在前，游標是「已經給了幾則」。
 * 之後的 server.use 蓋過它。
 */
export function serveNotifications(initial: readonly Notification[], options: ServeOptions = {}): NotificationRequests {
  const { pageSize = 50 } = options;
  let all = [...initial];
  const requests: NotificationRequests = { lists: [], reads: [], readAlls: { count: 0 }, counts: { count: 0 } };
  const unread = () => all.filter((entry) => entry.readAt === null);
  server.use(
    http.get('/v1/notifications/unread-count', () => {
      requests.counts.count += 1;
      return ok({ count: unread().length });
    }),
    http.get('/v1/notifications', ({ request }) => {
      const url = new URL(request.url);
      requests.lists.push(url);
      const source = url.searchParams.get('unread') === 'true' ? unread() : all;
      const start = Number(url.searchParams.get('cursor') ?? '0');
      const limit = Number(url.searchParams.get('limit') ?? String(pageSize));
      const size = Math.min(limit, pageSize);
      const items = source.slice(start, start + size);
      const page: NotificationPage = { items, nextCursor: start + size < source.length ? String(start + size) : null };
      return ok(page);
    }),
    http.post('/v1/notifications/read', async ({ request }) => {
      const body = (await request.json()) as { readonly notificationIds: readonly string[] };
      requests.reads.push(body.notificationIds);
      const before = unread().length;
      all = all.map((entry) =>
        body.notificationIds.includes(entry.notificationId) && entry.readAt === null
          ? { ...entry, readAt: '2026-11-18T09:00:00Z' }
          : entry,
      );
      return ok({ updated: before - unread().length });
    }),
    http.post('/v1/notifications/read-all', () => {
      requests.readAlls.count += 1;
      const updated = unread().length;
      all = all.map((entry) => (entry.readAt === null ? { ...entry, readAt: '2026-11-18T09:00:00Z' } : entry));
      return ok({ updated });
    }),
  );
  return requests;
}

/** 讓收件匣請求失敗（讀不到的狀態）。 */
export function failNotifications(status = 500): void {
  server.use(http.get('/v1/notifications', () => failure(status, 'server.error', '伺服器暫時有問題。')));
}

/** 假的關注狀態：PUT 之後狀態跟著變（取消關注之後 reason 是 null，關注是 manual）。canFollow 預設 true；false 時 PUT 回 409。回傳收到的 PUT 內文。 */
export function serveSubscription(
  itemKey: string,
  initial: Omit<ItemSubscription, 'canFollow'> & { readonly canFollow?: boolean },
): readonly boolean[] {
  const puts: boolean[] = [];
  const canFollow = initial.canFollow ?? true;
  let current: ItemSubscription = { ...initial, canFollow };
  server.use(
    http.get(`/v1/items/${itemKey}/subscription`, () => ok(current)),
    http.put(`/v1/items/${itemKey}/subscription`, async ({ request }) => {
      const body = (await request.json()) as { readonly following: boolean };
      puts.push(body.following);
      if (!canFollow) return failure(409, 'subscription.not_member', '要先加入這個專案，才能關注它的項目。');
      current = body.following ? { following: true, reason: 'manual', canFollow } : { following: false, reason: null, canFollow };
      return ok(current);
    }),
  );
  return puts;
}
