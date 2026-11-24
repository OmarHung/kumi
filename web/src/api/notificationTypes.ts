/**
 * 通知與關注的資料形狀（從 types.ts 拆出來，types.ts 會轉出這些型別，呼叫端照舊從 types.ts 匯入）。
 * 照 internal/HANDOFF-pm1-collaboration.md 第 4 節手寫。
 */
import type { Instant, PersonRef } from './types';

/** assigned 指派給你；mentioned 在說明或留言裡提及你；commented 你關注的項目有人留言；state_changed 你關注的項目改了狀態。 */
/** replied：有人回覆了你的留言（比 commented 優先、比 mentioned 次之，同一則只收一種）。 */
export type NotificationKind = 'assigned' | 'mentioned' | 'commented' | 'state_changed' | 'replied';

export interface Notification {
  readonly notificationId: string;
  readonly kind: NotificationKind;
  readonly projectKey: string;
  readonly itemKey: string;
  readonly itemTitle: string;
  /** 做這件事的人；人員目錄查不到時 displayName 是「查不到的人」。 */
  readonly actor: PersonRef;
  /** 只有 state_changed 有：改之前與改之後的狀態名稱。 */
  readonly fromStateName: string | null;
  readonly toStateName: string | null;
  /** 說明或留言的開頭，沒有時是 null。 */
  readonly excerpt: string | null;
  /** 由留言產生的通知（留言、留言裡的提及）是那一則留言的編號（也就是活動的 id）；其他種類與較早的通知是 null。 */
  readonly commentId: string | null;
  readonly createdAt: Instant;
  /** 還沒讀是 null。 */
  readonly readAt: Instant | null;
}

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
