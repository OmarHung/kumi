import type { AuditEvent, AuditOutcome } from '../../api/audit';
import { COMMAND_NAME } from '../../config/product';
import { ORGANIZATION_ROLE_LABELS } from '../../domain/labels';
import { scopeText } from '../inbox/accountNoticeText';

export const AUDIT_OUTCOME_LABELS: Readonly<Record<AuditOutcome, string>> = {
  succeeded: '成功',
  failed: '失敗',
};

/** 兌換失敗的原因（details.reason）；不認得的照原字串。 */
const REDEEM_FAILURE_REASONS: Readonly<Record<string, string>> = {
  origin_mismatch: '來源和核准時不同',
  verifier_mismatch: '驗證碼不符',
};

function roleLabel(role: string | undefined): string | null {
  if (role === undefined) return null;
  return (ORGANIZATION_ROLE_LABELS as Readonly<Record<string, string>>)[role] ?? role;
}

/** 細項裡有值的才寫；括號接在動作後面。沒有細項時回空字串。 */
function parenthesized(parts: readonly (string | null)[]): string {
  const present = parts.filter((part): part is string => part !== null && part !== '');
  return present.length === 0 ? '' : `（${present.join('、')}）`;
}

function countPart(value: string | undefined, label: (count: number) => string): string | null {
  const count = Number(value);
  return value !== undefined && Number.isInteger(count) && count > 0 ? label(count) : null;
}

/** 細項裡的範圍是空白隔開的 `items:read items:write`；說法和「存取權杖」頁、收件匣同一份，不認得的照原字串。 */
function scopesLabel(scopes: string | undefined): string | null {
  const list = (scopes ?? '').split(/\s+/).filter((scope) => scope !== '');
  return list.length === 0 ? null : scopeText(list);
}

function quoted(name: string | undefined): string {
  return name === undefined || name === '' ? '' : `「${name}」`;
}

type Describe = (event: AuditEvent) => string;

const sensitiveAction = (outcomeText: Readonly<Record<AuditOutcome, string>>): Describe => (event) => outcomeText[event.outcome];

/** 動作代碼（對外合約）對成中文說明；細項（details）有的才補。 */
const DESCRIBERS: Readonly<Record<string, Describe>> = {
  sign_in: sensitiveAction({ succeeded: '登入成功', failed: '登入失敗' }),
  password_changed: ({ details }) =>
    `改密碼${parenthesized([
      countPart(details.signedOutSessions, (count) => `登出其他裝置 ${String(count)} 個`),
      countPart(details.revokedAccessTokens, (count) => `撤銷存取權杖 ${String(count)} 把`),
    ])}`,
  other_sessions_signed_out: () => '登出其他裝置',
  password_reset: () => '重設密碼',
  account_disabled: () => '停用帳號',
  account_enabled: () => '啟用帳號',
  role_changed: ({ details }) => {
    const [from, to] = [roleLabel(details.from), roleLabel(details.to)];
    return from === null || to === null ? '改角色' : `改角色（從 ${from} 到 ${to}）`;
  },
  person_created: ({ details }) => `新增人員${parenthesized([roleLabel(details.role)])}`,
  access_token_created: ({ details }) =>
    `建立存取權杖${quoted(details.tokenName)}${parenthesized([scopesLabel(details.scopes)])}`,
  access_token_revoked: ({ details }) => `撤銷存取權杖${quoted(details.tokenName)}`,
  cli_login_approved: ({ details }) => `核准 ${COMMAND_NAME} 登入${parenthesized([details.machine ?? null])}`,
  cli_login_redeemed: ({ outcome, details }) => {
    const reason = details.reason === undefined ? null : (REDEEM_FAILURE_REASONS[details.reason] ?? details.reason);
    return outcome === 'succeeded' ? `兌換 ${COMMAND_NAME} 登入成功` : `兌換 ${COMMAND_NAME} 登入失敗${parenthesized([reason])}`;
  },
  cli_login_code_reused: ({ details }) =>
    `登入碼被重複使用${parenthesized([countPart(details.revokedTokens, (count) => `撤銷存取權杖 ${String(count)} 把`)])}`,
  oauth_authorized: ({ details }) => `授權 AI 用戶端${quoted(details.client)}${parenthesized([details.clientHost ?? null])}`,
  oauth_grant_revoked: ({ details }) => `撤銷 AI 用戶端授權${quoted(details.client)}`,
};

/** 一筆稽核紀錄的中文說明；不認得的動作（之後新增的）照原字串，不讓人看不到有這件事。 */
export function auditActionText(event: AuditEvent): string {
  return DESCRIBERS[event.action]?.(event) ?? event.action;
}
