import { apiRequest } from './http';
import type { SessionData, SignInRequest } from './types';

/** 沒登入時丟出 401 `auth.required` 的 ApiError。 */
export function getSession(signal?: AbortSignal): Promise<SessionData> {
  return apiRequest<SessionData>('/v1/session', { signal });
}

/** 帳號或密碼不對時丟出 401 `auth.invalid_credentials`。 */
export function signIn(request: SignInRequest): Promise<SessionData> {
  return apiRequest<SessionData>('/v1/session', { method: 'POST', body: request });
}

export function signOut(): Promise<null> {
  return apiRequest<null>('/v1/session', { method: 'DELETE' });
}
