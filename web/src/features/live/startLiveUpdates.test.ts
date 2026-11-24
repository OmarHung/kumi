import { QueryClient } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createFakeLiveConnection } from '../../test/liveConnection';
import { LiveRejectedError } from './liveTypes';
import { noteOwnWrites, RECENT_WRITE_MS } from './recentWrites';
import { WRITE_WAIT_LIMIT_MS } from './liveInvalidation';
import { retryDelay, startLiveUpdates } from './startLiveUpdates';

const COALESCE_MS = 150;

const ME = 'person-me';

function setup() {
  const queryClient = new QueryClient();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  const connection = createFakeLiveConnection();
  const stop = startLiveUpdates(queryClient, connection, { selfId: () => ME });
  return { queryClient, invalidate, connection, stop };
}

function invalidatedKeys(invalidate: ReturnType<typeof setup>['invalidate']) {
  return invalidate.mock.calls.map(([filters]) => filters?.queryKey);
}

describe('startLiveUpdates', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('連線之後同一段時間的 items 訊息併成一次，專案與項目的快取一起失效', async () => {
    // Arrange
    const { connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1'] });
    connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-7', 'MNT-1'] });
    expect(invalidate).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(COALESCE_MS);

    // Assert
    expect(invalidatedKeys(invalidate)).toEqual([
      ['project', 'MNT', 'items'],
      ['project', 'MNT', 'parent-candidates'],
      ['project', 'MNT', 'deleted-items'],
      ['project', 'MNT', 'cycles'],
      ['project', 'MNT', 'milestones'],
      ['item', 'MNT-1'],
      ['item', 'MNT-7'],
      ['my-work'],
    ]);
  });

  it('itemKeys 是 null 時，編號以專案代號開頭的項目全部失效，別的專案不動', async () => {
    // Arrange
    const { queryClient, connection, invalidate } = setup();
    queryClient.setQueryData(['item', 'MNT-3'], { key: 'MNT-3' });
    queryClient.setQueryData(['item', 'MNT-3', 'activity'], []);
    queryClient.setQueryData(['item', 'MNTX-1'], { key: 'MNTX-1' });
    queryClient.setQueryData(['item', 'WEB-3'], { key: 'WEB-3' });
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitItems({ projectKey: 'MNT', itemKeys: null });
    await vi.advanceTimersByTimeAsync(COALESCE_MS);

    // Assert
    const predicate = invalidate.mock.calls.map(([filters]) => filters?.predicate).find((candidate) => candidate !== undefined);
    const stale = queryClient
      .getQueryCache()
      .getAll()
      .filter((query) => predicate?.(query) === true)
      .map((query) => query.queryKey);
    expect(stale).toEqual([['item', 'MNT-3'], ['item', 'MNT-3', 'activity']]);
  });

  it('不同專案的訊息各自失效，我的工作只失效一次', async () => {
    // Arrange
    const { connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1'] });
    connection.emitItems({ projectKey: 'WEB', itemKeys: ['WEB-2'] });
    await vi.advanceTimersByTimeAsync(COALESCE_MS);

    // Assert
    const keys = invalidatedKeys(invalidate);
    expect(keys).toContainEqual(['item', 'MNT-1']);
    expect(keys).toContainEqual(['item', 'WEB-2']);
    expect(keys.filter((key) => key?.[0] === 'my-work')).toHaveLength(1);
  });

  it('inbox 讓通知失效', async () => {
    // Arrange
    const { connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitInbox();
    await vi.advanceTimersByTimeAsync(COALESCE_MS);

    // Assert
    expect(invalidatedKeys(invalidate)).toEqual([['notifications']]);
  });

  it('寫入還在送的時候先記著，送完才失效', async () => {
    // Arrange
    const { queryClient, connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);
    let finish: () => void = () => undefined;
    const mutation = queryClient
      .getMutationCache()
      .build(queryClient, { mutationFn: () => new Promise<void>((resolve) => { finish = resolve; }) })
      .execute(undefined);

    // Act
    await vi.advanceTimersByTimeAsync(0);
    connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1'] });
    await vi.advanceTimersByTimeAsync(COALESCE_MS * 3);

    // Assert
    expect(invalidate).not.toHaveBeenCalled();
    finish();
    await mutation;
    await vi.advanceTimersByTimeAsync(COALESCE_MS);
    expect(invalidatedKeys(invalidate)).toContainEqual(['item', 'MNT-1']);
  });

  it('連不上時照 0、2、5、10、30 秒再試，連上之後把所有的快取失效', async () => {
    // Arrange
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const connection = createFakeLiveConnection();
    connection.failNextStarts(3);

    // Act
    startLiveUpdates(queryClient, connection);
    await vi.advanceTimersByTimeAsync(0);
    // 第一次失敗之後 0 秒再試（第二次也失敗），下一次等 2 秒。
    expect(connection.starts()).toBe(2);
    await vi.advanceTimersByTimeAsync(1_999);
    expect(connection.starts()).toBe(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(connection.starts()).toBe(3);
    await vi.advanceTimersByTimeAsync(5_000);

    // Assert
    expect(connection.starts()).toBe(4);
    await vi.advanceTimersByTimeAsync(COALESCE_MS);
    expect(invalidate).toHaveBeenCalledWith();
  });

  it('第一次就連上不必補抓', async () => {
    // Arrange
    const { invalidate } = setup();

    // Act
    await vi.advanceTimersByTimeAsync(COALESCE_MS * 2);

    // Assert
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('自動重連成功之後所有的快取失效', async () => {
    // Arrange
    const { connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitReconnected();
    await vi.advanceTimersByTimeAsync(COALESCE_MS);

    // Assert
    expect(invalidate).toHaveBeenCalledOnce();
    expect(invalidate).toHaveBeenCalledWith();
  });

  it('連線被關掉（伺服器主動斷線）之後重新連，連上補抓', async () => {
    // Arrange
    const { connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitClosed();
    await vi.advanceTimersByTimeAsync(COALESCE_MS);

    // Assert
    expect(connection.starts()).toBe(2);
    expect(invalidate).toHaveBeenCalledWith();
  });

  it('停止之後關掉連線，不再重試也不再處理訊息', async () => {
    // Arrange
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const connection = createFakeLiveConnection();
    connection.failNextStarts(10);
    const stop = startLiveUpdates(queryClient, connection);
    await vi.advanceTimersByTimeAsync(0);

    // Act
    stop();
    connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1'] });
    await vi.advanceTimersByTimeAsync(60_000);

    // Assert
    expect(connection.stops()).toBe(1);
    expect(connection.starts()).toBeLessThanOrEqual(2);
    expect(invalidate).not.toHaveBeenCalled();
  });

  it.each([401, 403] as const)('協商被拒絕（%i）：不再重試', async (status) => {
    // Arrange
    const { connection } = setup();
    const queryClient = new QueryClient();
    const own = createFakeLiveConnection();
    own.failStartsWith(new LiveRejectedError(status));

    // Act
    startLiveUpdates(queryClient, own);
    await vi.advanceTimersByTimeAsync(120_000);

    // Assert
    expect(own.starts()).toBe(1);
    expect(connection.starts()).toBe(1);
  });

  it('401：讓工作階段的快取失效（網頁照既有的流程回到登入頁）；403 不失效', async () => {
    // Arrange
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const unauthorized = createFakeLiveConnection();
    unauthorized.failStartsWith(new LiveRejectedError(401));
    const forbidden = createFakeLiveConnection();
    forbidden.failStartsWith(new LiveRejectedError(403));

    // Act
    startLiveUpdates(queryClient, forbidden);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(invalidate).not.toHaveBeenCalled();
    startLiveUpdates(queryClient, unauthorized);
    await vi.advanceTimersByTimeAsync(1_000);

    // Assert
    expect(invalidate).toHaveBeenCalledOnce();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['session'] });
  });

  it('底層重連被拒絕（401）而放棄之後，連線關掉也不再重新連', async () => {
    // Arrange
    const { connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitRejected(401);
    connection.emitClosed();
    await vi.advanceTimersByTimeAsync(120_000);

    // Assert
    expect(connection.starts()).toBe(1);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['session'] });
  });

  it('其他錯誤（網路、5xx、429）照間隔重試', async () => {
    // Arrange
    const queryClient = new QueryClient();
    const connection = createFakeLiveConnection();
    connection.failStartsWith(new Error('HTTP 503'));

    // Act
    startLiveUpdates(queryClient, connection);
    await vi.advanceTimersByTimeAsync(37_000);

    // Assert：0、2、5、10 秒各一次，加上第一次
    expect(connection.starts()).toBe(5);
  });

  describe('自己的修改被推回來', () => {
    it('自己做的、而且每一個項目都是這個分頁剛寫入的：整則略過', async () => {
      // Arrange
      const { queryClient, connection, invalidate } = setup();
      await vi.advanceTimersByTimeAsync(0);
      noteOwnWrites(queryClient, ['MNT-1', 'MNT-2']);

      // Act
      connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1', 'MNT-2'], actorId: ME });
      await vi.advanceTimersByTimeAsync(COALESCE_MS);

      // Assert
      expect(invalidate).not.toHaveBeenCalled();
    });

    it.each([
      ['混了這個分頁沒寫過的項目（同一個人在別的分頁改的）', ['MNT-1', 'MNT-9'], ME],
      ['混了別人的變動（actorId 是 null）', ['MNT-1'], null],
      ['別人做的', ['MNT-1'], 'person-other'],
      ['舊的伺服器沒有 actorId', ['MNT-1'], undefined],
    ] as const)('照舊失效：%s', async (_name, itemKeys, actorId) => {
      // Arrange
      const { queryClient, connection, invalidate } = setup();
      await vi.advanceTimersByTimeAsync(0);
      noteOwnWrites(queryClient, ['MNT-1']);

      // Act
      connection.emitItems({ projectKey: 'MNT', itemKeys, actorId });
      await vi.advanceTimersByTimeAsync(COALESCE_MS);

      // Assert
      expect(invalidatedKeys(invalidate)).toContainEqual(['item', 'MNT-1']);
    });

    it('itemKeys 是 null（很多）時不略過', async () => {
      // Arrange
      const { queryClient, connection, invalidate } = setup();
      await vi.advanceTimersByTimeAsync(0);
      noteOwnWrites(queryClient, ['MNT-1']);

      // Act
      connection.emitItems({ projectKey: 'MNT', itemKeys: null, actorId: ME });
      await vi.advanceTimersByTimeAsync(COALESCE_MS);

      // Assert
      expect(invalidatedKeys(invalidate)).toContainEqual(['my-work']);
    });

    it('寫入已經是 10 秒以前的就不算「剛寫入」', async () => {
      // Arrange
      const { queryClient, connection, invalidate } = setup();
      await vi.advanceTimersByTimeAsync(0);
      noteOwnWrites(queryClient, ['MNT-1']);
      await vi.advanceTimersByTimeAsync(RECENT_WRITE_MS + 1);

      // Act
      connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1'], actorId: ME });
      await vi.advanceTimersByTimeAsync(COALESCE_MS);

      // Assert
      expect(invalidatedKeys(invalidate)).toContainEqual(['item', 'MNT-1']);
    });
  });

  it('寫入卡住（超過 10 秒還沒結束）時照樣失效，不讓即時更新整個停住', async () => {
    // Arrange
    const { queryClient, connection, invalidate } = setup();
    await vi.advanceTimersByTimeAsync(0);
    void queryClient
      .getMutationCache()
      .build(queryClient, { mutationFn: () => new Promise<void>(() => undefined) })
      .execute(undefined);
    await vi.advanceTimersByTimeAsync(0);

    // Act
    connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1'] });
    await vi.advanceTimersByTimeAsync(WRITE_WAIT_LIMIT_MS - 1_000);
    expect(invalidate).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1_000 + COALESCE_MS);

    // Assert
    expect(invalidatedKeys(invalidate)).toContainEqual(['item', 'MNT-1']);
  });

  describe('新通知的提示', () => {
    function setupWithHooks() {
      const queryClient = new QueryClient();
      const connection = createFakeLiveConnection();
      const notifications = { onConnected: vi.fn(), onInbox: vi.fn() };
      startLiveUpdates(queryClient, connection, { notifications });
      return { connection, notifications };
    }

    it('第一次連上、自動重連成功、被關掉之後重新連上，各通知一次「連上了」', async () => {
      // Arrange
      const { connection, notifications } = setupWithHooks();
      await vi.advanceTimersByTimeAsync(0);
      expect(notifications.onConnected).toHaveBeenCalledTimes(1);

      // Act
      connection.emitReconnected();
      connection.emitClosed();
      await vi.advanceTimersByTimeAsync(0);

      // Assert
      expect(notifications.onConnected).toHaveBeenCalledTimes(3);
    });

    it('收到 inbox 推送時通知一次，也照常讓通知的快取失效', async () => {
      // Arrange
      const queryClient = new QueryClient();
      const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
      const connection = createFakeLiveConnection();
      const notifications = { onConnected: vi.fn(), onInbox: vi.fn() };
      startLiveUpdates(queryClient, connection, { notifications });
      await vi.advanceTimersByTimeAsync(0);

      // Act
      connection.emitInbox();
      await vi.advanceTimersByTimeAsync(COALESCE_MS);

      // Assert
      expect(notifications.onInbox).toHaveBeenCalledTimes(1);
      expect(invalidatedKeys(invalidate)).toContainEqual(['notifications']);
    });

    it('第一次沒連上時，連上的那一刻才通知', async () => {
      // Arrange
      const { connection, notifications } = (() => {
        const queryClient = new QueryClient();
        const fake = createFakeLiveConnection();
        fake.failNextStarts(2);
        const hooks = { onConnected: vi.fn(), onInbox: vi.fn() };
        startLiveUpdates(queryClient, fake, { notifications: hooks });
        return { connection: fake, notifications: hooks };
      })();

      // Act
      await vi.advanceTimersByTimeAsync(0);
      const beforeConnected = notifications.onConnected.mock.calls.length;
      await vi.advanceTimersByTimeAsync(2_000);

      // Assert
      expect(connection.starts()).toBe(3);
      expect(beforeConnected).toBe(0);
      expect(notifications.onConnected).toHaveBeenCalledTimes(1);
    });
  });

  it('retryDelay：超過的次數一直是最後一個間隔', () => {
    expect([0, 1, 2, 3, 4, 5, 20].map((attempt) => retryDelay(attempt))).toEqual([0, 2_000, 5_000, 10_000, 30_000, 30_000, 30_000]);
  });
});
