import type { AuthorizationRequest } from '../../api/oauth';
import type { AccessTokenScope } from '../../api/types';

/** 同意頁上範圍的說法（和個人存取權杖的範圍同一組，但這裡是對「要授權給誰」的人解釋）；順序就是畫面上的順序。 */
export const CONSENT_SCOPES: readonly { readonly scope: AccessTokenScope; readonly label: string }[] = [
  { scope: 'items:read', label: '看專案與項目' },
  { scope: 'items:write', label: '建立與修改項目、留言' },
  { scope: 'blueprint:read', label: '看專案的設定與變更集' },
  { scope: 'blueprint:write', label: '提出變更集（還是要有人核准）' },
];

const SCOPE_LABELS = new Map(CONSENT_SCOPES.map((option) => [option.scope, option.label]));

/** 認得的範圍用上面的說法；不認得的（之後新增的）照原名，不讓使用者看不到有這一項。 */
export function consentScopeLabel(scope: AccessTokenScope): string {
  return SCOPE_LABELS.get(scope) ?? scope;
}

/** 依畫面的順序排；不認得的接在後面。 */
export function orderedScopes(scopes: readonly AccessTokenScope[]): readonly AccessTokenScope[] {
  const known = CONSENT_SCOPES.map((option) => option.scope).filter((scope) => scopes.includes(scope));
  return [...known, ...scopes.filter((scope) => !SCOPE_LABELS.has(scope))];
}

const READ_SCOPES: readonly AccessTokenScope[] = ['items:read', 'blueprint:read'];

/** 預設勾的範圍：有驗證的應用全勾；沒驗證的只勾讀取的（有請求的才算），寫入的要使用者自己勾。 */
export function defaultScopes(requested: readonly AccessTokenScope[], isVerified: boolean): readonly AccessTokenScope[] {
  return isVerified ? requested : requested.filter((scope) => READ_SCOPES.includes(scope));
}

// 網址列的 OAuth 參數名稱（snake_case）對應到 API 的欄位。
const PARAMETER_FIELDS: readonly (readonly [string, keyof AuthorizationRequest])[] = [
  ['response_type', 'responseType'],
  ['client_id', 'clientId'],
  ['redirect_uri', 'redirectUri'],
  ['scope', 'scope'],
  ['state', 'state'],
  ['code_challenge', 'codeChallenge'],
  ['code_challenge_method', 'codeChallengeMethod'],
  ['resource', 'resource'],
];

/** 網址列的參數原樣交給伺服器檢查（網頁不判斷對不對）；沒有的欄位不送。 */
export function requestFromSearch(params: URLSearchParams): AuthorizationRequest {
  const fields = PARAMETER_FIELDS.flatMap(([name, field]) => {
    const value = params.get(name);
    return value === null ? [] : [[field, value] as const];
  });
  return Object.fromEntries(fields);
}
