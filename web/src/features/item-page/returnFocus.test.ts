import { afterEach, describe, expect, it, vi } from 'vitest';
import { forgetReturnFocus, peekReturnFocus, rememberReturnFocus } from './returnFocus';

describe('回到清單時焦點要放回哪一筆', () => {
  afterEach(() => {
    sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it('以清單那一筆瀏覽紀錄為鍵記下編號，用過就拿掉', () => {
    // Arrange
    rememberReturnFocus({ returnTo: '/projects/AGY', fromEntry: 'entry-1' }, 'AGY-3');

    // Act & Assert
    expect(peekReturnFocus('entry-1')).toBe('AGY-3');
    expect(peekReturnFocus('entry-2')).toBeNull();
    forgetReturnFocus('entry-1');
    expect(peekReturnFocus('entry-1')).toBeNull();
  });

  it('同一筆紀錄記第二次是換掉', () => {
    // Arrange
    rememberReturnFocus({ fromEntry: 'entry-1' }, 'AGY-3');

    // Act
    rememberReturnFocus({ fromEntry: 'entry-1' }, 'AGY-4');

    // Assert
    expect(peekReturnFocus('entry-1')).toBe('AGY-4');
  });

  it('不是從清單連進來的（沒有 fromEntry）不記', () => {
    // Act
    rememberReturnFocus({ returnTo: '/projects/AGY' }, 'AGY-3');
    rememberReturnFocus(null, 'AGY-3');

    // Assert
    expect(sessionStorage.length).toBe(0);
  });

  it('一份文件的第一筆紀錄（React Router 的 key 恆為 default）不記也不放：重新載入後會撞到同一個鍵', () => {
    // Arrange
    rememberReturnFocus({ fromEntry: 'default' }, 'AGY-3');
    expect(sessionStorage.length).toBe(0);
    sessionStorage.setItem('item.returnFocus', JSON.stringify([['default', 'AGY-3']]));

    // Act & Assert
    expect(peekReturnFocus('default')).toBeNull();
  });

  it('只留最近的 20 筆', () => {
    // Act
    Array.from({ length: 25 }, (_, index) => index).forEach((index) => {
      rememberReturnFocus({ fromEntry: `entry-${String(index)}` }, `AGY-${String(index)}`);
    });

    // Assert
    expect(peekReturnFocus('entry-0')).toBeNull();
    expect(peekReturnFocus('entry-5')).toBe('AGY-5');
    expect(peekReturnFocus('entry-24')).toBe('AGY-24');
  });

  it('讀寫失敗當成沒有記錄', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act & Assert
    expect(() => {
      rememberReturnFocus({ fromEntry: 'entry-1' }, 'AGY-3');
    }).not.toThrow();
    expect(peekReturnFocus('entry-1')).toBeNull();
  });
});
