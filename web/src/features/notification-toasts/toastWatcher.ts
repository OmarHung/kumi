import type { Notification } from '../../api/types';
import { pageErrorReporter } from '../../lib/errorReporting';
import { seedFrom, selectNewNotifications, type ToastSeed } from './selectNewNotifications';

export interface ToastWatcherOptions {
  /** 最新的未讀（伺服器的順序，新的在前）。 */
  readonly fetchUnread: () => Promise<readonly Notification[]>;
  /** 現在能不能跳提示：分頁看得見、不在收件匣、設定不是「不顯示」。不行的時候照樣記下看過了。 */
  readonly canShow: () => boolean;
  /** 這個項目正開在畫面上：它的通知不跳。 */
  readonly isItemOpen: (itemKey: string) => boolean;
  readonly onFresh: (notifications: readonly Notification[]) => void;
  /** 排隊的某一步丟了例外：回報就好，之後的推送照常處理。預設回報到伺服器。 */
  readonly reportError?: (error: unknown) => void;
}

export interface ToastWatcher {
  /** 即時連線連上（第一次、每次重連之後）：靜靜讀一次未讀當作「已經知道的」，不跳。 */
  readonly start: () => void;
  /** 收到 inbox 推送：讀最新的未讀，挑出新的跳提示。 */
  readonly check: () => void;
  /** 登出、畫面被換掉時 false，StrictMode 重新掛上時再 true。 */
  readonly setActive: (isActive: boolean) => void;
}

function reportToServer(error: unknown): void {
  pageErrorReporter().report('error', error);
}

/** 任何一步（包括 onFresh）丟例外都不能讓之後永遠不跳：回報之後鏈子照常往下。 */
async function runSafely(task: () => Promise<void>, report: (error: unknown) => void): Promise<void> {
  try {
    await task();
  } catch (error) {
    report(error);
  }
}

/**
 * 新通知的提示怎麼知道「什麼是新的」：推送不帶內容，所以收到 inbox 之後用既有的 API 讀最新的未讀，
 * 照 selectNewNotifications 的規則挑。讀取一次一次排隊（同一時間只有一個在讀），還沒開始的檢查合併成一次：
 * 連續推送不會讀一堆。讀失敗不打擾使用者（提示只是側欄數字的補充），下一次推送或重連再讀；
 * 還沒有基準的時候（開始時讀失敗）第一次讀到的當作基準，不跳。排隊的某一步丟例外只回報，鏈子不會斷。
 */
export function createToastWatcher(options: ToastWatcherOptions): ToastWatcher {
  const { fetchUnread, canShow, isItemOpen, onFresh, reportError = reportToServer } = options;
  let seed: ToastSeed | null = null;
  let needsSeed = true;
  let isActive = true;
  let isQueued = false;
  let tail: Promise<void> = Promise.resolve();

  async function read(): Promise<void> {
    // 在讀取開頭取走「要建立基準」的旗標：讀的時候又重連，重連之後的那一次才是基準（見 docs/design/08）。
    const isSeeding = needsSeed || seed === null;
    needsSeed = false;
    let fetched: readonly Notification[];
    try {
      fetched = await fetchUnread();
    } catch {
      // 讀失敗不打擾使用者；還沒有基準的話下一次再試著建立。
      if (isSeeding) needsSeed = true;
      return;
    }
    if (!isActive) return;
    const current = seed;
    if (isSeeding || current === null) {
      seed = seedFrom(fetched);
      return;
    }
    const selection = selectNewNotifications(current, fetched);
    seed = selection.seed;
    if (!canShow()) return;
    const shown = selection.fresh.filter((entry) => !isItemOpen(entry.itemKey));
    if (shown.length > 0) onFresh(shown);
  }

  function enqueue(): void {
    if (isQueued || !isActive) return;
    isQueued = true;
    tail = tail.then(() => {
      isQueued = false;
      return runSafely(read, reportError);
    });
  }

  return {
    start: () => {
      needsSeed = true;
      enqueue();
    },
    check: enqueue,
    setActive: (next) => {
      isActive = next;
    },
  };
}
