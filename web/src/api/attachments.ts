import { ApiError, apiRequest, ClientErrorCodes, CSRF_HEADER, networkError, unwrapResponse } from './http';
import { itemResourcePath } from './items';
import type { Instant, PersonRef } from './types';

/**
 * 項目的附件（PM-5，internal/HANDOFF-pm5-attachments.md 第 2 節）。
 * 列出與下載要 item.read；上傳與刪除要 item.update（封存的專案 409 `project.archived`）。
 */

/** 一個檔案最大 25 MiB（和伺服器的 AttachmentRules.MaxBytes 一樣）：上傳前先擋，不必傳完才被退回。 */
export const ATTACHMENT_MAX_BYTES = 25 * 1024 * 1024;

/** 一個項目最多幾個附件。 */
export const ATTACHMENT_MAX_PER_ITEM = 100;

export interface Attachment {
  readonly id: string;
  readonly itemKey: string;
  readonly fileName: string;
  /** 伺服器整理過的類型；不像類型的是 application/octet-stream。 */
  readonly contentType: string;
  readonly sizeBytes: number;
  /** 內容的 SHA-256（小寫十六進位）。 */
  readonly sha256: string;
  readonly uploadedBy: PersonRef;
  readonly uploadedAt: Instant;
}

/** 先上傳的在前。看不到或已刪除的項目是 404 `item.not_found`。 */
export function listAttachments(itemKey: string, signal?: AbortSignal): Promise<readonly Attachment[]> {
  return apiRequest<readonly Attachment[]>(`${itemResourcePath(itemKey)}/attachments`, { signal });
}

/**
 * 下載的網址（同一個網域，Cookie 照常帶）。伺服器決定怎麼呈現：png、jpeg、gif、webp 在瀏覽器裡直接顯示，
 * 其他一律下載（Content-Disposition: attachment），SVG、HTML、PDF 也是下載：同一個網域打開使用者上傳的內容有安全風險。
 */
export function attachmentContentUrl(attachmentId: string): string {
  return `/v1/attachments/${encodeURIComponent(attachmentId)}/content`;
}

/** 刪除不能復原。回傳刪掉的那一個。404 `attachment.not_found`。 */
export function deleteAttachment(attachmentId: string): Promise<Attachment> {
  return apiRequest<Attachment>(`/v1/attachments/${encodeURIComponent(attachmentId)}`, { method: 'DELETE' });
}

export interface UploadOptions {
  /** 已經送出的位元組與總數；瀏覽器不知道總數時 total 是 null。 */
  readonly onProgress?: (sent: number, total: number | null) => void;
  readonly signal?: AbortSignal;
  /** 沒有信封的 413（代理伺服器擋的）要顯示的說明；沒給是項目附件的 25 MB。 */
  readonly tooLargeMessage?: string;
}

const HTTP_TOO_LARGE = 413;
const TOO_LARGE_CODE = 'attachment.too_large';
const TOO_LARGE_MESSAGE = `檔案超過 ${String(ATTACHMENT_MAX_BYTES / (1024 * 1024))} MB，無法上傳。`;

/**
 * 413 常常是前面的代理伺服器擋的，沒有 API 的信封：換成說得出原因的訊息與代碼，
 * 不然使用者看到的是「伺服器的回應格式不正確」。
 */
function explained(error: unknown, tooLargeMessage: string): Error {
  if (!(error instanceof ApiError)) return networkError();
  // 有信封的 413（例如助理附件的 ai.attachment_too_large）訊息已經說得出原因，只換沒有信封的。
  if (error.status !== HTTP_TOO_LARGE || error.code !== ClientErrorCodes.invalidResponse) return error;
  return new ApiError({ status: error.status, code: TOO_LARGE_CODE, message: tooLargeMessage });
}

function abortError(): DOMException {
  return new DOMException('上傳已取消。', 'AbortError');
}

/**
 * 上傳一個檔案到 url：multipart/form-data，欄位名稱 `file`（一次一個檔案；多個檔案就送多次）。
 * 成功 201，回傳伺服器的 JSON（項目與知識庫頁面的附件形狀不同，由呼叫端指定型別）。失敗：413 `attachment.too_large`、
 * 409 `attachment.limit_reached`、422 `attachment.name_invalid`、409 `project.archived`、403、404；丟出 ApiError
 * （信封與訊息的規則和 apiRequest 一樣，沒有信封的 413、5xx 也是）。
 * 用 XMLHttpRequest：fetch 拿不到上傳的進度。取消（signal）時丟出 AbortError，和 fetch 一樣，呼叫端分得出不是失敗。
 * 不設 Content-Type：瀏覽器要自己帶 multipart 的 boundary。
 */
export function uploadFile<T>(url: string, file: File, options: UploadOptions = {}): Promise<T> {
  const { onProgress, signal, tooLargeMessage = TOO_LARGE_MESSAGE } = options;
  return new Promise<T>((resolve, reject) => {
    if (signal?.aborted === true) {
      reject(abortError());
      return;
    }
    const request = new XMLHttpRequest();
    const cancel = () => {
      request.abort();
    };
    const finish = () => {
      signal?.removeEventListener('abort', cancel);
    };
    request.upload.addEventListener('progress', (event) => {
      onProgress?.(event.loaded, event.lengthComputable ? event.total : null);
    });
    request.addEventListener('load', () => {
      finish();
      try {
        resolve(unwrapResponse(request.status, request.responseText) as T);
      } catch (error) {
        reject(explained(error, tooLargeMessage));
      }
    });
    request.addEventListener('error', () => {
      finish();
      reject(networkError());
    });
    request.addEventListener('abort', () => {
      finish();
      reject(abortError());
    });
    signal?.addEventListener('abort', cancel, { once: true });
    const body = new FormData();
    body.append('file', file, file.name);
    request.open('POST', url);
    request.responseType = 'text';
    request.setRequestHeader('Accept', 'application/json');
    request.setRequestHeader(CSRF_HEADER, '1');
    request.send(body);
  });
}

/** 上傳一個檔案到項目：`POST /v1/items/{itemKey}/attachments`。 */
export function uploadAttachment(itemKey: string, file: File, options: UploadOptions = {}): Promise<Attachment> {
  return uploadFile<Attachment>(`${itemResourcePath(itemKey)}/attachments`, file, options);
}
