import { apiRequest } from './http';
import type {
  AccessToken,
  ChangePasswordRequest,
  CreateAccessTokenRequest,
  CreatedAccessToken,
  PasswordChanged,
  SignedOutSessions,
  SignOutOtherSessionsRequest,
} from './types';

/**
 * 改自己的密碼。成功後同一個帳號的其他登入全部作廢；這一個換成新的（回應帶新的 Cookie）。
 * request.revokeAccessTokens 是 true 時一併撤銷所有個人存取權杖；回傳作廢了幾個登入、撤銷了幾把權杖。
 * 失敗：422 account.password_incorrect（細項路徑 currentPassword）、person.password_too_short／too_long（newPassword）；
 * 429 request.rate_limited（依人限流，同一個人改密碼太頻繁時）。
 */
export function changeOwnPassword(request: ChangePasswordRequest): Promise<PasswordChanged> {
  return apiRequest<PasswordChanged>('/v1/account/password', { method: 'PUT', body: request });
}

/**
 * 登出其他裝置：要輸入目前的密碼。同一個帳號的其他登入全部作廢，這一個換成新的（回應帶新的 Cookie）；回傳作廢了幾個（0 是沒有別的登入）。只動登入，不動存取權杖。
 * 失敗：422 account.password_incorrect（細項路徑 currentPassword）；429 request.rate_limited（帶 Retry-After）。
 */
export function signOutOtherSessions(request: SignOutOtherSessionsRequest): Promise<SignedOutSessions> {
  return apiRequest<SignedOutSessions>('/v1/account/other-sessions/sign-out', { method: 'POST', body: request });
}

/** 自己的存取權杖（新的在前，已過期的也在，沒有明文）。 */
export function listAccessTokens(signal?: AbortSignal): Promise<readonly AccessToken[]> {
  return apiRequest<readonly AccessToken[]>('/v1/account/tokens', { signal });
}

/**
 * 建立存取權杖，回應帶明文（只有這一次）。失敗：422 access_token.name_required／name_too_long（細項路徑 name）、
 * scopes_required（scopes）、expiry_invalid（expiresInDays：沒給、超過 365 天，勾了本機代理超過 90 天）；409 access_token.limit_reached。
 */
export function createAccessToken(request: CreateAccessTokenRequest): Promise<CreatedAccessToken> {
  return apiRequest<CreatedAccessToken>('/v1/account/tokens', { method: 'POST', body: request });
}

/** 撤銷一把存取權杖，回傳撤銷的那一把。404 access_token.not_found：不是自己的、不存在、已經撤銷。 */
export function revokeAccessToken(tokenId: string): Promise<AccessToken> {
  return apiRequest<AccessToken>(`/v1/account/tokens/${encodeURIComponent(tokenId)}`, { method: 'DELETE' });
}
