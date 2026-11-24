import { CSRF_HEADER } from '../../api/http';
import { retryDelay } from './startLiveUpdates';
import { pageErrorReporter } from '../../lib/errorReporting';
import { LiveRejectedError, LiveUnavailableError, type ItemsMessage, type LiveConnection } from './liveTypes';

/** 載入函式庫連續失敗這麼多次就放棄（部署新版之後舊分頁的檔名已經不存在，再試也一樣）。 */
export const MAX_LOAD_FAILURES = 3;

export const LIVE_PATH = '/v1/live';

/** 協商（POST）要帶和其他請求一樣的標頭；Cookie 由瀏覽器帶。 */
const LIVE_HEADERS = { [CSRF_HEADER]: '1' };

/** SignalR 的 HttpError 帶 statusCode：協商被 401、403 拒絕。其他錯誤（網路、5xx、429）是 undefined。 */
export function rejectionOf(error: unknown): 401 | 403 | undefined {
  const status = typeof error === 'object' && error !== null && 'statusCode' in error ? error.statusCode : undefined;
  return status === 401 || status === 403 ? status : undefined;
}

/** 重連時協商被拒絕（工作階段失效、不准）：回 null 放棄，之後的處理交給 onRejected；其他錯誤照間隔重試。 */
function reconnectDelay(onRejected: (status: 401 | 403) => void) {
  return ({ previousRetryCount, retryReason }: { previousRetryCount: number; retryReason: Error }): number | null => {
    const status = rejectionOf(retryReason);
    if (status === undefined) return retryDelay(previousRetryCount);
    onRejected(status);
    return null;
  };
}

function isItemsMessage(value: unknown): value is ItemsMessage {
  if (typeof value !== 'object' || value === null) return false;
  const { projectKey, itemKeys } = value as { projectKey?: unknown; itemKeys?: unknown };
  return typeof projectKey === 'string' && (itemKeys === null || (Array.isArray(itemKeys) && itemKeys.every((key) => typeof key === 'string')));
}

interface Emitter<T> {
  readonly add: (handler: (value: T) => void) => void;
  readonly emit: (value: T) => void;
}

function createEmitter<T>(): Emitter<T> {
  const handlers: ((value: T) => void)[] = [];
  return {
    add: (handler) => {
      handlers.push(handler);
    },
    emit: (value) => {
      handlers.forEach((handler) => { handler(value); });
    },
  };
}

/** 載入失敗幾次之後放棄：前幾次原樣丟出（由呼叫端重試），第 MAX_LOAD_FAILURES 次記一筆、丟出放棄的錯誤。 */
function withLoadLimit<T>(load: () => Promise<T>): () => Promise<T> {
  let failures = 0;
  return async () => {
    try {
      return await load();
    } catch (error) {
      failures += 1;
      if (failures < MAX_LOAD_FAILURES) throw error;
      // 不另外顯示提示，記一筆讓維運看得到。
      pageErrorReporter().report('error', error);
      throw new LiveUnavailableError(error);
    }
  };
}

/**
 * 真的連線（SignalR）。函式庫在第一次連線時才動態載入，不放進第一次載入的程式。
 * 底層的自動重連永遠重試（間隔見 startLiveUpdates.ts）；第一次連不上與被關掉之後由 startLiveUpdates 重試。
 */
export function createSignalRConnection(): LiveConnection {
  const items = createEmitter<ItemsMessage>();
  const inbox = createEmitter<undefined>();
  const reconnected = createEmitter<undefined>();
  const closed = createEmitter<undefined>();
  const rejected = createEmitter<401 | 403>();
  let hub: { start: () => Promise<void>; stop: () => Promise<void> } | undefined;
  let stopped = false;
  const isStopped = () => stopped;

  async function build() {
    const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
    const connection = new HubConnectionBuilder()
      .withUrl(LIVE_PATH, { headers: LIVE_HEADERS })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: reconnectDelay(rejected.emit) })
      .configureLogging(LogLevel.None)
      .build();
    connection.on('items', (message: unknown) => {
      if (isItemsMessage(message)) items.emit(message);
    });
    connection.on('inbox', () => { inbox.emit(undefined); });
    connection.onreconnected(() => { reconnected.emit(undefined); });
    connection.onclose(() => { closed.emit(undefined); });
    return connection;
  }

  const load = withLoadLimit(build);

  return {
    start: async () => {
      if (isStopped()) return;
      hub ??= await load();
      // 載入函式庫的時候已經被停掉：不要再連。
      if (isStopped()) return;
      try {
        await hub.start();
      } catch (error) {
        const status = rejectionOf(error);
        throw status === undefined ? error : new LiveRejectedError(status);
      }
    },
    stop: async () => {
      stopped = true;
      await hub?.stop();
    },
    onItems: items.add,
    onInbox: inbox.add,
    onReconnected: reconnected.add,
    onRejected: rejected.add,
    onClosed: closed.add,
  };
}
