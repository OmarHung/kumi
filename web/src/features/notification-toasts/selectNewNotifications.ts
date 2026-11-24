import type { Notification } from '../../api/types';

/**
 * 「已經知道的」通知：開始時（連線第一次連上、每次重連之後）讀到的未讀，之後讀到的也一併記下。
 * latestCreatedAt 是其中最新的建立時間（伺服器給的時間，不用瀏覽器的時鐘）；開始時沒有任何未讀是 null。
 */
export interface ToastSeed {
  readonly knownIds: ReadonlySet<string>;
  readonly latestCreatedAt: number | null;
}

export interface Selection {
  /** 要跳提示的通知，新的在後。 */
  readonly fresh: readonly Notification[];
  readonly seed: ToastSeed;
}

function instantOf(notification: Notification): number {
  return Date.parse(notification.createdAt);
}

/** 開始時靜靜讀一次：全部當成已經知道的，一則都不跳。 */
export function seedFrom(fetched: readonly Notification[]): ToastSeed {
  return selectNewNotifications({ knownIds: new Set(), latestCreatedAt: null }, fetched).seed;
}

/**
 * 建立時間容許的先後顛倒（毫秒）：通知的時間是寫入端取的，兩個並行的交易可能先取時間的後提交，
 * 所以後到的那一則時間可能比已知的最新一則早一點；同一毫秒的兩則也算新。
 */
export const CLOCK_TOLERANCE_MS = 5000;

/**
 * 從最新的未讀裡挑出「新的」：編號沒看過、而且建立時間不早於已知最新一則減 CLOCK_TOLERANCE_MS。
 * 只看編號不夠：把比較新的通知標成已讀之後，更舊的未讀會浮到「最新的 10 則」裡，編號沒看過、卻不是新事件
 * （它們比已知的最新一則早得多，超過容許的範圍）。
 * 開始時沒有任何未讀（latestCreatedAt 是 null）就只看編號。每次都把讀到的全部記進新的 seed，所以同一則不會跳第二次。
 */
export function selectNewNotifications(seed: ToastSeed, fetched: readonly Notification[]): Selection {
  const { knownIds, latestCreatedAt } = seed;
  const fresh = fetched
    .filter((entry) => !knownIds.has(entry.notificationId) && (latestCreatedAt === null || instantOf(entry) >= latestCreatedAt - CLOCK_TOLERANCE_MS))
    // 伺服器是新的在前；提示疊起來是新的在下面，所以舊的先進。
    .toSorted((first, second) => instantOf(first) - instantOf(second));
  const instants = fetched.map(instantOf).filter((instant) => !Number.isNaN(instant));
  return {
    fresh,
    seed: {
      knownIds: new Set([...knownIds, ...fetched.map((entry) => entry.notificationId)]),
      latestCreatedAt: instants.length === 0 ? latestCreatedAt : Math.max(latestCreatedAt ?? Number.NEGATIVE_INFINITY, ...instants),
    },
  };
}
