import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyTheme, readTheme, saveTheme, THEME_STORAGE_KEY, useThemeChoice } from './theme';

afterEach(() => {
  document.documentElement.removeAttribute('data-theme');
});

describe('主題的選擇', () => {
  it('沒選過是跟著系統', () => {
    // Arrange：沒有任何設定

    // Act
    const choice = readTheme();

    // Assert
    expect(choice).toBe('system');
  });

  it('記在 localStorage 的 shell.theme，讀回來一樣', () => {
    // Arrange
    saveTheme('dark');

    // Act
    const choice = readTheme();

    // Assert
    expect(THEME_STORAGE_KEY).toBe('shell.theme');
    expect(localStorage.getItem('shell.theme')).toBe('dark');
    expect(choice).toBe('dark');
  });

  it('選回跟著系統時拿掉設定', () => {
    // Arrange
    saveTheme('light');

    // Act
    saveTheme('system');

    // Assert
    expect(localStorage.getItem('shell.theme')).toBeNull();
  });

  it('內容不認得或讀不到時當成跟著系統', () => {
    // Arrange
    localStorage.setItem('shell.theme', 'sepia');
    const unknown = readTheme();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act
    const blocked = readTheme();

    // Assert
    expect(unknown).toBe('system');
    expect(blocked).toBe('system');
  });

  it('寫不進去時不丟錯', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('quota');
    });

    // Act
    const save = () => {
      saveTheme('dark');
    };

    // Assert
    expect(save).not.toThrow();
  });

  it('套到 <html data-theme>；跟著系統時拿掉屬性', () => {
    // Arrange
    const root = document.documentElement;

    // Act
    applyTheme('light');
    const light = root.getAttribute('data-theme');
    applyTheme('system');

    // Assert
    expect(light).toBe('light');
    expect(root).not.toHaveAttribute('data-theme');
  });
});

describe('useThemeChoice', () => {
  it('從記住的設定開始；換了之後記住並套用', () => {
    // Arrange
    saveTheme('light');
    const { result } = renderHook(() => useThemeChoice());
    const initial = result.current[0];

    // Act
    act(() => {
      result.current[1]('dark');
    });

    // Assert
    expect(initial).toBe('light');
    expect(result.current[0]).toBe('dark');
    expect(localStorage.getItem('shell.theme')).toBe('dark');
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark');
  });
});
