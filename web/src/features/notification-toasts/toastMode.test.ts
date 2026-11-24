import { afterEach, describe, expect, it, vi } from 'vitest';
import { currentToastMode, readToastMode, setToastMode, TOAST_MODE_STORAGE_KEY } from './toastMode';

describe('通知提示的顯示偏好', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('沒有設定時是完整', () => {
    // Arrange & Act
    const mode = readToastMode();

    // Assert
    expect(mode).toBe('full');
  });

  it('設定記在這個瀏覽器的 notifications.toastMode，讀回來一樣', () => {
    // Arrange & Act
    setToastMode('compact');

    // Assert
    expect(localStorage.getItem(TOAST_MODE_STORAGE_KEY)).toBe('compact');
    expect(readToastMode()).toBe('compact');
  });

  it('不認得的值當成預設', () => {
    // Arrange
    localStorage.setItem(TOAST_MODE_STORAGE_KEY, 'loud');

    // Act & Assert
    expect(readToastMode()).toBe('full');
  });

  it('讀寫失敗時當成預設、不丟錯誤，這一頁照樣用新的設定', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act & Assert
    expect(readToastMode()).toBe('full');
    expect(() => {
      setToastMode('off');
    }).not.toThrow();
    expect(currentToastMode()).toBe('off');
  });
});
