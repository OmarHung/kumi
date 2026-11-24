import type { ItemsMessage, LiveConnection } from '../features/live/liveTypes';

/** 假的即時連線：測試自己決定什麼時候連上、什麼時候收到訊息。 */
export interface FakeLiveConnection extends LiveConnection {
  readonly starts: () => number;
  readonly stops: () => number;
  /** 接下來的幾次 start 失敗（依序）；用完之後成功。 */
  readonly failNextStarts: (count: number) => void;
  readonly emitItems: (message: ItemsMessage) => void;
  readonly emitInbox: () => void;
  readonly emitReconnected: () => void;
  readonly emitClosed: () => void;
  readonly emitRejected: (status: 401 | 403) => void;
  /** 接下來的 start 一律用這個錯誤失敗（null 恢復）。 */
  readonly failStartsWith: (error: Error | null) => void;
}

export function createFakeLiveConnection(): FakeLiveConnection {
  const items: ((message: ItemsMessage) => void)[] = [];
  const inbox: (() => void)[] = [];
  const reconnected: (() => void)[] = [];
  const closed: (() => void)[] = [];
  let startCount = 0;
  let stopCount = 0;
  const rejected: ((status: 401 | 403) => void)[] = [];
  let failures = 0;
  let failWith: Error | null = null;
  return {
    start: () => {
      startCount += 1;
      if (failWith !== null) return Promise.reject(failWith);
      if (failures > 0) {
        failures -= 1;
        return Promise.reject(new Error('連不上'));
      }
      return Promise.resolve();
    },
    stop: () => {
      stopCount += 1;
      return Promise.resolve();
    },
    onItems: (handler) => {
      items.push(handler);
    },
    onInbox: (handler) => {
      inbox.push(handler);
    },
    onReconnected: (handler) => {
      reconnected.push(handler);
    },
    onClosed: (handler) => {
      closed.push(handler);
    },
    onRejected: (handler) => {
      rejected.push(handler);
    },
    emitRejected: (status) => {
      rejected.forEach((handler) => { handler(status); });
    },
    failStartsWith: (error) => {
      failWith = error;
    },
    starts: () => startCount,
    stops: () => stopCount,
    failNextStarts: (count) => {
      failures = count;
    },
    emitItems: (message) => {
      items.forEach((handler) => { handler(message); });
    },
    emitInbox: () => {
      inbox.forEach((handler) => { handler(); });
    },
    emitReconnected: () => {
      reconnected.forEach((handler) => { handler(); });
    },
    emitClosed: () => {
      closed.forEach((handler) => { handler(); });
    },
  };
}
