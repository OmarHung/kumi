/** 失敗時的細項：哪個欄位（path）出了什麼問題。path 在核心欄位用文件裡的名字，例如 due_date。 */
export interface ApiErrorDetail {
  readonly code: string;
  readonly path: string | null;
  readonly message: string;
}

export interface EnvelopeError {
  readonly code: string;
  readonly message: string;
  readonly details: readonly ApiErrorDetail[];
  /** 少數錯誤帶的附加資料（例如 OAuth 授權請求的 error 與 redirectTo）；形狀由呼叫端自己確認。 */
  readonly data?: unknown;
}

export type Envelope =
  | { readonly success: true; readonly data: unknown }
  | { readonly success: false; readonly error: EnvelopeError };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function parseDetail(value: unknown): ApiErrorDetail | null {
  if (!isRecord(value) || typeof value.code !== 'string' || typeof value.message !== 'string') {
    return null;
  }
  const path = typeof value.path === 'string' ? value.path : null;
  return { code: value.code, path, message: value.message };
}

// 細項是附加資訊：形狀不對的單筆略過就好，不必讓整個錯誤變成「格式不正確」。
function parseDetails(value: unknown): readonly ApiErrorDetail[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value.map(parseDetail).filter((detail): detail is ApiErrorDetail => detail !== null);
}

function parseError(value: unknown): EnvelopeError | null {
  if (!isRecord(value) || typeof value.code !== 'string' || typeof value.message !== 'string') {
    return null;
  }
  const base = { code: value.code, message: value.message, details: parseDetails(value.details) };
  return 'data' in value && value.data !== null && value.data !== undefined ? { ...base, data: value.data } : base;
}

/** 回應是外部資料：只認得固定形狀的信封，其他一律回 null，由呼叫端當成格式錯誤。 */
export function parseEnvelope(value: unknown): Envelope | null {
  if (!isRecord(value) || typeof value.success !== 'boolean') {
    return null;
  }
  if (value.success) {
    return 'data' in value ? { success: true, data: value.data } : null;
  }
  const error = parseError(value.error);
  return error === null ? null : { success: false, error };
}
