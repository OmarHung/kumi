import { apiRequest } from './http';
import type { NewItemRequest } from './types';

/**
 * 匯入項目（PM-4b，internal/HANDOFF-pm4b-projects-import.md 第 2 節）。檔案在網頁上讀、對應欄位、轉好值，再送到伺服器。
 * 全部或全部不：任何一列不合格就一列都不建立，回 422 `item.import_invalid`，
 * details 每個問題一筆，path 是 `rows/<列的索引>/<欄位>`（例如 rows/3/due_date、rows/0/title、rows/7/state；
 * 欄位的寫法和新增項目的驗證錯誤一樣：核心欄位用 due_date 這類名字、自訂欄位用 key）。
 * 請求本身不合格（沒有列、超過 IMPORT_MAX_ROWS 列）也是 item.import_invalid，path 是 rows。
 * 封存的專案是 409 `project.archived`；沒有 item.create（或給了非起始狀態、卻沒有 project.manage）是 403 `work.forbidden`。
 */

/** 一次最多幾列。 */
export const IMPORT_MAX_ROWS = 1_000;

/** 匯入的一列：新增項目的內容，加上可以直接指定的狀態（不走轉換；給了非起始狀態要 project.manage）。 */
export interface ImportedItemRequest extends NewItemRequest {
  /** 狀態的 key；不給是流程的第一個狀態。 */
  readonly state?: string;
}

export interface ItemImportRequest {
  readonly rows: readonly ImportedItemRequest[];
  /** 只檢查、不建立（預覽用）。 */
  readonly dryRun: boolean;
}

export interface ItemImportResult {
  readonly dryRun: boolean;
  /** 合格的列數；只檢查時是「會建立幾個」。 */
  readonly count: number;
  /** 建立的項目編號，照列的順序；只檢查時是空的。 */
  readonly createdKeys: readonly string[];
}

export function importItems(projectKey: string, request: ItemImportRequest): Promise<ItemImportResult> {
  return apiRequest<ItemImportResult>(`/v1/projects/${encodeURIComponent(projectKey)}/items/import`, {
    method: 'POST',
    body: request,
  });
}
