import { http } from 'msw';
import type { AuditEvent, AuditPage } from '../api/audit';
import { failure, ok } from './api';
import { server } from './server';

/** 一筆稽核紀錄（假資料）；沒給的欄位是「擁有者登入成功、來源 203.0.113.5」。 */
export function auditEventOf(overrides: Partial<AuditEvent> = {}): AuditEvent {
  return {
    eventId: '0190b000-0000-7000-8000-000000000001',
    occurredAt: '2026-11-25T02:30:00Z',
    action: 'sign_in',
    outcome: 'succeeded',
    actor: { personId: '0190a000-0000-7000-8000-000000000001', displayName: '範例使用者' },
    target: null,
    sourceAddress: '203.0.113.5',
    details: {},
    ...overrides,
  };
}

/** n 筆編號不同、時間依序往前的紀錄（新的在前）。 */
export function auditEvents(count: number): readonly AuditEvent[] {
  return Array.from({ length: count }, (_, index) =>
    auditEventOf({
      eventId: `0190b000-0000-7000-8000-${String(1000 - index).padStart(12, '0')}`,
      occurredAt: new Date(Date.UTC(2026, 10, 25, 2, 30) - index * 60_000).toISOString(),
      sourceAddress: `203.0.113.${String(index + 1)}`,
    }),
  );
}

export interface AuditBackend {
  /** GET /v1/audit-events 收到的查詢字串（照順序）。 */
  readonly requests: URLSearchParams[];
}

interface AuditOptions {
  /** 一頁最多幾筆；沒給就用請求的 limit（伺服器的預設與上限另外照合約）。 */
  readonly pageSize?: number;
}

/**
 * 假的稽核紀錄端點，照合約：`GET /v1/audit-events?limit=&cursor=`，新的在前；回 `{ items, nextCursor }`，
 * 還有下一頁時 nextCursor 是不透明的字串（這裡用「已經給了幾筆」），到底了是 null；游標不對是 422 audit.query_invalid（路徑 cursor）。
 */
export function serveAudit(events: readonly AuditEvent[], { pageSize }: AuditOptions = {}): AuditBackend {
  const backend: AuditBackend = { requests: [] };
  server.use(
    http.get('/v1/audit-events', ({ request }) => {
      const query = new URL(request.url).searchParams;
      backend.requests.push(query);
      const cursor = query.get('cursor');
      const start = cursor === null ? 0 : Number(cursor.replace(/^after-/, ''));
      if (cursor !== null && !/^after-\d+$/.test(cursor)) {
        const issue = { code: 'audit.query_invalid', path: 'cursor', message: '游標不對。' };
        return failure(422, issue.code, issue.message, [issue]);
      }
      const limit = pageSize ?? Number(query.get('limit') ?? 50);
      const items = events.slice(start, start + limit);
      const end = start + items.length;
      const page: AuditPage = { items, nextCursor: end < events.length ? `after-${String(end)}` : null };
      return ok(page);
    }),
  );
  return backend;
}

/** 讀取稽核紀錄被擋下（不是擁有者）：403 person.forbidden。 */
export function forbidAudit(): void {
  server.use(http.get('/v1/audit-events', () => failure(403, 'person.forbidden', '只有組織的擁有者可以看稽核紀錄。')));
}
