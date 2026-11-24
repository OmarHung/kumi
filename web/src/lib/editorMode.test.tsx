import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  EDITOR_MODE_STORAGE_KEY,
  readEditorMode,
  resetEditorModeForTests,
  setEditorMode,
  useEditorMode,
} from './editorMode';

afterEach(() => {
  vi.restoreAllMocks();
  resetEditorModeForTests();
});

describe('編輯方式', () => {
  it('沒設定過是所見即所得', () => {
    // Arrange / Act
    const value = readEditorMode();

    // Assert
    expect(value).toBe('rich');
  });

  it('記在 localStorage 的 editor.mode，讀回來一樣', () => {
    // Arrange / Act
    setEditorMode('markdown');

    // Assert
    expect(EDITOR_MODE_STORAGE_KEY).toBe('editor.mode');
    expect(localStorage.getItem('editor.mode')).toBe('markdown');
    expect(readEditorMode()).toBe('markdown');
  });

  it('內容不認得或讀不到時當成所見即所得', () => {
    // Arrange
    localStorage.setItem('editor.mode', 'source');
    const unknown = readEditorMode();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act
    const blocked = readEditorMode();

    // Assert
    expect(unknown).toBe('rich');
    expect(blocked).toBe('rich');
  });

  it('寫不進去也不丟錯，這次照樣生效', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { result } = renderHook(() => useEditorMode());

    // Act
    act(() => {
      setEditorMode('markdown');
    });

    // Assert
    expect(result.current).toBe('markdown');
  });

  it('同一頁有人切換，用到的地方立刻跟著變；別的分頁改了也會跟著變', () => {
    // Arrange
    const first = renderHook(() => useEditorMode());
    const second = renderHook(() => useEditorMode());

    // Act
    act(() => {
      setEditorMode('markdown');
    });
    const afterSwitch = [first.result.current, second.result.current];
    act(() => {
      localStorage.setItem('editor.mode', 'rich');
      window.dispatchEvent(new StorageEvent('storage', { key: 'editor.mode' }));
    });

    // Assert
    expect(afterSwitch).toEqual(['markdown', 'markdown']);
    expect([first.result.current, second.result.current]).toEqual(['rich', 'rich']);
  });
});
