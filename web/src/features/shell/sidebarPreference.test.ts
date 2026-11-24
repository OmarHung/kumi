import { describe, expect, it, vi } from 'vitest';
import { readSidebarCollapsed, saveSidebarCollapsed, SIDEBAR_STORAGE_KEY } from './sidebarPreference';

describe('側欄收合的偏好', () => {
  it('沒有設定時是展開的', () => {
    // Arrange & Act
    const isCollapsed = readSidebarCollapsed();

    // Assert
    expect(isCollapsed).toBe(false);
  });

  it('收起來記在 shell.sidebar，展開就拿掉設定', () => {
    // Arrange & Act
    saveSidebarCollapsed(true);
    const afterCollapse = localStorage.getItem(SIDEBAR_STORAGE_KEY);
    const readBack = readSidebarCollapsed();
    saveSidebarCollapsed(false);

    // Assert
    expect(SIDEBAR_STORAGE_KEY).toBe('shell.sidebar');
    expect(afterCollapse).toBe('collapsed');
    expect(readBack).toBe(true);
    expect(localStorage.getItem(SIDEBAR_STORAGE_KEY)).toBeNull();
  });

  it('不認得的值當成展開', () => {
    // Arrange
    localStorage.setItem(SIDEBAR_STORAGE_KEY, 'tiny');

    // Act & Assert
    expect(readSidebarCollapsed()).toBe(false);
  });

  it('讀寫失敗時當成沒有設定，不丟出錯誤', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act & Assert
    expect(readSidebarCollapsed()).toBe(false);
    expect(() => {
      saveSidebarCollapsed(true);
    }).not.toThrow();
  });
});
