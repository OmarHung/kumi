/** 伺服器的 `items` 訊息：這個專案的這些項目有變動。itemKeys 是 null 表示「很多（超過 100 個），整個專案重抓」。 */
export interface ItemsMessage {
  readonly projectKey: string;
  readonly itemKeys: readonly string[] | null;
  /** 這一則裡所有的變動都是同一個人做的時候是那個人的人員編號，否則 null（舊的伺服器沒有這個欄位）。 */
  readonly actorId?: string | null;
}

/** 伺服器拒絕連線：401 是工作階段失效，403 是不准（例如權杖）。這兩種重試也一樣，不再重試。 */
/** 載入即時連線的程式一直失敗（部署新版之後舊分頁的檔名已經不存在）：放棄，不再重試。 */
export class LiveUnavailableError extends Error {
  constructor(cause: unknown) {
    super('載入即時連線的程式失敗', { cause });
    this.name = 'LiveUnavailableError';
  }
}

export class LiveRejectedError extends Error {
  readonly status: 401 | 403;

  constructor(status: 401 | 403) {
    super(`即時連線被拒絕（${String(status)}）`);
    this.name = 'LiveRejectedError';
    this.status = status;
  }
}

/**
 * 即時連線（SignalR 的 /v1/live）的最小介面：畫面與失效的邏輯只認這個，測試用假的。
 * 網頁不會呼叫伺服器的方法，只收伺服器送來的訊息（internal/HANDOFF-live-updates.md 第 5 節）。
 */
export interface LiveConnection {
  /** 連線；連不上時 reject（由呼叫端照間隔重試）；被拒絕（401、403）時 reject LiveRejectedError，不要再試。 */
  start: () => Promise<void>;
  stop: () => Promise<void>;
  onItems: (handler: (message: ItemsMessage) => void) => void;
  onInbox: (handler: () => void) => void;
  /** 連線斷了、底層自動重連成功之後。 */
  onReconnected: (handler: () => void) => void;
  /** 底層自動重連的協商被拒絕（401、403），而且已經放棄重連。 */
  onRejected: (handler: (status: 401 | 403) => void) => void;
  /** 連線關掉了而且不會自己重連（例如伺服器主動斷線）。 */
  onClosed: (handler: () => void) => void;
}

export type LiveConnectionFactory = () => LiveConnection;

/** 即時連線之外還要知道的兩件事（新通知的提示用）：連上了（第一次與每次重連之後），與收到 inbox 推送。 */
export interface LiveNotificationHooks {
  readonly onConnected: () => void;
  readonly onInbox: () => void;
}
