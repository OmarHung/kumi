import { apiRequest } from './http';

/** 儲存空間用量的分類：項目的附件、知識庫的附件。網頁不認得的分類照原字串顯示。 */
export type StorageArea = 'item_attachments' | 'knowledge_attachments' | 'assistant_attachments';

export interface StorageAreaUsage {
  readonly area: StorageArea;
  readonly bytes: number;
  readonly files: number;
}

/** 組織的儲存空間用量（`GET /v1/storage-usage`）。限制是 null 代表不限制（部署設定的 0）。 */
export interface StorageUsage {
  /** 組織的附件總量上限（位元組）。 */
  readonly organizationLimitBytes: number | null;
  /** 每個人過去 24 小時上傳量的上限（位元組）。 */
  readonly personDailyLimitBytes: number | null;
  readonly totalBytes: number;
  readonly areas: readonly StorageAreaUsage[];
}

/** 只有組織管理員讀得到（403 storage.forbidden）；已經超過上限時只有新的上傳被擋，用量照常回傳。 */
export function fetchStorageUsage(signal?: AbortSignal): Promise<StorageUsage> {
  return apiRequest<StorageUsage>('/v1/storage-usage', { signal });
}
