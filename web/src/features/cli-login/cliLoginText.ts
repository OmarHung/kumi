import type { CliLoginRequest } from '../../api/cliLogin';
import type { AccessTokenScope } from '../../api/types';
import { COMMAND_NAME } from '../../config/product';
import { DEFAULT_EXPIRY, EXPIRY_CHOICES, SCOPE_OPTIONS } from '../account/accessTokenText';

// 只收系統分配給暫時連線的範圍（32768 起）：低一點的埠常有別的服務佔著，不該是 kumi 開的暫時迴路。
export const MIN_PORT = 32768;
export const MAX_PORT = 65535;
// state 是 kumi 產生的 base64url；要放進導回的網址，所以只收這些字元（不必靠編碼才安全）。
const STATE_PATTERN = /^[A-Za-z0-9_-]{1,256}$/;
const PORT_PATTERN = /^\d{1,5}$/;
const LOOPBACK_HOST = 'http://127.0.0.1';
const CALLBACK_PATH = '/kumi-login';

export type CliLoginParse = { readonly request: CliLoginRequest } | { readonly problem: string };

/**
 * 網址列的參數（kumi 自己組的）轉成 API 的請求。連接埠與 state 不對就直接說有問題、不呼叫伺服器：
 * 它們決定使用者按「允許」之後瀏覽器要被導到哪裡，不能把格式不對的東西帶進導回的網址。其餘的原樣交給伺服器檢查。
 */
export function requestFromSearch(params: URLSearchParams): CliLoginParse {
  const portText = params.get('port') ?? '';
  const port = PORT_PATTERN.test(portText) ? Number(portText) : Number.NaN;
  if (!(port >= MIN_PORT && port <= MAX_PORT)) return { problem: '連接埠不對。' };
  const state = params.get('state') ?? '';
  if (!STATE_PATTERN.test(state)) return { problem: '缺少或帶了不對的識別碼。' };
  const challenge = params.get('challenge') ?? '';
  if (challenge === '') return { problem: '缺少驗證用的資料。' };
  return {
    request: {
      port,
      state,
      challenge,
      machine: params.get('machine') ?? '',
      tokens: (params.get('tokens') ?? '').split(',').map((kind) => kind.trim()).filter((kind) => kind !== ''),
      scopes: (params.get('scope') ?? '').split(/\s+/).filter((scope) => scope !== ''),
    },
  };
}

/** 使用者按「允許」之後伺服器給的導回網址只收這個前綴：碼只會送回發起登入的那台電腦（連接埠要和請求的一樣）。 */
export function isExpectedRedirect(redirectTo: string, port: number): boolean {
  return redirectTo.startsWith(`${LOOPBACK_HOST}:${String(port)}${CALLBACK_PATH}?`);
}

/** 拒絕不呼叫伺服器：網頁自己導回 kumi 的迴路，kumi 看到 access_denied 就結束。 */
export function denyRedirectUrl(port: number, state: string): string {
  return `${LOOPBACK_HOST}:${String(port)}${CALLBACK_PATH}?error=access_denied&state=${encodeURIComponent(state)}`;
}

/** 命令列的權杖能選的範圍（不含本機代理：那一把由「本機代理」的設定決定）。 */
export const CLI_SCOPE_OPTIONS = SCOPE_OPTIONS.filter((option) => option.scope !== 'agent:run').map((option) => ({
  scope: option.scope,
  label: option.label,
  hint: option.description,
}));

/** 這一頁對 agent:run 的說法：標題已經叫「本機代理」，底下再列一項同名的看不出它能做什麼。 */
const AGENT_SCOPE_LABEL = '接網頁的 AI 助理（領工作、回報進度）';

const SCOPE_LABELS = new Map(
  SCOPE_OPTIONS.map((option) => [option.scope, option.scope === 'agent:run' ? AGENT_SCOPE_LABEL : option.label]),
);

/** 不認得的範圍（之後新增的）照原名，不讓使用者看不到有這一項。 */
export function scopeLabel(scope: AccessTokenScope): string {
  return SCOPE_LABELS.get(scope) ?? scope;
}

/** 固定的範圍照畫面上一貫的順序排（和存取權杖一致）；不認得的接在最後，維持伺服器給的相對順序。 */
export function orderedScopes(scopes: readonly AccessTokenScope[]): readonly AccessTokenScope[] {
  const rank = (scope: AccessTokenScope) => {
    const index = SCOPE_OPTIONS.findIndex((option) => option.scope === scope);
    return index === -1 ? SCOPE_OPTIONS.length : index;
  };
  return [...scopes].sort((a, b) => rank(a) - rank(b));
}

const READ_SCOPES: readonly AccessTokenScope[] = ['items:read', 'blueprint:read'];

/** 預先勾好的：請求的範圍裡能給命令列用的讀取範圍；寫入的列出來，要使用者自己勾。 */
export function preselectedScopes(requested: readonly AccessTokenScope[]): readonly AccessTokenScope[] {
  return CLI_SCOPE_OPTIONS.map((option) => option.scope).filter((scope) => READ_SCOPES.includes(scope) && requested.includes(scope));
}

const DEFAULT_EXPIRY_DAYS = Number(DEFAULT_EXPIRY);

/** 這一頁的期限一定要有天數，不給「不過期」。 */
export const CLI_EXPIRY_CHOICES = EXPIRY_CHOICES.filter((choice) => choice.days !== null);

/** 選項的天數；對不到（不該發生）時用預設的 90 天，不送 null 讓伺服器退回。 */
export function cliExpiryDays(value: string): number {
  return CLI_EXPIRY_CHOICES.find((choice) => choice.value === value)?.days ?? DEFAULT_EXPIRY_DAYS;
}

/** 本機代理的權杖由伺服器固定發 90 天。 */
export const AGENT_EXPIRY_DAYS = 90;
/** 只要本機代理時沒有期限的選單可以調，直接說固定 90 天。 */
export const AGENT_ONLY_EXPIRY_NOTE = `本機代理的權杖固定 ${String(AGENT_EXPIRY_DAYS)} 天。`;
/** 同時要命令列時，上面有期限的選單：說明它不管本機代理。 */
export const AGENT_EXPIRY_NOTE = '本機代理的權杖固定 90 天，不受上面的期限影響。';

/** 讓使用者看得到核准之後瀏覽器會被導到哪裡。 */
export function redirectNote(port: number): string {
  return `按下允許之後，會把一次性的登入碼送回這台電腦的 127.0.0.1:${String(port)}。`;
}

/** 命令列有要、本機代理不給：照樣可以允許，只是不建那一把。 */
export const AGENT_SKIPPED_NOTE = '組織沒有開放成員使用自己的 AI 帳號，這次不會建立本機代理的權杖。';
/** 只要本機代理又不給：沒有東西可建，「允許」停用。 */
export const AGENT_BLOCKED_NOTE = '組織沒有開放成員使用自己的 AI 帳號，沒辦法建立本機代理的權杖。';
export const WARNING_NOTE = `只有在你剛剛自己在終端機執行 ${COMMAND_NAME} 時才允許。`;
export const APPROVED_NOTE = '已允許，請回到終端機。';
export const DENIED_NOTE = '已拒絕，請回到終端機。';
export const BAD_REDIRECT_NOTE = '伺服器給的導回網址不對，沒有導回終端機。請回到終端機重新執行。';
