import { describe, expect, it, vi } from 'vitest';
import type { Notification } from '../../api/types';
import { flushPending } from '../../test/flush';
import { notification } from '../../test/notifications';
import { createToastWatcher } from './toastWatcher';

const at = (minute: number) => `2026-11-22T08:${String(minute).padStart(2, '0')}:00Z`;

function setup(
  initial: readonly Notification[],
  overrides: { canShow?: () => boolean; isItemOpen?: (key: string) => boolean; onFresh?: (entries: readonly Notification[]) => void } = {},
) {
  let unread = [...initial];
  const fetchUnread = vi.fn(() => Promise.resolve(unread));
  const onFresh = vi.fn<(entries: readonly Notification[]) => void>(overrides.onFresh);
  const reportError = vi.fn<(error: unknown) => void>();
  const watcher = createToastWatcher({
    fetchUnread,
    canShow: overrides.canShow ?? (() => true),
    isItemOpen: overrides.isItemOpen ?? (() => false),
    onFresh,
    reportError,
  });
  return {
    watcher,
    reportError,
    fetchUnread,
    onFresh,
    serve: (next: readonly Notification[]) => {
      unread = [...next];
    },
  };
}

describe('新通知的觀察', () => {
  it('開始時讀到的未讀不跳，之後新來的跳', async () => {
    // Arrange
    const old = notification({ createdAt: at(1) });
    const { watcher, onFresh, serve } = setup([old]);
    watcher.start();
    await flushPending();
    const incoming = notification({ createdAt: at(2) });
    serve([incoming, old]);

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).toHaveBeenCalledTimes(1);
    expect(onFresh).toHaveBeenCalledWith([incoming]);
  });

  it('標成已讀讓更舊的未讀浮出來時不跳', async () => {
    // Arrange
    const newest = notification({ createdAt: at(9) });
    const older = notification({ createdAt: at(1) });
    const { watcher, onFresh, serve } = setup([newest]);
    watcher.start();
    await flushPending();
    serve([older]);

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).not.toHaveBeenCalled();
  });

  it('留言被刪除、它的通知跟著不見（收到 inbox 但只是少了通知）：不跳，剩下的更舊的未讀也不跳', async () => {
    // Arrange
    const newest = notification({ createdAt: at(9) });
    const older = notification({ createdAt: at(5) });
    const oldest = notification({ createdAt: at(1) });
    const { watcher, onFresh, serve } = setup([newest, older, oldest]);
    watcher.start();
    await flushPending();

    // Act
    serve([older, oldest]);
    watcher.check();
    await flushPending();
    serve([]);
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).not.toHaveBeenCalled();
  });

  it('通知少了之後，真的新來的照樣跳', async () => {
    // Arrange
    const first = notification({ createdAt: at(5) });
    const { watcher, onFresh, serve } = setup([first]);
    watcher.start();
    await flushPending();
    serve([]);
    watcher.check();
    await flushPending();
    const incoming = notification({ createdAt: at(9) });
    serve([incoming]);

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).toHaveBeenCalledTimes(1);
    expect(onFresh).toHaveBeenCalledWith([incoming]);
  });

  it('開始時沒有未讀：第一則新的就跳', async () => {
    // Arrange
    const { watcher, onFresh, serve } = setup([]);
    watcher.start();
    await flushPending();
    const incoming = notification({ createdAt: at(1) });
    serve([incoming]);

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).toHaveBeenCalledWith([incoming]);
  });

  it('重連之後重新開始：斷線期間來的不補', async () => {
    // Arrange
    const { watcher, onFresh, serve } = setup([]);
    watcher.start();
    await flushPending();
    const missed = notification({ createdAt: at(1) });
    serve([missed]);

    // Act
    watcher.start();
    await flushPending();
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).not.toHaveBeenCalled();
  });

  it('不能跳的時候（分頁看不見、在收件匣、不顯示）照樣記下看過了，之後能跳時不補', async () => {
    // Arrange
    let canShow = false;
    const { watcher, onFresh, serve } = setup([], { canShow: () => canShow });
    watcher.start();
    await flushPending();
    const missed = notification({ createdAt: at(1) });
    serve([missed]);
    watcher.check();
    await flushPending();
    canShow = true;

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).not.toHaveBeenCalled();
  });

  it('項目正開著的那一則不跳，其他的照跳', async () => {
    // Arrange
    const { watcher, onFresh, serve } = setup([], { isItemOpen: (key) => key === 'SMK-7' });
    watcher.start();
    await flushPending();
    const open = notification({ itemKey: 'SMK-7', createdAt: at(1) });
    const other = notification({ itemKey: 'SMK-8', createdAt: at(2) });
    serve([other, open]);

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).toHaveBeenCalledWith([other]);
  });

  it('連續的推送在讀的時候合併成一次', async () => {
    // Arrange
    const { watcher, fetchUnread } = setup([]);
    watcher.start();
    await flushPending();
    fetchUnread.mockClear();

    // Act
    watcher.check();
    watcher.check();
    watcher.check();
    await flushPending();

    // Assert
    expect(fetchUnread).toHaveBeenCalledTimes(1);
  });

  it('開始時讀失敗：第一次讀到的當作基準，不跳', async () => {
    // Arrange
    const existing = notification({ createdAt: at(1) });
    const { watcher, onFresh, fetchUnread } = setup([existing]);
    fetchUnread.mockRejectedValueOnce(new Error('連不上'));
    watcher.start();
    await flushPending();

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(onFresh).not.toHaveBeenCalled();
  });

  it('停用之後不再讀也不跳', async () => {
    // Arrange
    const { watcher, onFresh, fetchUnread } = setup([]);
    watcher.setActive(false);

    // Act
    watcher.start();
    watcher.check();
    await flushPending();

    // Assert
    expect(fetchUnread).not.toHaveBeenCalled();
    expect(onFresh).not.toHaveBeenCalled();
  });

  it('onFresh 丟例外：回報，之後的推送照樣能跳', async () => {
    // Arrange
    let shouldThrow = true;
    const { watcher, onFresh, reportError, serve } = setup([], {
      onFresh: () => {
        if (shouldThrow) throw new Error('壞掉了');
      },
    });
    watcher.start();
    await flushPending();
    const first = notification({ createdAt: at(1) });
    serve([first]);
    watcher.check();
    await flushPending();
    shouldThrow = false;
    const second = notification({ createdAt: at(2) });
    serve([second, first]);

    // Act
    watcher.check();
    await flushPending();

    // Assert
    expect(reportError).toHaveBeenCalledTimes(1);
    expect(onFresh).toHaveBeenCalledTimes(2);
    expect(onFresh).toHaveBeenLastCalledWith([second]);
  });

  it('讀的時候重連：重連之後的那一次才是基準，斷線期間來的不補', async () => {
    // Arrange
    const known = notification({ createdAt: at(1) });
    const missed = notification({ createdAt: at(2) });
    const resolvers: ((entries: readonly Notification[]) => void)[] = [];
    const fetchUnread = vi.fn(() => new Promise<readonly Notification[]>((resolve) => { resolvers.push(resolve); }));
    const onFresh = vi.fn<(entries: readonly Notification[]) => void>();
    const watcher = createToastWatcher({ fetchUnread, canShow: () => true, isItemOpen: () => false, onFresh, reportError: vi.fn() });
    watcher.start();
    await flushPending();
    resolvers[0]?.([known]);
    await flushPending();
    watcher.check();
    await flushPending();

    // Act
    watcher.start();
    resolvers[1]?.([known]);
    await flushPending();
    resolvers[2]?.([missed, known]);
    await flushPending();
    watcher.check();
    await flushPending();
    resolvers[3]?.([missed, known]);
    await flushPending();

    // Assert
    expect(onFresh).not.toHaveBeenCalled();
  });
});
