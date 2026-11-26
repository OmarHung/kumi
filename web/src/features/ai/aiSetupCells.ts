import type { SessionUser } from '../../api/types';

/**
 * AI 的四種接法：看兩件事就分得開——誰能用（全組織共用／只有自己）與用什麼登入（API 金鑰／Claude Code、Codex）。
 * 三頁（AI 設定、主機 AI 代理、AI 帳號）最上面共用這一份對照，標出目前在哪一格。
 */
export type AiSetupKind = 'organization_keys' | 'host_agent' | 'personal_keys' | 'local_agent';

/** 哪一頁在用這張對照：那一頁的格子標成目前所在。 */
export type AiSetupPage = 'organization' | 'host' | 'personal';

type Access = 'everyone' | 'admin' | 'owner';

export interface AiSetupCell {
  readonly kind: AiSetupKind;
  readonly label: string;
  /** 一句話：費用算誰、登入的限制。 */
  readonly detail: string;
  readonly href: string;
  readonly page: AiSetupPage;
  readonly access: Access;
}

export const AI_SETUP_COLUMNS = ['API 金鑰', 'Claude Code／Codex'] as const;

export interface AiSetupRow {
  readonly label: string;
  readonly cells: readonly [AiSetupCell, AiSetupCell];
}

export const AI_SETUP_ROWS: readonly AiSetupRow[] = [
  {
    label: '全組織共用',
    cells: [
      { kind: 'organization_keys', label: '組織的 API 金鑰', detail: '按用量計費，算在組織的供應商帳號', href: '/ai-settings', page: 'organization', access: 'admin' },
      { kind: 'host_agent', label: '主機 AI 代理', detail: '裝在 Kumi 主機上；只收 API 計費的登入，不能用訂閱', href: '/ai-settings/host', page: 'host', access: 'owner' },
    ],
  },
  {
    label: '只有自己',
    cells: [
      { kind: 'personal_keys', label: '我的 API 金鑰', detail: '按用量計費，算在自己的供應商帳號；要組織允許', href: '/account/ai#ai-keys', page: 'personal', access: 'everyone' },
      { kind: 'local_agent', label: '本機代理', detail: '自己電腦上的，可以用自己的訂閱；要組織允許', href: '/account/ai#local-agent', page: 'personal', access: 'everyone' },
    ],
  },
];

const MANAGED_BY: Readonly<Record<Exclude<Access, 'everyone'>, string>> = {
  admin: '由組織管理員設定',
  owner: '由組織擁有者設定',
};

/** 看得到那一頁就給連結；看不到的寫由誰設定（打了網址也只會說沒有權限）。 */
export function canOpenAiSetup(cell: AiSetupCell, user: SessionUser): boolean {
  if (cell.access === 'everyone') return true;
  if (cell.access === 'owner') return user.role === 'owner';
  return user.role === 'owner' || user.role === 'admin';
}

export function managedByText(cell: AiSetupCell): string | null {
  return cell.access === 'everyone' ? null : MANAGED_BY[cell.access];
}
