import type { QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../api/queryKeys';
import { createLiveInvalidator } from './liveInvalidation';
import { LiveRejectedError, LiveUnavailableError, type LiveConnection, type LiveNotificationHooks } from './liveTypes';

/** 連不上、連線關掉之後再試的間隔（毫秒）：0、2、5、10、30 秒，之後每 30 秒，永遠重試。 */
export const RECONNECT_DELAYS_MS: readonly number[] = [0, 2_000, 5_000, 10_000, 30_000];

export function retryDelay(attempt: number, delays: readonly number[] = RECONNECT_DELAYS_MS): number {
  return delays[Math.min(attempt, delays.length - 1)] ?? 0;
}

export interface LiveUpdateOptions {
  readonly delays?: readonly number[];
  /** 併在一起處理的時間（毫秒）。 */
  readonly coalesceMs?: number;
  /** 自己的人員編號（登入的人）。 */
  readonly selfId?: () => string | undefined;
  /** 新通知的提示：連上（含重連）與收到 inbox 時各呼叫一次。 */
  readonly notifications?: LiveNotificationHooks;
}

/** 可以取消的等待：停止時不留計時器。 */
function createWaiter() {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return {
    wait: (ms: number) =>
      new Promise<void>((resolve) => {
        timer = setTimeout(resolve, ms);
      }),
    cancel: () => {
      if (timer !== undefined) clearTimeout(timer);
    },
  };
}

/** 失效之外，同時通知新通知的提示（沒有就只失效）。 */
function alsoNotify(invalidate: () => void, notify: (() => void) | undefined): () => void {
  return () => {
    invalidate();
    notify?.();
  };
}

/**
 * 登入後的即時更新：連線、把收到的訊息變成快取的失效。回傳停止的函式（登出、工作階段失效、畫面被換掉時呼叫）。
 * 斷線不另外顯示：連回來之後（自動重連成功、第一次沒連上之後連上、被關掉之後重新連上）把所有的快取失效，補回斷線期間漏掉的。
 */
export function startLiveUpdates(queryClient: QueryClient, connection: LiveConnection, options: LiveUpdateOptions = {}): () => void {
  const { delays = RECONNECT_DELAYS_MS, coalesceMs, selfId, notifications } = options;
  const invalidator = createLiveInvalidator(queryClient, { delayMs: coalesceMs, selfId });
  let stopped = false;
  let isRejected = false;
  // 被拒絕（401、403）、載入失敗放棄之後也算停了：重試也一樣。
  const isStopped = () => stopped || isRejected;
  const waiter = createWaiter();

  function giveUp(): void {
    isRejected = true;
    waiter.cancel();
  }

  /**
   * 401 是工作階段失效：讓登入狀態的快取失效，網頁照既有的流程發現沒登入、回到登入頁（分頁在背景時側欄的輪詢不會跑，不能靠它）。
   * 403 是不准，不必動登入狀態。
   */
  function reject(status: 401 | 403): void {
    if (isRejected) return;
    giveUp();
    if (status === 401) void queryClient.invalidateQueries({ queryKey: queryKeys.session });
  }

  /** 連線，失敗就照間隔再試；isRecovery：這是斷線之後的重連（連上就補抓）。 */
  async function connect(isRecovery: boolean): Promise<void> {
    let failures = 0;
    while (!isStopped()) {
      try {
        await connection.start();
        if (isStopped()) return;
        if (isRecovery || failures > 0) invalidator.all();
        notifications?.onConnected();
        return;
      } catch (error) {
        if (error instanceof LiveRejectedError) reject(error.status);
        if (error instanceof LiveUnavailableError) giveUp();
        if (isStopped()) return;
        await waiter.wait(retryDelay(failures, delays));
        failures += 1;
      }
    }
  }

  connection.onItems(invalidator.items);
  connection.onInbox(alsoNotify(invalidator.inbox, notifications?.onInbox));
  connection.onReconnected(alsoNotify(invalidator.all, notifications?.onConnected));
  connection.onRejected(reject);
  connection.onClosed(() => {
    if (!isStopped()) void connect(true);
  });
  void connect(false);

  return () => {
    stopped = true;
    waiter.cancel();
    invalidator.dispose();
    void connection.stop().catch(() => undefined);
  };
}
