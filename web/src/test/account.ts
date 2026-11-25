import { http } from 'msw';
import type { AccessToken, AccessTokenScope } from '../api/types';
import { failure, ok } from './api';
import { MAX_PASSWORD, MIN_PASSWORD } from './people';
import { server } from './server';

export interface AccountBackend {
  /** PUT /v1/account/password 收到的內文（照收到的順序；被擋下來的也算）。 */
  readonly passwordChanges: unknown[];
  /** DELETE /v1/account/other-sessions 收到幾次。 */
  readonly signOutOthers: () => number;
}

interface AccountOptions {
  /** 目前的密碼：送來的 currentPassword 不是它就是 422 account.password_incorrect。 */
  readonly currentPassword: string;
  /** 別的裝置上還有幾個登入；改密碼或登出其他裝置之後變成 0。 */
  readonly otherSessions?: number;
}

// 和後端一樣（src/Modules/PmTool.Identity/AccountService.cs、PersonInput.cs）。
const PASSWORD_INCORRECT = '目前的密碼不正確。';

interface Issue {
  readonly code: string;
  readonly path: string;
  readonly message: string;
}

/** 新密碼的長度；細項路徑是 newPassword。 */
function newPasswordIssue(password: string): Issue | null {
  if (password.length < MIN_PASSWORD) {
    return { code: 'person.password_too_short', path: 'newPassword', message: `密碼至少要 ${String(MIN_PASSWORD)} 個字元。` };
  }
  return password.length > MAX_PASSWORD
    ? { code: 'person.password_too_long', path: 'newPassword', message: `密碼不能超過 ${String(MAX_PASSWORD)} 個字元。` }
    : null;
}

interface PasswordBody {
  readonly currentPassword?: unknown;
  readonly newPassword?: unknown;
}

function stringOf(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * 假的「我的帳號」端點，照合約（internal/HANDOFF-security-local-ops.md 第 4 節）：
 * PUT /v1/account/password 檢查目前的密碼（422 account.password_incorrect，細項路徑 currentPassword）與新密碼的長度
 * （422，細項路徑 newPassword），兩個都有問題時一次回兩個細項；成功時回作廢了幾個其他的登入；
 * DELETE /v1/account/other-sessions 也是。
 * 改過之後目前的密碼就是新的那一個。429 由個別的測試用 server.use 蓋掉。
 */
export function serveAccount({ currentPassword, otherSessions = 0 }: AccountOptions): AccountBackend {
  let password = currentPassword;
  let others = otherSessions;
  let signOuts = 0;
  const passwordChanges: unknown[] = [];

  function signOutOtherSessions() {
    const signedOut = others;
    others = 0;
    return ok({ signedOutSessions: signedOut });
  }

  server.use(
    http.put('/v1/account/password', async ({ request }) => {
      const body = (await request.json()) as PasswordBody;
      passwordChanges.push(body);
      const next = stringOf(body.newPassword);
      // 後端把兩個欄位的問題一次列出來（網頁一起標），第一個的代碼與訊息是整個失敗的。
      const issues = [
        stringOf(body.currentPassword) === password
          ? null
          : { code: 'account.password_incorrect', path: 'currentPassword', message: PASSWORD_INCORRECT },
        newPasswordIssue(next),
      ].filter((issue): issue is Issue => issue !== null);
      const [first] = issues;
      if (first !== undefined) return failure(422, first.code, first.message, issues);
      password = next;
      return signOutOtherSessions();
    }),
    http.delete('/v1/account/other-sessions', () => {
      signOuts += 1;
      return signOutOtherSessions();
    }),
  );
  return { passwordChanges, signOutOthers: () => signOuts };
}

/** 和登入共用依來源 IP 的額度用完了（429 request.rate_limited）。 */
export function rateLimitPasswordChange(): void {
  server.use(http.put('/v1/account/password', () => failure(429, 'request.rate_limited', 'Too many requests.')));
}

// ---- 存取權杖（internal/HANDOFF-pat-cli.md 第 4 節） ----

const ALL_SCOPES: readonly AccessTokenScope[] = ['items:read', 'items:write', 'blueprint:read', 'blueprint:write', 'agent:run'];
const DEFAULT_SCOPES: readonly AccessTokenScope[] = ['items:read', 'items:write', 'blueprint:read', 'blueprint:write'];
// 和後端一樣（AccessTokenRules）。
export const TOKEN_NAME_MAX = 100;
export const TOKEN_EXPIRY_MAX_DAYS = 365;
export const TOKEN_ACTIVE_MAX = 20;
const NOT_FOUND = '找不到這把存取權杖。';
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** 清單裡的一把權杖（假資料）；沒給的欄位是「剛建立、全部範圍、不過期」。 */
export function tokenOf(overrides: Partial<AccessToken> = {}): AccessToken {
  return {
    tokenId: '11111111-1111-4111-8111-111111111111',
    name: '範例權杖',
    displayPrefix: 'pmt_Ab12',
    scopes: DEFAULT_SCOPES,
    createdAt: '2026-11-01T02:00:00Z',
    lastUsedAt: null,
    expiresAt: null,
    machineName: null,
    ...overrides,
  };
}

export interface AccessTokenBackend {
  /** POST /v1/account/tokens 收到的內文（被擋下來的也算）。 */
  readonly creates: unknown[];
  /** DELETE 收到的 tokenId。 */
  readonly revokes: string[];
  /** 目前清單裡的權杖（新的在前）。 */
  readonly tokens: () => readonly AccessToken[];
  /** 最後一次建立回給網頁的明文（測試拿來確認畫面上找得到、關上之後找不到）。 */
  readonly lastSecret: () => string | null;
}

interface CreateBody {
  readonly name?: unknown;
  readonly scopes?: unknown;
  readonly expiresInDays?: unknown;
}

function tokenIssue(body: CreateBody): Issue | null {
  const name = typeof body.name === 'string' ? body.name.trim() : '';
  const days = body.expiresInDays;
  if (name === '') return { code: 'access_token.name_required', path: 'name', message: '請輸入權杖的名稱。' };
  if (name.length > TOKEN_NAME_MAX) {
    return { code: 'access_token.name_too_long', path: 'name', message: `名稱不能超過 ${String(TOKEN_NAME_MAX)} 個字元。` };
  }
  if (!Array.isArray(body.scopes) || body.scopes.length === 0) {
    return { code: 'access_token.scopes_required', path: 'scopes', message: '至少要選一個範圍。' };
  }
  if (body.scopes.includes('agent:run') && body.scopes.includes('items:write')) {
    return {
      code: 'access_token.scopes_conflict',
      path: 'scopes',
      message: 'agent:run 是給本機代理的權杖用的，不能和 items:write 放在同一把；請分成兩把。',
    };
  }
  const isBadDays = days !== null && (typeof days !== 'number' || days < 1 || days > TOKEN_EXPIRY_MAX_DAYS);
  return isBadDays
    ? { code: 'access_token.expiry_invalid', path: 'expiresInDays', message: `期限要在 1 到 ${String(TOKEN_EXPIRY_MAX_DAYS)} 天之間，或選不過期。` }
    : null;
}

/**
 * 假的存取權杖端點，照合約：GET 回清單（新的在前）；POST 檢查名稱、範圍、期限（422，細項路徑 name、scopes、expiresInDays；
 * 不認得的範圍名稱是 400）與上限（還能用的最多 20 把，409 access_token.limit_reached），成功回 201 `{ token, secret }`；
 * DELETE 回撤銷的那一把（不是 GUID、不存在、已撤銷都是 404 access_token.not_found），撤銷之後不在清單裡。
 */
export function serveAccessTokens(initial: readonly AccessToken[] = []): AccessTokenBackend {
  let tokens: readonly AccessToken[] = initial;
  let secret: string | null = null;
  let counter = 0;
  const creates: unknown[] = [];
  const revokes: string[] = [];

  server.use(
    http.get('/v1/account/tokens', () => ok(tokens)),
    http.post('/v1/account/tokens', async ({ request }) => {
      const body = (await request.json()) as CreateBody;
      creates.push(body);
      const issue = tokenIssue(body);
      if (issue !== null) return failure(422, issue.code, issue.message, [issue]);
      const scopes = body.scopes as readonly string[];
      if (scopes.some((scope) => !ALL_SCOPES.includes(scope as AccessTokenScope))) {
        return failure(400, 'request.invalid', '不認得的範圍。');
      }
      const now = Date.now();
      const active = tokens.filter((token) => token.expiresAt === null || Date.parse(token.expiresAt) > now);
      if (active.length >= TOKEN_ACTIVE_MAX) {
        return failure(409, 'access_token.limit_reached', `最多只能同時有 ${String(TOKEN_ACTIVE_MAX)} 把還能用的存取權杖，請先撤銷不用的。`);
      }
      counter += 1;
      secret = `pmt_Zz${String(counter).padStart(2, '0')}example-secret-value-never-shown-again`;
      const days = body.expiresInDays as number | null;
      const token = tokenOf({
        tokenId: `00000000-0000-4000-8000-${String(counter).padStart(12, '0')}`,
        name: (body.name as string).trim(),
        displayPrefix: secret.slice(0, 8),
        scopes: scopes as readonly AccessTokenScope[],
        createdAt: new Date(now).toISOString(),
        expiresAt: days === null ? null : new Date(now + days * 86_400_000).toISOString(),
      });
      tokens = [token, ...tokens];
      return ok({ token, secret }, 201);
    }),
    http.delete('/v1/account/tokens/:tokenId', ({ params }) => {
      const tokenId = String(params.tokenId);
      revokes.push(tokenId);
      const found = GUID.test(tokenId) ? tokens.find((token) => token.tokenId === tokenId) : undefined;
      if (found === undefined) return failure(404, 'access_token.not_found', NOT_FOUND);
      tokens = tokens.filter((token) => token !== found);
      return ok(found);
    }),
  );
  return { creates, revokes, tokens: () => tokens, lastSecret: () => secret };
}
