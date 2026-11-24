import { act, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { SIDEBAR_STORAGE_KEY } from './sidebarPreference';

afterEach(() => {
  vi.unstubAllGlobals();
});

/** 窄螢幕：只有窄螢幕的查詢符合（其他媒體查詢一律不符合）。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

/** 可以改變寬度的窄螢幕：change(false) 模擬視窗變寬，通知每一個聽眾。 */
function stubResizableScreen() {
  let isCompact = true;
  const listeners = new Set<() => void>();
  vi.stubGlobal('matchMedia', (query: string) => ({
    get matches() {
      return query === COMPACT_QUERY && isCompact;
    },
    media: query,
    addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
  }));
  return {
    change(next: boolean) {
      isCompact = next;
      act(() => {
        listeners.forEach((listener) => {
          listener();
        });
      });
    },
  };
}

describe('寬螢幕的側欄收合', () => {
  it('按「側欄」側欄就收起來（aria-expanded 變成 false），再按一次回來；焦點留在按鈕上', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user } = renderApp('/my-work');
    await screen.findByRole('navigation', { name: '主要導覽' });
    const toggle = screen.getByRole('button', { name: '側欄' });
    const expandedAtFirst = toggle.getAttribute('aria-expanded');

    // Act
    await user.click(toggle);
    const navAfterCollapse = screen.queryByRole('navigation', { name: '主要導覽' });
    const expandedAfterCollapse = toggle.getAttribute('aria-expanded');
    const focusedAfterCollapse = document.activeElement;
    await user.click(toggle);

    // Assert
    expect(expandedAtFirst).toBe('true');
    expect(navAfterCollapse).not.toBeInTheDocument();
    expect(expandedAfterCollapse).toBe('false');
    expect(focusedAfterCollapse).toBe(toggle);
    expect(screen.getByRole('navigation', { name: '主要導覽' })).toBeInTheDocument();
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(toggle).toHaveFocus();
  });

  it('收起來記在這個瀏覽器，重新打開時照舊', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const first = renderApp('/my-work');
    await first.user.click(await screen.findByRole('button', { name: '側欄' }));
    first.unmount();

    // Act
    renderApp('/my-work');

    // Assert
    expect(localStorage.getItem(SIDEBAR_STORAGE_KEY)).toBe('collapsed');
    expect(await screen.findByRole('button', { name: '側欄' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('navigation', { name: '主要導覽' })).not.toBeInTheDocument();
  });

  it('窄螢幕不受收合影響：側欄照樣是「選單」打開的抽屜', async () => {
    // Arrange
    localStorage.setItem(SIDEBAR_STORAGE_KEY, 'collapsed');
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user } = renderApp('/my-work');

    // Act
    await user.click(await screen.findByRole('button', { name: '選單' }));

    // Assert
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    expect(within(nav).getByRole('link', { name: '我的工作' })).toHaveFocus();
  });
});

describe('窄螢幕的抽屜', () => {
  it('抽屜開著時視窗變寬就關上；再變窄也不會自己打開', async () => {
    // Arrange
    const screenSize = stubResizableScreen();
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user } = renderApp('/my-work');
    const menuButton = await screen.findByRole('button', { name: '選單' });
    await user.click(menuButton);
    const expandedWhileNarrow = menuButton.getAttribute('aria-expanded');

    // Act
    screenSize.change(false);
    const expandedAfterWiden = menuButton.getAttribute('aria-expanded');
    screenSize.change(true);

    // Assert
    expect(expandedWhileNarrow).toBe('true');
    expect(expandedAfterWiden).toBe('false');
    expect(menuButton).toHaveAttribute('aria-expanded', 'false');
  });
});
