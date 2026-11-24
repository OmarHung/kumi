import { http, type JsonBodyType } from 'msw';
import type { ApiErrorDetail } from '../api/envelope';
import type { ImportedItemRequest, ItemImportRequest } from '../api/itemImport';
import type { Person } from '../api/types';
import { failure, ok } from './api';
import { memberPerson, ownerPerson } from './people';
import { server } from './server';

/** 伺服器檢查不過時回的細項：index 是送出的第幾筆（從 0 算）。 */
export function rowDetail(index: number, field: string | null, message: string): ApiErrorDetail {
  return { code: 'item.invalid', path: field === null ? `rows/${String(index)}` : `rows/${String(index)}/${field}`, message };
}

export interface ItemImportOptions {
  /** GET /v1/people 回的人員（Email 對應用）；預設是範例擁有者與成員。 */
  readonly people?: readonly Person[];
  /** 每一次收到匯入請求時檢查：回細項就是 422 item.import_invalid，回 null 就接受。 */
  readonly validate?: (rows: readonly ImportedItemRequest[], request: ItemImportRequest) => readonly ApiErrorDetail[] | null;
  /** 正式匯入（不是乾跑）要等這個 promise 完成才回應：測送出中的狀態。 */
  readonly gate?: Promise<unknown>;
  /** 第一個建立的編號；預設 12。 */
  readonly firstNumber?: number;
  /** 讓匯入請求直接失敗（403、409…）。 */
  readonly reject?: { readonly onlySubmit?: boolean; readonly status: number; readonly code: string; readonly message: string; readonly details?: JsonBodyType };
}

export interface ServedItemImport {
  /** 收到的每一個匯入請求（含乾跑），依序。 */
  readonly requests: ItemImportRequest[];
  /** 其中正式匯入（dryRun: false）的。 */
  readonly submitted: () => readonly ItemImportRequest[];
}

/** 假的匯入端點：POST /v1/projects/{key}/items/import 與人員清單。 */
export function serveItemImport(projectKey: string, options: ItemImportOptions = {}): ServedItemImport {
  const requests: ItemImportRequest[] = [];
  const { people = [ownerPerson, memberPerson], firstNumber = 12 } = options;
  server.use(
    http.get('/v1/people', () => ok([...people])),
    http.post(`/v1/projects/${projectKey}/items/import`, async ({ request }) => {
      const body = (await request.json()) as ItemImportRequest;
      requests.push(body);
      if (!body.dryRun) await options.gate;
      if (options.reject !== undefined && !(options.reject.onlySubmit === true && body.dryRun)) {
        return failure(options.reject.status, options.reject.code, options.reject.message, options.reject.details);
      }
      const details = options.validate?.(body.rows, body) ?? null;
      if (details !== null && details.length > 0) {
        return failure(422, 'item.import_invalid', '匯入的資料有問題，一列都沒有建立。', details);
      }
      const createdKeys = body.dryRun ? [] : body.rows.map((_, index) => `${projectKey}-${String(firstNumber + index)}`);
      return ok({ dryRun: body.dryRun, count: body.rows.length, createdKeys });
    }),
  );
  return { requests, submitted: () => requests.filter((request) => !request.dryRun) };
}

/** 文字的 UTF-16（加 BOM）位元組，模擬 Excel 的「Unicode 文字」。 */
export function utf16Bytes(text: string, order: 'le' | 'be' = 'le'): Uint8Array<ArrayBuffer> {
  const units = Array.from({ length: text.length }, (_, index) => text.charCodeAt(index));
  const pairs = units.flatMap((unit) => (order === 'le' ? [unit & 0xff, unit >> 8] : [unit >> 8, unit & 0xff]));
  return new Uint8Array(order === 'le' ? [0xff, 0xfe, ...pairs] : [0xfe, 0xff, ...pairs]);
}
