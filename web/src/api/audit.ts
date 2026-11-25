import { useInfiniteQuery } from '@tanstack/react-query';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';
import type { Instant } from './types';

/**
 * 稽核紀錄裡的人（做的人、對象）：沒有時是 null（例如登入失敗、找不到帳號）。
 * 紀錄比人久：那個人已經不在組織時，編號還在、名字是 null。
 */
export interface AuditPersonRef {
  readonly personId: string;
  readonly displayName: string | null;
}

export type AuditOutcome = 'succeeded' | 'failed';

export interface AuditEvent {
  readonly eventId: string;
  readonly occurredAt: Instant;
  /** 動作的代碼（對外的合約，例如 sign_in、access_token_created）；網頁不認得的照原字串顯示。 */
  readonly action: string;
  readonly outcome: AuditOutcome;
  readonly actor: AuditPersonRef | null;
  readonly target: AuditPersonRef | null;
  readonly sourceAddress: string | null;
  /** 動作的細項（字串對字串，例如 from、to、tokenName、scopes）。 */
  readonly details: Readonly<Record<string, string>>;
}

export interface AuditPage {
  readonly items: readonly AuditEvent[];
  /** 還有下一頁時的游標；null 是到底了。 */
  readonly nextCursor: string | null;
}

/** 一頁的筆數（伺服器預設 50、最多 100）：資料量大，一次不全部載入。 */
export const AUDIT_PAGE_SIZE = 50;

/**
 * 稽核紀錄，新的在前（`GET /v1/audit-events?limit=&cursor=&person=`）。只有組織的擁有者讀得到（403 person.forbidden）；
 * 游標不對是 422 audit.query_invalid（路徑 cursor）。
 */
export function listAuditEvents(cursor: string | null, signal?: AbortSignal): Promise<AuditPage> {
  const params = new URLSearchParams({ limit: String(AUDIT_PAGE_SIZE) });
  if (cursor !== null) params.set('cursor', cursor);
  return apiRequest<AuditPage>(`/v1/audit-events?${params.toString()}`, { signal });
}

/** 一頁一頁往後接；沒有權限的人（isEnabled 是 false）不發請求。 */
export function useAuditEvents(isEnabled: boolean) {
  return useInfiniteQuery({
    queryKey: queryKeys.auditEvents,
    queryFn: ({ pageParam, signal }) => listAuditEvents(pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
    enabled: isEnabled,
    retry: false,
  });
}
