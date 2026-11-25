import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, isApiError } from './http';
import { isNavigableUrl } from '../lib/navigableUrl';
import { queryKeys } from './queryKeys';
import type { AccessTokenScope, Instant } from './types';

/** 授權請求的參數，照網址列上的 OAuth 參數轉成 camelCase（internal/HANDOFF-mcp.md 4.6）；網址上沒有的欄位不送。 */
export interface AuthorizationRequest {
  readonly responseType?: string;
  readonly clientId?: string;
  readonly redirectUri?: string;
  readonly scope?: string;
  readonly state?: string;
  readonly codeChallenge?: string;
  readonly codeChallengeMethod?: string;
  readonly resource?: string;
}

export interface AuthorizationAccount {
  readonly displayName: string;
  readonly email: string;
  readonly organizationName: string;
}

export interface AuthorizationCheck {
  readonly clientId: string;
  readonly clientName: string;
  /** Client ID Metadata Document 的網域；動態註冊（沒有驗證過）的是 null。 */
  readonly clientHost: string | null;
  readonly redirectHost: string;
  readonly redirectIsLoopback: boolean;
  /**
   * 導回的網址和應用（client_id）是不是同一個網站（導回本機算是；同網域或其中一方是另一方的子網域也算）。
   * false 時同意頁要顯著警告；舊的伺服器沒有這個欄位，不警告。
   */
  readonly redirectMatchesClient?: boolean;
  readonly requestedScopes: readonly AccessTokenScope[];
  readonly account: AuthorizationAccount;
}

export interface AuthorizationRedirect {
  readonly redirectTo: string;
}

export interface OAuthGrant {
  readonly grantId: string;
  readonly clientName: string;
  readonly clientHost: string | null;
  readonly scopes: readonly AccessTokenScope[];
  readonly createdAt: Instant;
  readonly lastUsedAt: Instant | null;
}

const BASE = '/v1/oauth/authorization';

export function checkAuthorization(request: AuthorizationRequest, signal?: AbortSignal): Promise<AuthorizationCheck> {
  return apiRequest<AuthorizationCheck>(`${BASE}/check`, { method: 'POST', body: request, signal });
}

export function approveAuthorization(
  request: AuthorizationRequest,
  scopes: readonly AccessTokenScope[],
): Promise<AuthorizationRedirect> {
  return apiRequest<AuthorizationRedirect>(`${BASE}/approve`, { method: 'POST', body: { ...request, scopes } });
}

export function denyAuthorization(request: AuthorizationRequest): Promise<AuthorizationRedirect> {
  return apiRequest<AuthorizationRedirect>(`${BASE}/deny`, { method: 'POST', body: request });
}

export function listOAuthGrants(signal?: AbortSignal): Promise<readonly OAuthGrant[]> {
  return apiRequest<readonly OAuthGrant[]>('/v1/account/oauth-grants', { signal });
}

export function revokeOAuthGrant(grantId: string): Promise<null> {
  return apiRequest<null>(`/v1/account/oauth-grants/${encodeURIComponent(grantId)}`, { method: 'DELETE' });
}

/**
 * 授權請求失敗時信封的 error.data：error 是 OAuth 的錯誤碼，redirectTo 有值時可以把使用者帶回應用
 * （帶著錯誤）。信封是外部資料，形狀不對（沒有 data、欄位不是字串）或不是 http(s) 的網址，都當成沒有導回的地方。
 */
export function redirectTargetOf(error: unknown): string | null {
  if (!isApiError(error) || typeof error.data !== 'object' || error.data === null) return null;
  const { redirectTo } = error.data as { readonly redirectTo?: unknown };
  return typeof redirectTo === 'string' && isNavigableUrl(redirectTo) ? redirectTo : null;
}

/** 檢查授權請求。同一個請求只問一次：不重試、不因為視窗取得焦點重抓、離開頁面就丟掉（沒有要保留的快取）。 */
export function useAuthorizationCheck(request: AuthorizationRequest, requestKey: string) {
  return useQuery({
    queryKey: ['oauth-authorization', requestKey],
    queryFn: ({ signal }) => checkAuthorization(request, signal),
    retry: false,
    staleTime: Infinity,
    gcTime: 0,
    refetchOnWindowFocus: false,
  });
}

export function useApproveAuthorization(request: AuthorizationRequest) {
  return useMutation({
    mutationFn: (scopes: readonly AccessTokenScope[]) => approveAuthorization(request, scopes),
  });
}

export function useDenyAuthorization(request: AuthorizationRequest) {
  return useMutation({ mutationFn: () => denyAuthorization(request) });
}

/** pollMs：每隔多久重讀一次（設定精靈看 Claude Code／Codex 授權了沒）。 */
export function useOAuthGrants(pollMs?: number) {
  return useQuery({
    queryKey: queryKeys.oauthGrants,
    queryFn: ({ signal }) => listOAuthGrants(signal),
    refetchInterval: pollMs,
  });
}

const NOT_FOUND = 404;

/** 撤銷一筆授權；404（別處先撤了）也算成功，兩種都重抓清單，畫面和伺服器對上。 */
export function useRevokeOAuthGrant() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (grantId: string): Promise<{ readonly wasAlreadyGone: boolean }> => {
      try {
        await revokeOAuthGrant(grantId);
        return { wasAlreadyGone: false };
      } catch (error) {
        if (isApiError(error) && error.status === NOT_FOUND) return { wasAlreadyGone: true };
        throw error;
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.oauthGrants }),
  });
}
