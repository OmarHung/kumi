import { act, renderHook } from '@testing-library/react';
import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { badgedFaviconUrl, DEFAULT_TAB_BADGE } from './faviconBadge';
import { setTabBadge } from './tabBadgePreference';
import { useFaviconBadge } from './useFaviconBadge';

const FAVICON_URL = '/favicon.svg';

describe('useFaviconBadge', () => {
  let icon: HTMLLinkElement;

  beforeEach(() => {
    icon = document.createElement('link');
    icon.rel = 'icon';
    icon.setAttribute('href', FAVICON_URL);
    document.head.append(icon);
  });

  afterEach(() => {
    icon.remove();
  });

  it('有未讀時換成帶圓點的圖示，讀完了換回原本的', () => {
    // Arrange
    const { rerender } = renderHook(({ hasUnread }) => { useFaviconBadge(hasUnread); }, {
      initialProps: { hasUnread: true },
    });
    const whileUnread = icon.getAttribute('href');

    // Act
    rerender({ hasUnread: false });

    // Assert
    expect(whileUnread).toBe(badgedFaviconUrl(DEFAULT_TAB_BADGE));
    expect(icon).toHaveAttribute('href', FAVICON_URL);
  });

  it('圓點照設定畫，改了設定馬上換', () => {
    // Arrange
    setTabBadge({ position: 'bottom-left', size: 'small', color: 'green', customColor: null });
    renderHook(() => { useFaviconBadge(true); });
    const before = icon.getAttribute('href');

    // Act
    act(() => {
      setTabBadge({ position: 'top-left', size: 'large', color: 'blue', customColor: null });
    });

    // Assert
    expect(before).toBe(badgedFaviconUrl({ position: 'bottom-left', size: 'small', color: 'green', customColor: null }));
    expect(icon).toHaveAttribute('href', badgedFaviconUrl({ position: 'top-left', size: 'large', color: 'blue', customColor: null }));
  });

  it('換過圓點的樣子之後讀完，回到原本的圖示而不是上一個帶圓點的', () => {
    // Arrange
    const { rerender } = renderHook(({ hasUnread }) => { useFaviconBadge(hasUnread); }, {
      initialProps: { hasUnread: true },
    });
    act(() => {
      setTabBadge({ position: 'bottom-right', size: 'large', color: 'pink', customColor: null });
    });

    // Act
    rerender({ hasUnread: false });

    // Assert
    expect(icon).toHaveAttribute('href', FAVICON_URL);
  });

  it('StrictMode 掛上、清掉、再掛上之後，卸下時照樣回到原本的圖示', () => {
    // Arrange
    const { unmount } = renderHook(() => { useFaviconBadge(true); }, { wrapper: StrictMode });
    const whileUnread = icon.getAttribute('href');

    // Act
    unmount();

    // Assert
    expect(whileUnread).toBe(badgedFaviconUrl(DEFAULT_TAB_BADGE));
    expect(icon).toHaveAttribute('href', FAVICON_URL);
  });

  it('頁面上有好幾個圖示的 link 時每一個都換，再各自換回原本的', () => {
    // Arrange
    const png = document.createElement('link');
    png.rel = 'icon';
    png.setAttribute('href', '/favicon-32.png');
    document.head.append(png);
    const { unmount } = renderHook(() => { useFaviconBadge(true); });
    const whileUnread = [icon.getAttribute('href'), png.getAttribute('href')];

    // Act
    unmount();

    // Assert
    expect(whileUnread).toEqual([badgedFaviconUrl(DEFAULT_TAB_BADGE), badgedFaviconUrl(DEFAULT_TAB_BADGE)]);
    expect(icon).toHaveAttribute('href', FAVICON_URL);
    expect(png).toHaveAttribute('href', '/favicon-32.png');
    png.remove();
  });

  it('沒有未讀時不動圖示', () => {
    // Arrange & Act
    renderHook(() => { useFaviconBadge(false); });

    // Assert
    expect(icon).toHaveAttribute('href', FAVICON_URL);
  });

  it('離開登入後的外框（登出）時換回原本的圖示', () => {
    // Arrange
    const { unmount } = renderHook(() => { useFaviconBadge(true); });

    // Act
    unmount();

    // Assert
    expect(icon).toHaveAttribute('href', FAVICON_URL);
  });

  it('頁面上沒有圖示的 link 時什麼都不做', () => {
    // Arrange
    icon.remove();

    // Act
    const render = () => renderHook(() => { useFaviconBadge(true); });

    // Assert
    expect(render).not.toThrow();
    expect(document.head.querySelector('link[rel~="icon"]')).toBeNull();
  });
});
