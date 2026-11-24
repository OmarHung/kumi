import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CSRF_HEADER } from '../../api/http';

type Handler = (...args: unknown[]) => void;

interface HubState {
  url: string;
  options: unknown;
  retry: ((context: { previousRetryCount: number; retryReason: Error }) => number | null) | undefined;
  handlers: Map<string, Handler>;
  start: ReturnType<typeof vi.fn<() => Promise<void>>>;
  stop: ReturnType<typeof vi.fn<() => Promise<void>>>;
}

const hub = vi.hoisted<HubState>(() => ({
  url: '',
  options: undefined,
  retry: undefined,
  handlers: new Map(),
  start: vi.fn(() => Promise.resolve()),
  stop: vi.fn(() => Promise.resolve()),
}));

vi.mock('@microsoft/signalr', () => {
  class FakeBuilder {
    withUrl(url: string, options: unknown) {
      hub.url = url;
      hub.options = options;
      return this;
    }
    withAutomaticReconnect(policy: { nextRetryDelayInMilliseconds: typeof hub.retry }) {
      hub.retry = policy.nextRetryDelayInMilliseconds;
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        on: (name: string, handler: Handler) => hub.handlers.set(name, handler),
        onreconnected: (handler: Handler) => hub.handlers.set('reconnected', handler),
        onclose: (handler: Handler) => hub.handlers.set('close', handler),
        start: hub.start,
        stop: hub.stop,
      };
    }
  }
  return { HubConnectionBuilder: FakeBuilder, LogLevel: { None: 6 } };
});

const { createSignalRConnection, rejectionOf } = await import('./signalRConnection');

describe('createSignalRConnection', () => {
  beforeEach(() => {
    hub.handlers.clear();
    hub.start.mockClear();
    hub.stop.mockClear();
  });

  it('連到 /v1/live，協商帶 CSRF 標頭，重連的間隔是 0、2、5、10、30 秒之後一直 30 秒', async () => {
    // Arrange
    const connection = createSignalRConnection();

    // Act
    await connection.start();

    // Assert
    expect(hub.url).toBe('/v1/live');
    expect(hub.options).toEqual({ headers: { [CSRF_HEADER]: '1' } });
    expect(hub.start).toHaveBeenCalledOnce();
    expect([0, 1, 2, 3, 4, 9].map((previousRetryCount) => hub.retry?.({ previousRetryCount, retryReason: new Error('網路') }))).toEqual([0, 2_000, 5_000, 10_000, 30_000, 30_000]);
  });

  it('把 items、inbox、重連、關閉轉給處理的函式，格式不對的 items 略過', async () => {
    // Arrange
    const connection = createSignalRConnection();
    const items = vi.fn();
    const inbox = vi.fn();
    const reconnected = vi.fn();
    const closed = vi.fn();
    connection.onItems(items);
    connection.onInbox(inbox);
    connection.onReconnected(reconnected);
    connection.onClosed(closed);
    await connection.start();

    // Act
    hub.handlers.get('items')?.({ projectKey: 'MNT', itemKeys: ['MNT-1'] });
    hub.handlers.get('items')?.({ projectKey: 'MNT', itemKeys: null });
    hub.handlers.get('items')?.({ nope: true });
    hub.handlers.get('inbox')?.({});
    hub.handlers.get('reconnected')?.();
    hub.handlers.get('close')?.();

    // Assert
    expect(items.mock.calls).toEqual([[{ projectKey: 'MNT', itemKeys: ['MNT-1'] }], [{ projectKey: 'MNT', itemKeys: null }]]);
    expect(inbox).toHaveBeenCalledOnce();
    expect(reconnected).toHaveBeenCalledOnce();
    expect(closed).toHaveBeenCalledOnce();
  });

  it('停止之後不再連線；再次 start 重用同一條', async () => {
    // Arrange
    const connection = createSignalRConnection();
    await connection.start();
    await connection.start();
    expect(hub.start).toHaveBeenCalledTimes(2);

    // Act
    await connection.stop();
    await connection.start();

    // Assert
    expect(hub.stop).toHaveBeenCalledOnce();
    expect(hub.start).toHaveBeenCalledTimes(2);
  });

  it('重連時協商被 401、403 拒絕就放棄（回 null）並通知；其他錯誤照間隔', async () => {
    // Arrange
    const connection = createSignalRConnection();
    const rejected = vi.fn();
    connection.onRejected(rejected);
    await connection.start();
    const reason = (statusCode: number) => Object.assign(new Error('HTTP'), { statusCode });

    // Act
    const results = [401, 403, 503, 429].map((statusCode) => hub.retry?.({ previousRetryCount: 1, retryReason: reason(statusCode) }));

    // Assert
    expect(results).toEqual([null, null, 2_000, 2_000]);
    expect(rejected.mock.calls).toEqual([[401], [403]]);
  });

  it('第一次連線被 401 拒絕時 start 丟出 LiveRejectedError，其他錯誤原樣丟出', async () => {
    // Arrange
    const connection = createSignalRConnection();
    hub.start.mockRejectedValueOnce(Object.assign(new Error('HTTP'), { statusCode: 401 }));
    hub.start.mockRejectedValueOnce(new Error('網路'));

    // Act & Assert
    await expect(connection.start()).rejects.toMatchObject({ name: 'LiveRejectedError', status: 401 });
    await expect(connection.start()).rejects.toThrow('網路');
  });

  it('rejectionOf：只認 401、403', () => {
    expect([401, 403, 500, 429].map((statusCode) => rejectionOf({ statusCode }))).toEqual([401, 403, undefined, undefined]);
    expect(rejectionOf(new Error('x'))).toBeUndefined();
  });
});
