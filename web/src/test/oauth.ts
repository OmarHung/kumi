import { http, HttpResponse } from 'msw';
import type { AuthorizationCheck, OAuthGrant } from '../api/oauth';
import type { AccessTokenScope } from '../api/types';
import { failure, ok } from './api';
import { server } from './server';

const ALL_SCOPES: readonly AccessTokenScope[] = ['items:read', 'items:write', 'blueprint:read', 'blueprint:write'];

/** 同意頁檢查的結果（假資料）；沒給的欄位是「Claude Code、有驗證過的網域、導回本機、全部範圍」。 */
export function authorizationCheckOf(overrides: Partial<AuthorizationCheck> = {}): AuthorizationCheck {
  return {
    clientId: 'https://client.example/metadata.json',
    clientName: 'Claude Code',
    clientHost: 'client.example',
    redirectHost: '127.0.0.1',
    redirectIsLoopback: true,
    requestedScopes: ALL_SCOPES,
    account: { displayName: '範例使用者', email: 'owner@example.com', organizationName: '範例組織' },
    ...overrides,
  };
}

/** 授權請求失敗：422 oauth.request_invalid，error 與 redirectTo 在信封的 error.data（internal/HANDOFF-mcp.md 4.6）。 */
export function authorizationFailure(message: string, redirectTo: string | null, error = 'invalid_request') {
  return HttpResponse.json(
    {
      success: false,
      data: null,
      error: { code: 'oauth.request_invalid', message, details: [], data: { error, redirectTo } },
    },
    { status: 422 },
  );
}

export interface OAuthBackend {
  /** check／approve／deny 收到的內文（照收到的順序）。 */
  readonly checks: unknown[];
  readonly approvals: { readonly scopes?: readonly string[] }[];
  readonly denials: unknown[];
}

interface OAuthOptions {
  readonly check?: AuthorizationCheck;
  readonly approveRedirect?: string;
  readonly denyRedirect?: string;
}

/** 假的授權端點：check 回 check；approve 與 deny 回導回的網址。要失敗的測試用 server.use 蓋掉。 */
export function serveAuthorization({
  check = authorizationCheckOf(),
  approveRedirect = 'http://127.0.0.1:4567/callback?code=abc&state=xyz',
  denyRedirect = 'http://127.0.0.1:4567/callback?error=access_denied&state=xyz',
}: OAuthOptions = {}): OAuthBackend {
  const backend: OAuthBackend = { checks: [], approvals: [], denials: [] };
  server.use(
    http.post('/v1/oauth/authorization/check', async ({ request }) => {
      backend.checks.push(await request.json());
      return ok(check);
    }),
    http.post('/v1/oauth/authorization/approve', async ({ request }) => {
      backend.approvals.push((await request.json()) as { scopes?: readonly string[] });
      return ok({ redirectTo: approveRedirect });
    }),
    http.post('/v1/oauth/authorization/deny', async ({ request }) => {
      backend.denials.push(await request.json());
      return ok({ redirectTo: denyRedirect });
    }),
  );
  return backend;
}

export function grantOf(overrides: Partial<OAuthGrant> = {}): OAuthGrant {
  return {
    grantId: '44444444-4444-4444-8444-444444444444',
    clientName: 'Claude Code',
    clientHost: 'client.example',
    scopes: ALL_SCOPES,
    createdAt: '2026-11-20T02:00:00Z',
    lastUsedAt: null,
    ...overrides,
  };
}

export interface OAuthGrantBackend {
  readonly revokes: string[];
  readonly grants: () => readonly OAuthGrant[];
}

/** 假的「已授權的 AI 代理」端點：GET 回清單；DELETE 撤銷（不存在是 404 oauth.grant_not_found）。 */
export function serveOAuthGrants(initial: readonly OAuthGrant[] = []): OAuthGrantBackend {
  let grants = initial;
  const revokes: string[] = [];
  server.use(
    http.get('/v1/account/oauth-grants', () => ok(grants)),
    http.delete('/v1/account/oauth-grants/:grantId', ({ params }) => {
      const grantId = String(params.grantId);
      revokes.push(grantId);
      if (!grants.some((grant) => grant.grantId === grantId)) {
        return failure(404, 'oauth.grant_not_found', '找不到這筆授權。');
      }
      grants = grants.filter((grant) => grant.grantId !== grantId);
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { revokes, grants: () => grants };
}
