import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  PROJECT_VIEW_STORAGE_PREFIX,
  projectPageLink,
  readProjectView,
  saveProjectView,
  useProjectPageLink,
} from './projectViewPreference';

describe('每個專案上次用的檢視', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('記下看板，下次連到這個專案就開看板；其他專案不受影響', () => {
    // Act
    saveProjectView('AGY', 'board');

    // Assert
    expect(localStorage.getItem(`${PROJECT_VIEW_STORAGE_PREFIX}AGY`)).toBe('board');
    expect(readProjectView('AGY')).toBe('board');
    expect(readProjectView('SMK')).toBe('table');
    expect(projectPageLink('AGY')).toBe('/projects/AGY?view=board');
    expect(projectPageLink('SMK')).toBe('/projects/SMK');
  });

  it('換回表格就拿掉記錄', () => {
    // Arrange
    saveProjectView('AGY', 'board');

    // Act
    saveProjectView('AGY', 'table');

    // Assert
    expect(localStorage.getItem(`${PROJECT_VIEW_STORAGE_PREFIX}AGY`)).toBeNull();
    expect(readProjectView('AGY')).toBe('table');
  });

  it('行事曆與時間軸也記得，連結照它開', () => {
    // Act
    saveProjectView('AGY', 'calendar');
    saveProjectView('SMK', 'timeline');

    // Assert
    expect(readProjectView('AGY')).toBe('calendar');
    expect(projectPageLink('AGY')).toBe('/projects/AGY?view=calendar');
    expect(projectPageLink('SMK')).toBe('/projects/SMK?view=timeline');
  });

  it('內容不認得的當成表格', () => {
    // Arrange
    localStorage.setItem(`${PROJECT_VIEW_STORAGE_PREFIX}AGY`, 'gantt');

    // Act & Assert
    expect(readProjectView('AGY')).toBe('table');
  });

  it('讀寫失敗當成沒有記錄，不丟錯', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act & Assert
    expect(() => {
      saveProjectView('AGY', 'board');
    }).not.toThrow();
    expect(readProjectView('AGY')).toBe('table');
  });

  it('連結跟著換：記下新的檢視之後，用它的元件重新渲染', () => {
    // Arrange
    const { result } = renderHook(() => useProjectPageLink('AGY'));
    expect(result.current).toBe('/projects/AGY');

    // Act
    act(() => {
      saveProjectView('AGY', 'board');
    });

    // Assert
    expect(result.current).toBe('/projects/AGY?view=board');
  });
});
