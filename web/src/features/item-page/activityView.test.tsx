import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ACTIVITY_VIEW_STORAGE_KEY, readActivityView, setActivityView, useActivityView } from './activityView';

describe('時間軸看哪一份', () => {
  // 寫不進去時的暫存在記憶體裡，由 test/setup.ts 在每個測試後重設；這裡只還原 spy。
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('沒設定過是留言', () => {
    // Arrange / Act
    const value = readActivityView();

    // Assert
    expect(value).toBe('comments');
  });

  it('記在 localStorage 的 item.activityView，讀回來一樣', () => {
    // Arrange / Act
    setActivityView('all');

    // Assert
    expect(ACTIVITY_VIEW_STORAGE_KEY).toBe('item.activityView');
    expect(localStorage.getItem('item.activityView')).toBe('all');
    expect(readActivityView()).toBe('all');
  });

  it('內容不認得或讀不到時當成留言', () => {
    // Arrange
    localStorage.setItem('item.activityView', 'everything');
    const unknown = readActivityView();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act
    const blocked = readActivityView();

    // Assert
    expect(unknown).toBe('comments');
    expect(blocked).toBe('comments');
  });

  it('寫不進去也不丟錯，這次照樣生效', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { result } = renderHook(() => useActivityView());

    // Act
    act(() => {
      setActivityView('all');
    });

    // Assert
    expect(result.current).toBe('all');
  });

  it('同一頁有人切換，項目頁與面板立刻一起換', () => {
    // Arrange
    const page = renderHook(() => useActivityView());
    const panel = renderHook(() => useActivityView());

    // Act
    act(() => {
      setActivityView('all');
    });

    // Assert
    expect(page.result.current).toBe('all');
    expect(panel.result.current).toBe('all');
  });

  it('別的分頁改了（storage 事件），這一頁也跟著變', () => {
    // Arrange
    const { result } = renderHook(() => useActivityView());

    // Act
    act(() => {
      localStorage.setItem('item.activityView', 'all');
      window.dispatchEvent(new StorageEvent('storage', { key: 'item.activityView', newValue: 'all' }));
    });

    // Assert
    expect(result.current).toBe('all');
  });
});
