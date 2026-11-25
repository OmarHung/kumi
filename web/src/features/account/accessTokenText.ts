import type { AccessToken, AccessTokenScope, Instant } from '../../api/types';
import { formatDate, toCalendarDate } from '../../domain/dates';
import { formatRelativeTime } from '../../domain/time';

export interface ScopeOption {
  readonly scope: AccessTokenScope;
  readonly label: string;
  /** 勾選框旁邊的一句說明：這個範圍能做什麼。 */
  readonly description: string;
}

/** 範圍的名稱（對外的值是合約，internal/HANDOFF-pat-cli.md 第 4 節）與說明；順序就是畫面上的順序。 */
export const SCOPE_OPTIONS: readonly ScopeOption[] = [
  { scope: 'items:read', label: '讀取項目', description: '看專案、項目、活動、我的工作與人員。' },
  { scope: 'items:write', label: '修改項目', description: '新增、修改、刪除項目，改狀態與類型，留言。' },
  { scope: 'blueprint:read', label: '讀取設定', description: '匯出專案的設定（CLI 裡叫 blueprint），預覽與查看變更集。' },
  { scope: 'blueprint:write', label: '修改設定', description: '送出、套用與退回變更集；專案改名、封存與取消封存。' },
  { scope: 'agent:run', label: '本機代理', description: '讓這台電腦的 Claude Code／Codex 接網頁的 AI 助理。' },
];

/** 建立時預設勾選的範圍：本機代理是專門用途（AI 帳號頁的「本機代理」有一鍵建立的），不預設勾。 */
export const DEFAULT_SCOPES: readonly AccessTokenScope[] = ['items:read', 'items:write', 'blueprint:read', 'blueprint:write'];

const SCOPE_LABELS = new Map(SCOPE_OPTIONS.map((option) => [option.scope, option.label]));

/** 本機代理的權杖不能同時改項目（引擎拿得到它），所以這兩個不能在同一把裡；伺服器也擋，這裡先停用，不讓人勾出會被退回的組合。 */
const EXCLUSIVE_SCOPES: Readonly<Partial<Record<AccessTokenScope, AccessTokenScope>>> = {
  'agent:run': 'items:write',
  'items:write': 'agent:run',
};

/** 另一個互斥的範圍已經勾著時，這一項停用的原因與解法（接在說明後面，開頭有空白）：預設就勾著「修改項目」，本機代理一開始是停用的，要告訴人先取消哪一個。沒有衝突回 null。 */
export function scopeConflictNote(scope: AccessTokenScope, selected: readonly AccessTokenScope[]): string | null {
  const other = EXCLUSIVE_SCOPES[scope];
  if (other === undefined || !selected.includes(other)) return null;
  return ` 要勾選「${SCOPE_LABELS.get(scope) ?? scope}」，請先取消「${SCOPE_LABELS.get(other) ?? other}」：這兩個不能放在同一把。`;
}

/** 清單上的範圍：項目與設定的四種都有時寫「全部」（本機代理另外接在後面），其他照順序列出名稱。 */
export function scopesText(scopes: readonly AccessTokenScope[]): string {
  const labels = (list: readonly AccessTokenScope[]) =>
    SCOPE_OPTIONS.filter((option) => list.includes(option.scope)).map((option) => SCOPE_LABELS.get(option.scope) ?? option.scope);
  if (!DEFAULT_SCOPES.every((scope) => scopes.includes(scope))) return labels(scopes).join('、');
  return ['全部', ...labels(scopes.filter((scope) => !DEFAULT_SCOPES.includes(scope)))].join('、');
}

export interface ExpiryChoice {
  readonly value: string;
  readonly label: string;
  readonly days: number;
}

/** 期限的選項；網頁預設 90 天。伺服器不收不過期的權杖，所以沒有「不過期」。 */
export const EXPIRY_CHOICES: readonly ExpiryChoice[] = [
  { value: '30', label: '30 天', days: 30 },
  { value: '90', label: '90 天', days: 90 },
  { value: '365', label: '1 年', days: 365 },
];

export const DEFAULT_EXPIRY = '90';

/** 本機代理（agent:run）的權杖最長幾天：伺服器擋超過的（422 expiresInDays），選單先只給到這裡。 */
export const AGENT_EXPIRY_MAX_DAYS = 90;

const DEFAULT_EXPIRY_DAYS = Number(DEFAULT_EXPIRY);

/** 選項的天數；對不到（不該發生）時用預設的 90 天，不送出伺服器會退回的值。 */
export function expiryDays(value: string): number {
  return EXPIRY_CHOICES.find((choice) => choice.value === value)?.days ?? DEFAULT_EXPIRY_DAYS;
}

/** 勾了本機代理時期限的選項只到 90 天；其他是全部。 */
export function expiryChoicesFor(scopes: readonly AccessTokenScope[]): readonly ExpiryChoice[] {
  return scopes.includes('agent:run') ? EXPIRY_CHOICES.filter((choice) => choice.days <= AGENT_EXPIRY_MAX_DAYS) : EXPIRY_CHOICES;
}

/** 目前選的期限在這些範圍下還能不能選。 */
export function isExpiryAllowed(value: string, scopes: readonly AccessTokenScope[]): boolean {
  return expiryChoicesFor(scopes).some((choice) => choice.value === value);
}

export function isExpired(token: AccessToken, now: number): boolean {
  return token.expiresAt !== null && Date.parse(token.expiresAt) <= now;
}

/** 日期（使用者所在地的日曆日）；解析不了的原樣回傳。 */
export function dayText(at: Instant): string {
  const date = new Date(at);
  return Number.isNaN(date.getTime()) ? at : formatDate(toCalendarDate(date));
}

export function lastUsedText(token: AccessToken, now: number): string {
  return token.lastUsedAt === null ? '還沒用過' : formatRelativeTime(token.lastUsedAt, now);
}

/** 到期的字：不過期（舊的權杖才有）、日期；已過期的另外標（AccessTokenExpiry）。 */
export function expiryText(token: AccessToken): string {
  return token.expiresAt === null ? '不過期' : dayText(token.expiresAt);
}

/** 「撤銷」按鈕的名稱：帶上開頭，同名的兩把才分得出來。 */
export function revokeLabel(token: AccessToken): string {
  return `撤銷「${token.name}」（${prefixText(token)}）`;
}

/** 開頭幾個字加省略號：明文的其餘部分畫面上不會再出現。 */
export function prefixText(token: AccessToken): string {
  return `${token.displayPrefix}…`;
}
