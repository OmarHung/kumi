import { describe, expect, it, vi } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { startLiveUpdates } from './startLiveUpdates';

const report = vi.hoisted(() => vi.fn());

vi.mock('../../lib/errorReporting', () => ({ pageErrorReporter: () => ({ report, reportMessage: vi.fn() }) }));
// 函式庫載入失敗（部署新版之後舊分頁的檔名已經不存在）。
vi.mock('@microsoft/signalr', () => {
  throw new Error('Failed to fetch dynamically imported module');
});

const { createSignalRConnection, MAX_LOAD_FAILURES } = await import('./signalRConnection');

describe('載入 SignalR 失敗', () => {
  it('連續失敗 3 次之後 start 丟出放棄的錯誤，並用網頁錯誤回報記一筆', async () => {
    // Arrange
    const connection = createSignalRConnection();

    // Act
    const results = await Promise.allSettled(Array.from({ length: MAX_LOAD_FAILURES }, () => connection.start()));

    // Assert
    expect(results.map((result) => (result.status === 'rejected' ? (result.reason as Error).name : 'ok'))).toEqual([
      'Error',
      'Error',
      'LiveUnavailableError',
    ]);
    expect(report).toHaveBeenCalledOnce();
    expect(report).toHaveBeenCalledWith('error', expect.any(Error));
  });

  it('放棄之後 startLiveUpdates 不再重試，也不動登入狀態', async () => {
    // Arrange
    vi.useFakeTimers();
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const connection = createSignalRConnection();
    const starts = vi.spyOn(connection, 'start');

    // Act
    startLiveUpdates(queryClient, connection);
    await vi.advanceTimersByTimeAsync(300_000);
    vi.useRealTimers();

    // Assert：0、2 秒各再試一次，第三次放棄
    expect(starts.mock.calls.length).toBeLessThanOrEqual(3);
    expect(invalidate).not.toHaveBeenCalled();
  });
});
