import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { readWeekStart, setWeekStart, useWeekStart, WEEK_START_STORAGE_KEY } from './weekStart';

describe('一週從哪天開始', () => {
  it('沒設定過是週一', () => {
    // Arrange / Act
    const value = readWeekStart();

    // Assert
    expect(value).toBe('monday');
  });

  it('記在 localStorage 的 shell.weekStart，讀回來一樣', () => {
    // Arrange / Act
    setWeekStart('sunday');

    // Assert
    expect(WEEK_START_STORAGE_KEY).toBe('shell.weekStart');
    expect(localStorage.getItem('shell.weekStart')).toBe('sunday');
    expect(readWeekStart()).toBe('sunday');
  });

  it('內容不認得或讀不到時當成週一', () => {
    // Arrange
    localStorage.setItem('shell.weekStart', 'friday');
    const unknown = readWeekStart();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act
    const blocked = readWeekStart();

    // Assert
    expect(unknown).toBe('monday');
    expect(blocked).toBe('monday');
  });

  it('寫不進去也不丟錯，這次照樣生效', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { result } = renderHook(() => useWeekStart());

    // Act
    act(() => {
      setWeekStart('sunday');
    });

    // Assert
    expect(result.current).toBe('sunday');
    vi.restoreAllMocks();
    act(() => {
      setWeekStart('monday');
    });
  });

  it('同一頁有人切換，用到的地方立刻跟著變', () => {
    // Arrange
    const first = renderHook(() => useWeekStart());
    const second = renderHook(() => useWeekStart());

    // Act
    act(() => {
      setWeekStart('sunday');
    });

    // Assert
    expect(first.result.current).toBe('sunday');
    expect(second.result.current).toBe('sunday');
  });

  it('別的分頁改了（storage 事件），這一頁也跟著變', () => {
    // Arrange
    const { result } = renderHook(() => useWeekStart());

    // Act
    act(() => {
      localStorage.setItem('shell.weekStart', 'sunday');
      window.dispatchEvent(new StorageEvent('storage', { key: 'shell.weekStart', newValue: 'sunday' }));
    });

    // Assert
    expect(result.current).toBe('sunday');
  });
});
