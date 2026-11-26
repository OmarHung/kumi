import type { QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../api/queryKeys';
import { isHandBusy, subscribeToHandBusy } from './handActivity';
import { wereAllWrittenHere } from './recentWrites';
import type { ItemsMessage } from './liveTypes';

/** 同一段時間收到的訊息併成一次處理（毫秒）。 */
export const COALESCE_MS = 150;

/** 專案底下跟項目的變動有關的資料：清單、可選的上層、已刪除、進度的數字（週期與里程碑）。 */
function projectKeys(projectKey: string): readonly (readonly unknown[])[] {
  return [
    // 前綴：表格、看板、行事曆、時間軸、子項目的清單都在 'items' 底下（api/queryKeys.ts）。
    [...queryKeys.project(projectKey), 'items'],
    [...queryKeys.project(projectKey), 'parent-candidates'],
    queryKeys.deletedItems(projectKey),
    queryKeys.cycles(projectKey),
    queryKeys.milestones(projectKey),
  ];
}

/** 整個專案的項目（內容、活動、附件、關注）：項目的查詢鍵是 ['item', 編號]，編號以「專案代號-」開頭。 */
function isItemOfProject(queryKey: readonly unknown[], projectKey: string): boolean {
  const [scope, key] = queryKey;
  return scope === 'item' && typeof key === 'string' && key.startsWith(`${projectKey}-`);
}

/** projectKey → 變動的項目編號；null 是整個專案。 */
type Pending = ReadonlyMap<string, ReadonlySet<string> | null>;

function mergeMessage(pending: Pending, message: ItemsMessage): Pending {
  const next = new Map(pending);
  const existing = pending.get(message.projectKey);
  if (message.itemKeys === null || (pending.has(message.projectKey) && existing === null)) {
    next.set(message.projectKey, null);
  } else {
    next.set(message.projectKey, new Set([...(existing ?? []), ...message.itemKeys]));
  }
  return next;
}

function invalidateItems(queryClient: QueryClient, pending: Pending, invalidateOverviews: () => void): void {
  pending.forEach((itemKeys, projectKey) => {
    projectKeys(projectKey).forEach((queryKey) => {
      void queryClient.invalidateQueries({ queryKey });
    });
    if (itemKeys === null) {
      void queryClient.invalidateQueries({ predicate: (query) => isItemOfProject(query.queryKey, projectKey) });
      return;
    }
    itemKeys.forEach((itemKey) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.item(itemKey) });
    });
  });
  // 側欄的數量與「我的工作」的四種版面。
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
  // 專案總覽的數字（任何專案的項目變了都可能動到）；有節流，見 createOverviewThrottle。
  invalidateOverviews();
}

/** 專案總覽最多這麼久失效一次（毫秒）：後端每次都掃全組織的項目，不能跟著每 500 毫秒一輪的推送重抓。 */
export const OVERVIEW_THROTTLE_MS = 10_000;

/**
 * 總覽失效的節流：前緣馬上做（第一次不用等），期間內又來的記成一次，到期補上，不會丟掉最後一次。
 * 清單等其他快取不走這裡。
 */
function createOverviewThrottle(queryClient: QueryClient) {
  let lastAt: number | undefined;
  let timer: ReturnType<typeof setTimeout> | undefined;
  function run(): void {
    timer = undefined;
    lastAt = Date.now();
    void queryClient.invalidateQueries({ queryKey: queryKeys.overviews });
  }
  return {
    invalidate: (): void => {
      if (timer !== undefined) return;
      const waitMs = lastAt === undefined ? 0 : Math.max(0, OVERVIEW_THROTTLE_MS - (Date.now() - lastAt));
      if (waitMs === 0) run();
      else timer = setTimeout(run, waitMs);
    },
    dispose: (): void => {
      if (timer !== undefined) clearTimeout(timer);
      timer = undefined;
    },
  };
}

/** 寫入還在送，最多先記著這麼久（毫秒）：卡住或離線暫停的寫入不能讓即時更新整個停住。拖曳、輸入沒有上限。 */
export const WRITE_WAIT_LIMIT_MS = 10_000;

/** 還值得等的寫入：還在送、而且送了不到上限。回傳要再等多久（沒有要等的是 0）。 */
function writeWaitMs(queryClient: QueryClient): number {
  const now = Date.now();
  const waits = queryClient
    .getMutationCache()
    .getAll()
    .filter((mutation) => mutation.state.status === 'pending')
    .map((mutation) => WRITE_WAIT_LIMIT_MS - (now - mutation.state.submittedAt));
  return Math.max(0, ...waits);
}

/** 自己剛寫入的項目被推回來：寫入的 onSuccess 已經照設計更新了快取，再重抓只會讓「停在原位」的列跳走。 */
function isOwnEcho(queryClient: QueryClient, me: string | undefined, { actorId, itemKeys }: ItemsMessage): boolean {
  return me !== undefined && actorId === me && itemKeys !== null && wereAllWrittenHere(queryClient, itemKeys);
}

/** 還沒處理的變動：哪些項目、整個重抓、通知。 */
function createPendingWork() {
  let items: Pending = new Map();
  let isAll = false;
  let isInbox = false;
  return {
    hasWork: () => isAll || isInbox || items.size > 0,
    addItems: (message: ItemsMessage) => {
      items = mergeMessage(items, message);
    },
    markAll: () => {
      isAll = true;
    },
    markInbox: () => {
      isInbox = true;
    },
    takeInbox: () => {
      const was = isInbox;
      isInbox = false;
      return was;
    },
    /** 取走要處理的項目：全部重抓時是 'all'，沒有東西是 null。 */
    takeItems: (): Pending | 'all' | null => {
      const batch = items;
      items = new Map();
      if (isAll) {
        isAll = false;
        return 'all';
      }
      return batch.size > 0 ? batch : null;
    },
  };
}

export interface LiveInvalidator {
  readonly items: (message: ItemsMessage) => void;
  readonly inbox: () => void;
  /** 重連之後：所有的快取都失效（只會重抓畫面上正在用的）。 */
  readonly all: () => void;
  readonly dispose: () => void;
}

export interface LiveInvalidatorOptions {
  readonly delayMs?: number;
  /** 自己的人員編號（登入的人）：用來略過自己剛寫入的修改被推回來。 */
  readonly selfId?: () => string | undefined;
}

/**
 * 收到的「哪些東西變了」先併在一起（COALESCE_MS），再讓對應的快取失效，API 照舊重抓（看得到什麼由 API 的權限決定）。
 * 手上有東西（拖曳中的卡片、開著輸入的儲存格）時先記著，等結束再失效，沒有上限；
 * 寫入還在送時也先記著，但最多等 WRITE_WAIT_LIMIT_MS：卡住或離線暫停的寫入不能讓即時更新整個停住。
 * isWaitingOnWrite：timer 是在等寫入結束的上限（不是併批的計時），寫入一結束就改成馬上排。
 * 自己剛寫入的項目被推回來（actorId 是自己、每個項目都是這個分頁剛寫過的）整則略過。
 */
export function createLiveInvalidator(queryClient: QueryClient, { delayMs = COALESCE_MS, selfId = () => undefined }: LiveInvalidatorOptions = {}): LiveInvalidator {
  const work = createPendingWork();
  const overviewThrottle = createOverviewThrottle(queryClient);
  let isDisposed = false;
  let isWaitingOnWrite = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  function flush(): void {
    timer = undefined;
    isWaitingOnWrite = false;
    if (work.takeInbox()) void queryClient.invalidateQueries({ queryKey: ['notifications'] });
    // 手上有東西時留著，結束時由 resumeWhenIdle 再排一次。
    if (isHandBusy()) return;
    const wait = writeWaitMs(queryClient);
    if (wait > 0) {
      timer = work.hasWork() ? setTimeout(flush, wait) : undefined;
      isWaitingOnWrite = timer !== undefined;
      return;
    }
    const batch = work.takeItems();
    if (batch === 'all') void queryClient.invalidateQueries();
    else if (batch !== null) invalidateItems(queryClient, batch, overviewThrottle.invalidate);
  }

  function schedule(): void {
    if (!isDisposed) timer ??= setTimeout(flush, delayMs);
  }

  function resumeWhenIdle(): void {
    if (!work.hasWork() || isHandBusy() || writeWaitMs(queryClient) > 0) return;
    if (isWaitingOnWrite && timer !== undefined) {
      clearTimeout(timer);
      timer = undefined;
      isWaitingOnWrite = false;
    }
    schedule();
  }

  const stopWatching = [subscribeToHandBusy(resumeWhenIdle), queryClient.getMutationCache().subscribe(resumeWhenIdle)];
  const request = (mark: () => void) => () => { mark(); schedule(); };

  return {
    items: (message) => {
      if (!isOwnEcho(queryClient, selfId(), message)) request(() => { work.addItems(message); })();
    },
    inbox: request(work.markInbox),
    all: request(work.markAll),
    dispose: () => {
      isDisposed = true;
      if (timer !== undefined) clearTimeout(timer);
      timer = undefined;
      overviewThrottle.dispose();
      stopWatching.forEach((stop) => { stop(); });
    },
  };
}
