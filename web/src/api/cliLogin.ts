import { useMutation, useQuery } from '@tanstack/react-query';
import { apiRequest } from './http';
import type { AccessTokenScope } from './types';

/** kumi 要的權杖種類：cli 是命令列用的，agent 是本機代理用的（internal/HANDOFF-setup-wizard.md 第 3.3 節）。 */
export type CliLoginTokenKind = 'cli' | 'agent';

/** 網址列帶來的請求（kumi 自己組的）；網頁只檢查連接埠與 state，其餘原樣交給伺服器。 */
export interface CliLoginRequest {
  readonly port: number;
  readonly state: string;
  readonly challenge: string;
  readonly machine: string;
  readonly tokens: readonly string[];
  readonly scopes: readonly string[];
}

export interface CliLoginAccount {
  readonly displayName: string;
  readonly email: string;
  readonly organizationName: string;
}

export interface CliLoginCheck {
  /** 伺服器整理過的電腦名稱（顯示用）。 */
  readonly machineName: string;
  readonly tokens: readonly CliLoginTokenKind[];
  /** 命令列的權杖請求的範圍（預先勾好）。 */
  readonly scopes: readonly AccessTokenScope[];
  /** 本機代理的權杖固定的範圍。 */
  readonly agentScopes: readonly AccessTokenScope[];
  /** 組織允許成員用自己的 AI 帳號；false 時只建命令列的。 */
  readonly agentAllowed: boolean;
  readonly account: CliLoginAccount;
}

export interface CliLoginApproval extends CliLoginRequest {
  readonly grantedScopes: readonly AccessTokenScope[];
  /** 一定要有天數：伺服器不收不過期的（422 expiresInDays）。 */
  readonly expiresInDays: number;
  /** 使用者從終端機抄來的確認碼，只有 6 個數字（不含空白）；網頁自己算不出正確的碼，由伺服器比對。 */
  readonly confirmationCode: string;
}

export interface CliLoginRedirect {
  readonly redirectTo: string;
}

const BASE = '/v1/cli-login';

export function checkCliLogin(request: CliLoginRequest, signal?: AbortSignal): Promise<CliLoginCheck> {
  return apiRequest<CliLoginCheck>(`${BASE}/check`, { method: 'POST', body: request, signal });
}

/** 422 cli_login.request_invalid、cli_login.confirmation_mismatch（細項路徑 confirmationCode）、access_token.expiry_invalid；403 cli_login.agent_not_allowed；409 access_token.limit_reached；429。 */
export function approveCliLogin(approval: CliLoginApproval): Promise<CliLoginRedirect> {
  return apiRequest<CliLoginRedirect>(`${BASE}/approve`, { method: 'POST', body: approval });
}

/** 同一個請求只問一次：不重試、不因為視窗取得焦點重抓、離開頁面就丟掉（和 OAuth 的同意頁一樣）。 */
export function useCliLoginCheck(request: CliLoginRequest | null, requestKey: string) {
  return useQuery({
    queryKey: ['cli-login', requestKey],
    queryFn: ({ signal }) => checkCliLogin(request as CliLoginRequest, signal),
    enabled: request !== null,
    retry: false,
    staleTime: Infinity,
    gcTime: 0,
    refetchOnWindowFocus: false,
  });
}

export function useApproveCliLogin() {
  return useMutation({ mutationFn: approveCliLogin });
}
