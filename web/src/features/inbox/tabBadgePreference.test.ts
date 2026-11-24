import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_TAB_BADGE } from './faviconBadge';
import { currentTabBadge, readTabBadge, setTabBadge, TAB_BADGE_STORAGE_KEY, updateTabBadge, useTabBadge } from './tabBadgePreference';

describe('分頁圖示圓點的偏好', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('沒有存過時是預設（右上、中、紅）', () => {
    // Arrange & Act
    const badge = readTabBadge();

    // Assert
    expect(badge).toEqual({ position: 'top-right', size: 'medium', color: 'red', customColor: null });
    expect(badge).toEqual(DEFAULT_TAB_BADGE);
  });

  it('存的內容讀不懂時當成預設，認得的欄位照樣用', () => {
    // Arrange
    localStorage.setItem(TAB_BADGE_STORAGE_KEY, '{不是 JSON');
    const broken = readTabBadge();
    localStorage.setItem(TAB_BADGE_STORAGE_KEY, JSON.stringify({ position: 'bottom-left', size: 'huge', color: 42 }));

    // Act
    const partial = readTabBadge();

    // Assert
    expect(broken).toEqual(DEFAULT_TAB_BADGE);
    expect(partial).toEqual({ ...DEFAULT_TAB_BADGE, position: 'bottom-left' });
  });

  it('改了記在這個瀏覽器，讀回來是同一份', () => {
    // Arrange
    const badge = { position: 'bottom-right', size: 'large', color: 'custom', customColor: '#0a0b0c' } as const;

    // Act
    setTabBadge(badge);

    // Assert
    expect(JSON.parse(localStorage.getItem(TAB_BADGE_STORAGE_KEY) ?? 'null')).toEqual(badge);
    expect(readTabBadge()).toEqual(badge);
  });

  it('存的自訂色碼整理成小寫的六碼；不合格式時丟掉，選的是自訂就退回預設的顏色', () => {
    // Arrange
    localStorage.setItem(TAB_BADGE_STORAGE_KEY, JSON.stringify({ color: 'custom', customColor: 'ABC' }));
    const shorthand = readTabBadge();
    localStorage.setItem(TAB_BADGE_STORAGE_KEY, JSON.stringify({ color: 'custom', customColor: 'url(#x)' }));

    // Act
    const invalid = readTabBadge();

    // Assert
    expect(shorthand).toEqual({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '#aabbcc' });
    expect(invalid).toEqual(DEFAULT_TAB_BADGE);
  });

  it('updateTabBadge 只改給的欄位，以當下存的為準', () => {
    // Arrange
    setTabBadge({ position: 'top-left', size: 'small', color: 'green', customColor: null });

    // Act
    updateTabBadge({ color: 'custom', customColor: '#112233' });
    updateTabBadge({ customColor: '#445566' });

    // Assert
    expect(readTabBadge()).toEqual({ position: 'top-left', size: 'small', color: 'custom', customColor: '#445566' });
  });

  it('updateTabBadge 選成自訂卻沒有色碼時退回預設的顏色（和重新載入時讀到的一樣）', () => {
    // Arrange
    setTabBadge({ ...DEFAULT_TAB_BADGE, color: 'green' });

    // Act
    updateTabBadge({ color: 'custom' });

    // Assert
    expect(currentTabBadge()).toEqual(DEFAULT_TAB_BADGE);
    expect(readTabBadge()).toEqual(DEFAULT_TAB_BADGE);
  });

  it('updateTabBadge 收到不合格式的色碼時不存', () => {
    // Arrange
    setTabBadge({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '#112233' });

    // Act
    updateTabBadge({ customColor: 'not-a-color' });

    // Assert
    expect(readTabBadge().customColor).toBe('#112233');
  });

  it('連續讀兩次是同一個物件（畫面訂閱時不會一直重畫）', () => {
    // Arrange
    setTabBadge({ ...DEFAULT_TAB_BADGE, color: 'green' });

    // Act
    const first = currentTabBadge();
    const second = currentTabBadge();

    // Assert
    expect(second).toBe(first);
  });

  it('寫不進去時（私密瀏覽）這一頁照樣生效', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });

    // Act
    setTabBadge({ ...DEFAULT_TAB_BADGE, size: 'small' });

    // Assert
    expect(currentTabBadge()).toEqual({ ...DEFAULT_TAB_BADGE, size: 'small' });
  });

  it('寫不進去之後，別的分頁寫進去了照樣跟著換', () => {
    // Arrange
    const { result } = renderHook(() => useTabBadge());
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    act(() => {
      setTabBadge({ ...DEFAULT_TAB_BADGE, size: 'small' });
    });
    setItem.mockRestore();

    // Act
    act(() => {
      localStorage.setItem(TAB_BADGE_STORAGE_KEY, JSON.stringify({ ...DEFAULT_TAB_BADGE, color: 'yellow' }));
      window.dispatchEvent(new StorageEvent('storage', { key: TAB_BADGE_STORAGE_KEY }));
    });

    // Assert
    expect(result.current).toEqual({ ...DEFAULT_TAB_BADGE, color: 'yellow' });
  });

  it('別的分頁改了，這個分頁跟著換', () => {
    // Arrange
    const { result } = renderHook(() => useTabBadge());
    const before = result.current;

    // Act
    act(() => {
      localStorage.setItem(TAB_BADGE_STORAGE_KEY, JSON.stringify({ ...DEFAULT_TAB_BADGE, color: 'pink' }));
      window.dispatchEvent(new StorageEvent('storage', { key: TAB_BADGE_STORAGE_KEY }));
    });

    // Assert
    expect(before).toEqual(DEFAULT_TAB_BADGE);
    expect(result.current).toEqual({ ...DEFAULT_TAB_BADGE, color: 'pink' });
  });
});
