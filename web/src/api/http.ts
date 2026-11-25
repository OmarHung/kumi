import { parseEnvelope, type ApiErrorDetail } from './envelope';

export type { ApiErrorDetail } from './envelope';

/** 後端要求會改資料的請求帶這個標頭；為了簡單，所有請求都帶。 */
export const CSRF_HEADER = 'X-PmTool-Csrf';

/** 前端自己產生的錯誤代碼，和後端的代碼分開，避免撞名。 */
export const ClientErrorCodes = {
  network: 'client.network',
  invalidResponse: 'client.invalid_response',
} as const;

export const AUTH_REQUIRED_CODE = 'auth.required';

const MESSAGES = {
  network: '連不上伺服器，請確認網路連線後再試。',
  invalidResponse: '伺服器的回應格式不正確。',
  rateLimited: '請求太頻繁，請等幾秒再試。',
  serverError: '伺服器暫時無法處理，請稍後再試。',
} as const;

const HTTP_OK_MIN = 200;
const HTTP_OK_MAX = 300;
const HTTP_NO_CONTENT = 204;
const HTTP_UNAUTHORIZED = 401;
const HTTP_TOO_MANY_REQUESTS = 429;
const HTTP_SERVER_ERROR = 500;

interface ApiErrorInit {
  readonly status: number;
  readonly code: string;
  readonly message: string;
  readonly details?: readonly ApiErrorDetail[];
  readonly data?: unknown;
  readonly retryAfterSeconds?: number | null;
}

/** API 失敗時丟出的錯誤。status 是 0 表示請求根本沒有送到伺服器。 */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly details: readonly ApiErrorDetail[];
  /** 錯誤信封的 error.data：只有少數錯誤有，沒有時是 undefined。 */
  readonly data: unknown;
  /** 429 回應的 Retry-After（秒）；沒有標頭或格式不對是 null。 */
  readonly retryAfterSeconds: number | null;

  constructor({ status, code, message, details = [], data, retryAfterSeconds = null }: ApiErrorInit) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.details = details;
    this.data = data;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

/** 給畫面顯示的訊息。ApiError 的訊息已經是給人看的；其他例外是程式的問題，不把內容露給使用者。 */
export function errorMessage(error: unknown): string {
  return isApiError(error) ? error.message : '發生未預期的錯誤，請重新整理頁面再試。';
}

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export interface RequestOptions {
  readonly method?: HttpMethod;
  readonly body?: unknown;
  readonly signal?: AbortSignal;
}

// 登入狀態失效是跨畫面的事件：用戶端只負責通知，清狀態與導頁由外層決定。
const authEvents = new EventTarget();
const AUTH_REQUIRED_EVENT = 'auth-required';

/** 任何請求收到 401 auth.required 時呼叫 listener。回傳取消訂閱的函式。 */
export function onAuthRequired(listener: () => void): () => void {
  authEvents.addEventListener(AUTH_REQUIRED_EVENT, listener);
  return () => {
    authEvents.removeEventListener(AUTH_REQUIRED_EVENT, listener);
  };
}

function buildInit({ method = 'GET', body, signal }: RequestOptions): RequestInit {
  const headers: Record<string, string> = { Accept: 'application/json', [CSRF_HEADER]: '1' };
  const init: RequestInit = { method, headers, credentials: 'same-origin', signal: signal ?? null };
  if (body === undefined) {
    return init;
  }
  return {
    ...init,
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  };
}

async function send(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(path, init);
  } catch (error) {
    // 取消（換頁、查詢過期）要原樣往上丟，TanStack Query 才知道這不是失敗。
    if (init.signal?.aborted === true) {
      throw error;
    }
    throw networkError();
  }
}

function parseJson(text: string): unknown {
  if (text === '') {
    return undefined;
  }
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

// 429 與 5xx 的原始訊息常常是英文或代理伺服器的頁面，使用者看了也不知道怎麼辦，一律換成固定的說法。
function friendlyMessage(status: number, fallback: string): string {
  if (status === HTTP_TOO_MANY_REQUESTS) {
    return MESSAGES.rateLimited;
  }
  if (status >= HTTP_SERVER_ERROR) {
    return MESSAGES.serverError;
  }
  return fallback;
}

function errorWithoutEnvelope(status: number, retryAfterSeconds: number | null): ApiError {
  const message = friendlyMessage(status, MESSAGES.invalidResponse);
  const isInvalidResponse = message === MESSAGES.invalidResponse;
  const code = isInvalidResponse ? ClientErrorCodes.invalidResponse : `http.${String(status)}`;
  return new ApiError({ status, code, message, retryAfterSeconds });
}

const RETRY_AFTER_PATTERN = /^\d{1,6}$/;

/** Retry-After 只認「幾秒」的寫法（伺服器給的就是這種）；日期的寫法與不合理的值當作沒有。 */
function parseRetryAfter(header: string | null | undefined): number | null {
  const text = header?.trim() ?? '';
  return RETRY_AFTER_PATTERN.test(text) ? Number(text) : null;
}

/** 429 的說明：有 Retry-After 時寫出要等幾秒（登入與改密碼共用）。 */
export function tooManyAttemptsMessage(error: unknown): string {
  const seconds = isApiError(error) ? error.retryAfterSeconds : null;
  return seconds === null ? '嘗試太多次，請稍後再試。' : `嘗試太多次，請在 ${String(seconds)} 秒後再試。`;
}

/**
 * 拆開回應的信封（fetch 與上傳用的 XMLHttpRequest 共用，兩條路的成功、失敗與訊息才一致）：
 * 成功時回傳 data；失敗時丟出 ApiError。text 是回應的內文（可以是空的）。
 */
export function unwrapResponse(status: number, text: string, retryAfter?: string | null): unknown {
  const isOk = status >= HTTP_OK_MIN && status < HTTP_OK_MAX;
  if (isOk && status === HTTP_NO_CONTENT) {
    return null;
  }
  const retryAfterSeconds = parseRetryAfter(retryAfter);
  const envelope = parseEnvelope(parseJson(text));
  if (envelope === null) {
    throw errorWithoutEnvelope(status, retryAfterSeconds);
  }
  if (envelope.success && isOk) {
    return envelope.data;
  }
  if (envelope.success) {
    throw errorWithoutEnvelope(status, retryAfterSeconds);
  }

  const { code, message, details, data } = envelope.error;
  if (status === HTTP_UNAUTHORIZED && code === AUTH_REQUIRED_CODE) {
    authEvents.dispatchEvent(new Event(AUTH_REQUIRED_EVENT));
  }
  throw new ApiError({ status, code, message: friendlyMessage(status, message), details, data, retryAfterSeconds });
}

/** 請求根本沒有送到伺服器（斷線、DNS）。 */
export function networkError(): ApiError {
  return new ApiError({ status: 0, code: ClientErrorCodes.network, message: MESSAGES.network });
}

/**
 * 呼叫 API 並拆開信封。成功時回傳 data；失敗時丟出 ApiError。
 * data 的形狀由呼叫端的型別宣告，這一層只確認信封本身。之後改由 OpenAPI 產生時再補完整的檢查。
 */
export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await send(path, buildInit(options));
  return unwrapResponse(response.status, await response.text(), response.headers.get('Retry-After')) as T;
}
