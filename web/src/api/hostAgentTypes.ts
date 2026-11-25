/**
 * 主機 AI 代理（組織擁有者管理、全組織的成員使用）在 API 上的形狀。internal/HANDOFF-host-agent.md 第 5 節。
 * 只有擁有者看得到這些資料；成員只在 AI 助理的模型選單看到來源（AiConnectionOption 的 hosted）。
 */
import type { AiEffort, AiModel, AiModelOption, LocalEngineKind } from './aiTypes';

export type HostAgentState = 'awaiting_pairing' | 'active' | 'disabled';

/** 引擎登入的計費方式。只有 api 能開成組織來源。 */
export type HostBilling = 'api' | 'subscription' | 'unknown';

export type HostEngineReadiness =
  | 'ready'
  | 'not_installed'
  | 'version_unsupported'
  | 'not_logged_in'
  | 'not_authorized'
  | 'billing_not_shareable';

export interface HostEngineStatus {
  readonly kind: LocalEngineKind;
  readonly readiness: HostEngineReadiness;
  readonly version: string | null;
  readonly loggedIn: boolean;
  readonly billing: HostBilling;
  readonly models: readonly AiModelOption[];
  /** 給擁有者看的說明（怎麼裝、怎麼登入）；沒問題是 null。 */
  readonly problem: string | null;
  readonly authorizedAt: string | null;
  readonly authorizedBy: { readonly personId: string; readonly displayName: string } | null;
}

export type HostLoginState = 'pending' | 'claimed' | 'completed' | 'failed' | 'cancelled';

export interface HostLogin {
  readonly id: string;
  readonly engine: LocalEngineKind;
  readonly state: HostLoginState;
  readonly expiresAt: string;
  readonly error: string | null;
  readonly startedBy: { readonly personId: string; readonly displayName: string };
  readonly createdAt: string;
}

export type HostAgentWarningKind = 'other_machine' | 'paired_by_former_owner' | 'credential_expired';

/** 要擁有者處理的狀況（都要重新配對）。machineName 只有 other_machine 有。 */
export interface HostAgentWarning {
  readonly kind: HostAgentWarningKind;
  readonly machineName: string | null;
  readonly at: string;
}

export interface HostAgent {
  readonly id: string;
  readonly name: string;
  readonly state: HostAgentState;
  /** 最近 60 秒內登記過。 */
  readonly online: boolean;
  readonly lastSeenAt: string | null;
  readonly machineName: string | null;
  readonly agentVersion: string | null;
  readonly serverUrl: string | null;
  readonly engines: readonly HostEngineStatus[];
  readonly logins: readonly HostLogin[];
  readonly pairingExpiresAt: string | null;
  readonly createdAt: string;
  /** 機器憑證的到期日（worker 在線時會自己換新）；還沒配對是 null。 */
  readonly credentialExpiresAt: string | null;
  readonly warning: HostAgentWarning | null;
}

export type HostSourceStatus =
  | 'ready'
  | 'disabled'
  | 'agent_unavailable'
  | 'agent_offline'
  | 'engine_unavailable'
  | 'quota_reached';

/** 擁有者設的上限，null 是不限。 */
export interface HostSourceLimits {
  readonly dailyRequestsPerPerson: number | null;
  readonly monthlyRequests: number | null;
  readonly monthlyTokens: number | null;
}

export interface HostUsageTotals {
  readonly requests: number;
  readonly completed: number;
  readonly failed: number;
  readonly stopped: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
}

export interface HostSource {
  readonly id: string;
  readonly agentId: string;
  readonly engine: LocalEngineKind;
  readonly name: string;
  readonly enabled: boolean;
  readonly status: HostSourceStatus;
  readonly statusMessage: string | null;
  readonly models: readonly AiModel[];
  readonly defaultModel: string | null;
  readonly defaultEffort: AiEffort | null;
  readonly limits: HostSourceLimits;
  readonly running: number;
  readonly queued: number;
  /** 這個月（組織的時區）的合計。 */
  readonly usage: HostUsageTotals;
  readonly billingAcknowledgedAt: string | null;
  readonly updatedAt: string;
  readonly updatedBy: { readonly personId: string; readonly displayName: string };
}

export interface HostAgentOverview {
  readonly agents: readonly HostAgent[];
  readonly sources: readonly HostSource[];
}

/** 新增或重新配對：配對碼只在這個回應裡。 */
export interface HostAgentPairing {
  readonly agent: HostAgent;
  readonly pairingCode: string;
  readonly pairingExpiresAt: string;
}

/** 開始登入：登入碼只在這個回應裡。 */
export interface HostLoginStart {
  readonly login: HostLogin;
  readonly code: string;
}

export interface HostAgentInput {
  readonly name: string;
  /** 修改時 false 是停用；新增時不看。 */
  readonly enabled: boolean;
}

export interface HostSourceInput {
  readonly agentId: string;
  readonly engine: LocalEngineKind;
  readonly name: string;
  readonly enabled: boolean;
  readonly enabledModels: readonly string[];
  readonly defaultModel: string | null;
  readonly defaultEffort: AiEffort | null;
  readonly limits: HostSourceLimits;
  /** 擁有者確認這個來源以 API 用量計費（不是訂閱的額度）。開著時一定要是 true。 */
  readonly acknowledgeApiBilling: boolean;
}

export interface HostPersonUsage {
  readonly person: { readonly personId: string; readonly displayName: string };
  readonly totals: HostUsageTotals;
}

export interface HostProjectUsage {
  readonly projectKey: string;
  readonly totals: HostUsageTotals;
}

export interface HostSourceUsage {
  /** 例如 2026-11。 */
  readonly month: string;
  readonly totals: HostUsageTotals;
  readonly people: readonly HostPersonUsage[];
  readonly projects: readonly HostProjectUsage[];
}
