/**
 * 通知與關注的資料形狀（從 types.ts 拆出來，types.ts 會轉出這些型別，呼叫端照舊從 types.ts 匯入）。
 * 照 internal/HANDOFF-pm1-collaboration.md 第 4 節手寫。
 */
import type { Instant, PersonRef } from './types';

/** assigned 指派給你；mentioned 在說明或留言裡提及你；commented 你關注的項目有人留言；state_changed 你關注的項目改了狀態。 */
/** replied：有人回覆了你的留言（比 commented 優先、比 mentioned 次之，同一則只收一種）。 */
export type ItemNotificationKind = 'assigned' | 'mentioned' | 'commented' | 'state_changed' | 'replied';

/** account_security：你的帳號發生了安全相關的事（建立權杖、命令列登入、授權 AI 用戶端、登入碼被重複使用），沒有專案與項目。 */
export type NotificationKind = ItemNotificationKind | 'account_security';

/**
 * 帳號通知發生了什麼事：access_token_created 手動建立權杖；cli_login_tokens_issued 命令列透過瀏覽器登入（一次一到兩把）；
 * oauth_app_authorized 授權了 AI 用戶端（Claude Code 等）連到帳號；cli_login_code_reused 登入碼被重複使用，同一次登入發出的權杖已撤銷。
 */
export type AccountNoticeEvent =
  | 'access_token_created'
  | 'cli_login_tokens_issued'
  | 'oauth_app_authorized'
  | 'cli_login_code_reused';

export interface AccountNoticeToken {
  readonly name: string;
  /** 對外的範圍名稱（items:read 這些）。 */
  readonly scopes: readonly string[];
  /** 不會過期是 null。 */
  readonly expiresAt: Instant | null;
}

/** 每個種類只填用得到的欄位，其他是空陣列、null 或 0。 */
export interface AccountNotice {
  readonly event: AccountNoticeEvent;
  /** 手動建立：1 把；命令列登入：1 到 2 把。 */
  readonly tokens: readonly AccountNoticeToken[];
  /** OAuth 授權的範圍。 */
  readonly scopes: readonly string[];
  /** 伺服器看到的來源位址。 */
  readonly sourceAddress: string | null;
  readonly machineName: string | null;
  readonly clientName: string | null;
  readonly clientHost: string | null;
  /** 只有 cli_login_code_reused：撤銷了幾把。 */
  readonly revokedTokens: number;
}

interface NotificationBase {
  readonly notificationId: string;
  /** 做這件事的人；人員目錄查不到時 displayName 是「查不到的人」。帳號通知是本人。 */
  readonly actor: PersonRef;
  /** 只有 state_changed 有：改之前與改之後的狀態名稱。 */
  readonly fromStateName: string | null;
  readonly toStateName: string | null;
  /** 說明或留言的開頭，沒有時是 null。 */
  readonly excerpt: string | null;
  /** 由留言產生的通知（留言、留言裡的提及）是那一則留言的編號（也就是活動的 id）；其他種類與較早的通知是 null。 */
  readonly commentId: string | null;
  /** 摘要是留言編輯之後換上去的新內容：畫面在摘要後面註明「已編輯」。 */
  readonly edited: boolean;
  readonly createdAt: Instant;
  /** 還沒讀是 null。 */
  readonly readAt: Instant | null;
}

/** 和項目有關的通知：連到項目（或項目裡的那一則留言）。 */
export interface ItemNotification extends NotificationBase {
  readonly kind: ItemNotificationKind;
  readonly projectKey: string;
  readonly itemKey: string;
  readonly itemTitle: string;
  /** API 一律回這個欄位（項目的通知是 null）。 */
  readonly account?: null;
}

/** 帳號通知：只有本人看得到，沒有專案與項目，連到「命令列與整合」。 */
export interface AccountSecurityNotification extends NotificationBase {
  readonly kind: 'account_security';
  readonly projectKey: null;
  readonly itemKey: null;
  readonly itemTitle: null;
  readonly account: AccountNotice;
}

/** 以 kind 區分：account_security 是帳號通知，其他都是項目的通知。 */
export type Notification = ItemNotification | AccountSecurityNotification;

export interface NotificationPage {
  readonly items: readonly Notification[];
  readonly nextCursor: string | null;
}

export interface NotificationQuery {
  readonly unread?: boolean;
  readonly cursor?: string;
  /** 1～100，預設 50。 */
  readonly limit?: number;
}

export interface UnreadCount {
  readonly count: number;
}

/** 標成已讀、全部標為已讀的回應：這次真的改了幾則。 */
export interface UpdatedCount {
  readonly updated: number;
}

/** 你為什麼在關注：manual 自己按的；creator 建立的人；assignee 負責人；commenter 留過言；mentioned 被提及。 */
export type SubscriptionReason = 'manual' | 'creator' | 'assignee' | 'commenter' | 'mentioned';

export interface ItemSubscription {
  readonly following: boolean;
  /** 沒有在關注時是 null。 */
  readonly reason: SubscriptionReason | null;
  /** 是不是這個專案的成員：只有成員收得到通知。不是成員（例如組織的管理員）時 false，PUT 會回 409 subscription.not_member。 */
  readonly canFollow: boolean;
}
