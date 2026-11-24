import { describe, expect, it, vi } from 'vitest';
import { clearRecentItems, MAX_RECENT_ITEMS, readRecentItems, recordRecentItem } from './recentItems';

const STORAGE_KEY = 'jump.recentItems';

function recent(number: number) {
  return { key: `AGY-${String(number)}`, projectKey: 'AGY', title: `項目 ${String(number)}` };
}

describe('最近看過的項目', () => {
  it('沒有記錄時是空的', () => {
    // Arrange、Act
    const items = readRecentItems();

    // Assert
    expect(items).toEqual([]);
  });

  it('最新的排在最前面', () => {
    // Arrange
    recordRecentItem(recent(1));
    recordRecentItem(recent(2));

    // Act
    const items = readRecentItems();

    // Assert
    expect(items.map((item) => item.key)).toEqual(['AGY-2', 'AGY-1']);
  });

  it('再看一次同一個項目：移到最前面、不重複，標題換成新的', () => {
    // Arrange
    recordRecentItem(recent(1));
    recordRecentItem(recent(2));

    // Act
    recordRecentItem({ ...recent(1), title: '改過的標題' });

    // Assert
    expect(readRecentItems()).toEqual([{ ...recent(1), title: '改過的標題' }, recent(2)]);
  });

  it(`最多留 ${String(MAX_RECENT_ITEMS)} 個，最舊的先丟掉`, () => {
    // Arrange
    const numbers = Array.from({ length: MAX_RECENT_ITEMS + 2 }, (_, index) => index + 1);

    // Act
    numbers.forEach((number) => {
      recordRecentItem(recent(number));
    });

    // Assert
    const keys = readRecentItems().map((item) => item.key);
    expect(keys).toHaveLength(MAX_RECENT_ITEMS);
    expect(keys[0]).toBe(`AGY-${String(MAX_RECENT_ITEMS + 2)}`);
    expect(keys).not.toContain('AGY-1');
    expect(keys).not.toContain('AGY-2');
  });

  it('存的內容壞掉或不是清單時當成沒有', () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, '{壞掉的');
    const broken = readRecentItems();
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ key: 'AGY-1' }));

    // Act
    const notList = readRecentItems();

    // Assert
    expect(broken).toEqual([]);
    expect(notList).toEqual([]);
  });

  it('清單裡形狀不對的項目略過，其他的照樣讀得到', () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, JSON.stringify([recent(1), { key: 'AGY-2' }, null, 'AGY-3', recent(4)]));

    // Act
    const items = readRecentItems();

    // Assert
    expect(items).toEqual([recent(1), recent(4)]);
  });

  it('讀不到 localStorage 時當成沒有', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });

    // Act
    const items = readRecentItems();

    // Assert
    expect(items).toEqual([]);
  });

  it('clearRecentItems 清掉全部（登出用），別的鍵照樣留著', () => {
    // Arrange
    recordRecentItem(recent(1));
    localStorage.setItem('shell.theme', 'dark');

    // Act
    clearRecentItems();

    // Assert
    expect(readRecentItems()).toEqual([]);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(localStorage.getItem('shell.theme')).toBe('dark');
  });

  it('清不掉（localStorage 被擋）時不丟錯', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });

    // Act
    const clear = () => {
      clearRecentItems();
    };

    // Assert
    expect(clear).not.toThrow();
  });

  it('存不了（容量滿、私密瀏覽）時不丟錯', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });

    // Act
    const record = () => {
      recordRecentItem(recent(1));
    };

    // Assert
    expect(record).not.toThrow();
  });
});
