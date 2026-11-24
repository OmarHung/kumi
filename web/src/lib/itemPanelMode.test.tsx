import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ITEM_PANEL_MODE_STORAGE_KEY,
  readItemPanelMode,
  resetItemPanelModeForTests,
  setItemPanelMode,
  useItemPanelMode,
} from './itemPanelMode';

afterEach(() => {
  vi.restoreAllMocks();
  resetItemPanelModeForTests();
});

describe('項目面板的樣子', () => {
  it('沒設定過是側邊面板', () => {
    // Arrange / Act
    const value = readItemPanelMode();

    // Assert
    expect(value).toBe('side');
  });

  it('記在 localStorage 的 shell.itemPanelMode，讀回來一樣', () => {
    // Arrange / Act
    setItemPanelMode('modal');

    // Assert
    expect(ITEM_PANEL_MODE_STORAGE_KEY).toBe('shell.itemPanelMode');
    expect(localStorage.getItem('shell.itemPanelMode')).toBe('modal');
    expect(readItemPanelMode()).toBe('modal');
  });

  it('內容不認得或讀不到時當成側邊面板', () => {
    // Arrange
    localStorage.setItem('shell.itemPanelMode', 'fullscreen');
    const unknown = readItemPanelMode();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act
    const blocked = readItemPanelMode();

    // Assert
    expect(unknown).toBe('side');
    expect(blocked).toBe('side');
  });

  it('寫不進去也不丟錯，這次照樣生效', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { result } = renderHook(() => useItemPanelMode());

    // Act
    act(() => {
      setItemPanelMode('modal');
    });

    // Assert
    expect(result.current).toBe('modal');
  });

  it('同一頁有人切換，用到的地方立刻跟著變；別的分頁改了也會跟著變', () => {
    // Arrange
    const first = renderHook(() => useItemPanelMode());
    const second = renderHook(() => useItemPanelMode());

    // Act
    act(() => {
      setItemPanelMode('modal');
    });
    const afterSwitch = [first.result.current, second.result.current];
    act(() => {
      localStorage.setItem('shell.itemPanelMode', 'side');
      window.dispatchEvent(new StorageEvent('storage', { key: 'shell.itemPanelMode' }));
    });

    // Assert
    expect(afterSwitch).toEqual(['modal', 'modal']);
    expect([first.result.current, second.result.current]).toEqual(['side', 'side']);
  });
});
