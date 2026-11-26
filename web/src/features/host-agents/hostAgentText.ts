import type { LocalEngineKind } from '../../api/aiTypes';
import type { HostAgent, HostAgentState, HostAgentWarning, HostBilling, HostEngineReadiness, HostSource, HostSourceStatus, HostUsageTotals } from '../../api/hostAgentTypes';
import { COMMAND_NAME } from '../../config/product';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { OPTION_LABELS } from '../ai/aiLabels';

/** 主機上的兩個引擎，畫面上固定這個順序。 */
export const HOST_ENGINES: readonly LocalEngineKind[] = ['claude_code', 'codex'];

/** 指令裡的引擎名稱（kumi host login claude-code）。 */
const ENGINE_COMMAND_NAMES: Readonly<Record<LocalEngineKind, string>> = { claude_code: 'claude-code', codex: 'codex' };

export const engineLabel = (engine: LocalEngineKind): string => OPTION_LABELS[engine];

/** 在主機上執行的指令（docker compose 的 ai-worker 服務）。 */
export const PAIR_COMMAND = `docker compose exec -it ai-worker ${COMMAND_NAME} host pair`;

export function loginCommand(engine: LocalEngineKind): string {
  return `docker compose exec -it ai-worker ${COMMAND_NAME} host login ${ENGINE_COMMAND_NAMES[engine]}`;
}

/** 登入的說明：只能用計費給組織的帳號，訂閱帳號依條款不能共用。 */
export function loginRule(engine: LocalEngineKind): string {
  const account = engine === 'claude_code' ? 'Anthropic Console（API 用量計費）' : 'OpenAI API 金鑰';
  const subscription = engine === 'claude_code' ? 'Claude' : 'ChatGPT';
  return `只能用 ${account}登入。${subscription} 的訂閱帳號依條款不能給全組織共用，登入了會被拒絕並登出。`;
}

export const HOST_AGENTS_NOTE =
  '全組織共用。把 Claude Code／Codex 裝在 Kumi 主機的 ai-worker 上，成員不必自己登入或裝軟體。只能用 API 用量計費的帳號登入，費用算在那個帳號；Claude、ChatGPT 的訂閱依條款不能共用。';

export const SOURCES_NOTE = '每個來源是一台主機上的一個引擎：成員在 AI 助理選得到哪些模型、用量上限都在這裡設。要打開「開放給成員使用」才選得到。';

const AGENT_STATE_TEXT: Readonly<Record<HostAgentState, string>> = {
  awaiting_pairing: '等待配對',
  active: '已配對',
  disabled: '已停用',
};

/** 代理的狀態（文字，不靠顏色）：等待配對／在線／離線（最後在 N 分鐘前）／已停用。now 由呼叫端給（最近一次讀到的時間）。 */
export function agentStatusText(agent: HostAgent, now: number): string {
  if (agent.state === 'awaiting_pairing' || agent.state === 'disabled') return AGENT_STATE_TEXT[agent.state];
  if (agent.online) return '在線';
  return agent.lastSeenAt === null ? '離線' : `離線（最後在 ${formatRelativeTime(agent.lastSeenAt, now)}）`;
}

export const READINESS_TEXT: Readonly<Record<HostEngineReadiness, string>> = {
  ready: '可以接工作',
  not_installed: '沒裝',
  version_unsupported: '版本不支援',
  not_logged_in: '需要登入',
  not_authorized: '需要擁有者重新授權',
  billing_not_shareable: '訂閱帳號不能共用',
};

export const BILLING_TEXT: Readonly<Record<HostBilling, string>> = {
  api: 'API 用量計費',
  subscription: '訂閱',
  unknown: '計費方式不明',
};

const SOURCE_STATUS_TEXT: Readonly<Record<HostSourceStatus, string>> = {
  ready: '可以使用',
  disabled: '已停用',
  agent_unavailable: '主機代理不能用',
  agent_offline: '主機離線',
  engine_unavailable: '引擎不能用',
  quota_reached: '已用到上限',
};

/** 來源的狀態；伺服器有說明時接在後面。 */
export function sourceStatusText(source: HostSource): string {
  const base = SOURCE_STATUS_TEXT[source.status];
  return source.statusMessage === null || source.statusMessage === '' ? base : `${base}：${source.statusMessage}`;
}

const NUMBER = new Intl.NumberFormat('en-US');
export const formatCount = (value: number): string => NUMBER.format(value);

/** 「12 則 · 輸入 1,200 · 輸出 300 token」。 */
export function usageSummary(totals: HostUsageTotals): string {
  return `${formatCount(totals.requests)} 則 · 輸入 ${formatCount(totals.inputTokens)} · 輸出 ${formatCount(totals.outputTokens)} token`;
}

/** 「12 則（完成 10、失敗 1、中斷 1）」：用量表的一列。 */
export function outcomeSummary(totals: HostUsageTotals): string {
  return `${formatCount(totals.requests)} 則（完成 ${formatCount(totals.completed)}、失敗 ${formatCount(totals.failed)}、中斷 ${formatCount(totals.stopped)}）`;
}

/** 擁有者設的上限能設多大（和 AiRules 同一份數字）。 */
export const MAX_REQUEST_LIMIT = 1_000_000;
export const MAX_TOKEN_LIMIT = 100_000_000_000;

/**
 * 機器憑證的到期說明。有警告時不寫（警告已經說了憑證的狀況，再寫日期會互相矛盾）；
 * 日期過了寫「已於 … 過期」；「會自動換新」只在啟用、在線的代理才承諾（worker 在線時才會換）。
 */
export function credentialExpiryText(agent: HostAgent, now: number): string | null {
  if (agent.credentialExpiresAt === null) return null;
  if (agent.warning !== null) return null;
  const when = formatInstant(agent.credentialExpiresAt);
  if (Date.parse(agent.credentialExpiresAt) <= now) return `機器憑證已於 ${when} 過期`;
  const renews = agent.state === 'active' && agent.online;
  return renews ? `機器憑證 ${when} 到期，會在到期前自動換新` : `機器憑證 ${when} 到期`;
}

/** 要擁有者處理的狀況：一句話說發生什麼事、要做什麼（三種都是重新配對）。 */
export function warningText(warning: HostAgentWarning): string {
  switch (warning.kind) {
    case 'other_machine': {
      const machine = warning.machineName === null || warning.machineName === '' ? '另一台電腦' : `另一台電腦（${warning.machineName}）`;
      return `有${machine}拿這台的憑證登記，已拒絕。憑證可能外流，請重新配對。`;
    }
    case 'paired_by_former_owner':
      return '配對這台的人已經不是擁有者，請由現在的擁有者重新配對。';
    case 'credential_expired':
      return '憑證過期了，請重新配對。';
  }
}
