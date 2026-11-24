import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { renderApp } from '../../test/renderApp';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('/design 的 SidePanel 展示', () => {
  it('點一列開面板、Alt+↓ 換下一個、Esc 關上後焦點回到清單上那一筆', async () => {
    // Arrange
    const { user } = renderApp('/design');
    await screen.findByRole('heading', { level: 1, name: '設計元件' });
    const rows = screen.getByRole('table', { name: '展示的項目（fill）' });

    // Act
    await user.click(within(rows).getByRole('button', { name: '登入頁改版' }));
    const panel = screen.getByRole('complementary', { name: '項目 AGY-11' });
    const focusedHeading = document.activeElement;
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    const nextTitle = within(panel).getByRole('heading', { level: 2 }).textContent;
    await user.keyboard('{Escape}');

    // Assert
    expect(focusedHeading).toBe(within(panel).getByRole('heading', { level: 2 }));
    expect(nextTitle).toBe('AGY-12');
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
    expect(within(rows).getByRole('button', { name: '付款流程' })).toHaveFocus();
  });

  it('面板裡的選單、上一個／下一個按鈕、關閉按鈕', async () => {
    // Arrange
    stubIntersectionObserver();
    const { user } = renderApp('/design');
    await screen.findByRole('heading', { level: 1, name: '設計元件' });
    const rows = screen.getByRole('table', { name: '展示的項目（page）' });
    await user.click(within(rows).getByRole('button', { name: '付款流程' }));
    const panel = screen.getByRole('complementary', { name: '項目 AGY-12' });

    // Act
    await user.click(within(panel).getByRole('button', { name: '更多動作' }));
    const isMenuOpen = within(panel).queryByRole('menu') !== null;
    await user.keyboard('{Escape}');
    await user.click(within(panel).getByRole('button', { name: '上一個項目' }));
    await user.click(within(panel).getByRole('button', { name: '上一個項目' }));
    const title = within(panel).getByRole('heading', { level: 2 }).textContent;
    await user.click(within(panel).getByRole('button', { name: '關閉' }));

    // Assert
    expect(isMenuOpen).toBe(true);
    expect(title).toBe('AGY-11');
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
  });
});
