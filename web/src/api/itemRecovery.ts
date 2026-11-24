import type { ApiErrorDetail } from './envelope';
import { apiRequest } from './http';
import { itemResourcePath } from './items';
import type { StateCategory } from './blueprint';
import type { CalendarDate, DeletedItem, Instant, Item, ItemDetail, PersonRef, Priority } from './types';

/**
 * 批次操作與復原刪除（PM-4a，internal/HANDOFF-pm4a-bulk-restore.md 第 2 節）。
 * 批次的每一筆各自照單一項目的規則做：一筆不行不影響其他的，回應分成成功與失敗兩份，順序照送出的編號。
 */

/** 一次最多幾個項目（和清單一頁的上限相同）。 */
export const BULK_MAX_ITEMS = 200;

/** 一筆沒做成的原因：和單一項目的操作失敗時的錯誤相同（代碼、訊息、細項）。 */
export interface BulkItemFailure {
  readonly key: string;
  readonly error: {
    readonly code: string;
    readonly message: string;
    readonly details: readonly ApiErrorDetail[];
  };
}

/**
 * 批次修改。state 和其他屬性只能擇一（一起給是 422 item.bulk_invalid）：
 * 給 state 是改狀態（每一筆照流程的轉換規則）；否則改有寫的屬性，寫 null 是清掉，沒寫（undefined）是不改。
 */
export type BulkUpdateRequest =
  | { readonly keys: readonly string[]; readonly state: string }
  | {
      readonly keys: readonly string[];
      readonly assigneeId?: string | null;
      readonly priority?: Priority;
      readonly startDate?: CalendarDate | null;
      readonly dueDate?: CalendarDate | null;
      readonly cycleId?: string | null;
      readonly milestoneId?: string | null;
    };

export interface BulkUpdateResult {
  /** 改好的項目（清單的形狀，沒有說明）；值本來就一樣的也在這裡。 */
  readonly updated: readonly Item[];
  readonly failed: readonly BulkItemFailure[];
}

export interface BulkDeleteResult {
  /** 刪除的項目；本來就已經刪除的也在這裡。 */
  readonly deleted: readonly DeletedItem[];
  /** 常見的是 item.has_children：還有不在這一批、沒刪除的下層。 */
  readonly failed: readonly BulkItemFailure[];
}

export interface BulkRestoreResult {
  /** 復原的項目（清單的形狀）；本來就沒有刪除的也在這裡。 */
  readonly restored: readonly Item[];
  /** 常見的是 item.parent_deleted（上層也刪除了、又不在這一批）與 item.type_removed（藍圖已經沒有這個類型）。 */
  readonly failed: readonly BulkItemFailure[];
}

/** 已刪除的項目（清單用，沒有說明與自訂欄位）。 */
export interface DeletedItemSummary {
  readonly key: string;
  readonly projectKey: string;
  readonly number: number;
  readonly type: string;
  readonly title: string;
  /** 刪除時的狀態；藍圖之後改過時可能已經不在流程裡（復原時伺服器才搬）。 */
  readonly state: string;
  readonly stateCategory: StateCategory;
  /** 刪除時的上層；上層可能也是刪除的。 */
  readonly parentKey: string | null;
  /** 刪除的人；舊資料沒有記錄時是 null。 */
  readonly deletedBy: PersonRef | null;
  readonly deletedAt: Instant;
}

export interface DeletedItemPage {
  readonly items: readonly DeletedItemSummary[];
  readonly nextCursor: string | null;
}

export interface DeletedItemQuery {
  /** 1 到 200，預設 50。 */
  readonly limit?: number;
  readonly cursor?: string;
}

function projectItemsPath(projectKey: string): string {
  return `/v1/projects/${encodeURIComponent(projectKey)}/items`;
}

/** 422 item.bulk_invalid（請求本身不合格，details 的 path 是 keys、state 或屬性名稱）、404 project.not_found、403 work.forbidden。 */
export function bulkUpdateItems(projectKey: string, request: BulkUpdateRequest): Promise<BulkUpdateResult> {
  return apiRequest<BulkUpdateResult>(`${projectItemsPath(projectKey)}/bulk-update`, { method: 'POST', body: request });
}

/** 上層和下層都在這一批時伺服器先刪下層，一起選的整串都刪得掉。 */
export function bulkDeleteItems(projectKey: string, keys: readonly string[]): Promise<BulkDeleteResult> {
  return apiRequest<BulkDeleteResult>(`${projectItemsPath(projectKey)}/bulk-delete`, { method: 'POST', body: { keys } });
}

/** 上層和下層都在這一批時伺服器先復原上層：「復原」整批刪除時整串回來。 */
export function bulkRestoreItems(projectKey: string, keys: readonly string[]): Promise<BulkRestoreResult> {
  return apiRequest<BulkRestoreResult>(`${projectItemsPath(projectKey)}/bulk-restore`, { method: 'POST', body: { keys } });
}

/**
 * 復原一個刪除的項目。沒有刪除時照樣回傳它（重送不會出錯）。
 * 409 item.parent_deleted（details 一筆，path 是 parent，先復原上層）、409 item.type_removed、404 item.not_found。
 */
export function restoreItem(itemKey: string): Promise<ItemDetail> {
  return apiRequest<ItemDetail>(`${itemResourcePath(itemKey)}/restore`, { method: 'POST' });
}

/** 最近刪除的在前。 */
export function listDeletedItems(
  projectKey: string,
  query: DeletedItemQuery = {},
  signal?: AbortSignal,
): Promise<DeletedItemPage> {
  const params = new URLSearchParams();
  if (query.limit !== undefined) params.append('limit', String(query.limit));
  if (query.cursor !== undefined) params.append('cursor', query.cursor);
  const text = params.toString();
  return apiRequest<DeletedItemPage>(`${projectItemsPath(projectKey)}/deleted${text === '' ? '' : `?${text}`}`, {
    signal,
  });
}
