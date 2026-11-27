import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { BELOW_WIDE_QUERY } from '../../lib/useMediaQuery';

afterEach(() => {
  vi.unstubAllGlobals();
});

/** 平板直向（48～64rem）：只有「不到 64rem」的查詢符合。 */
function stubTabletScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === BELOW_WIDE_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

async function findRail() {
  return screen.findByRole('navigation', { name: '主要導覽' });
}

describe('圖示列', () => {
  it('寬螢幕沒有圖示列，只有側欄的導覽', async () => {
    // Arrange
    signedInAs(exampleOwner);

    // Act
    renderApp('/my-work');

    // Assert
    const nav = await findRail();
    expect(within(nav).getByRole('link', { name: '我的工作' })).toBeInTheDocument();
    expect(screen.getAllByRole('navigation', { name: '主要導覽' })).toHaveLength(1);
    expect(screen.queryByRole('button', { name: '搜尋' })).not.toBeInTheDocument();
  });

  it('48～64rem 有圖示列，各格的名稱與連結', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner);

    // Act
    renderApp('/my-work');

    // Assert
    const rail = await findRail();
    expect(within(rail).getByRole('link', { name: 'Kumi' })).toHaveAttribute('href', '/');
    expect(within(rail).getByRole('button', { name: '搜尋' })).toBeInTheDocument();
    expect(within(rail).getByRole('link', { name: '我的工作' })).toHaveAttribute('href', '/my-work');
    expect(within(rail).getByRole('link', { name: '專案總覽' })).toHaveAttribute('href', '/overview');
    expect(within(rail).getByRole('link', { name: '收件匣' })).toHaveAttribute('href', '/inbox');
    expect(within(rail).getByRole('link', { name: '知識庫' })).toHaveAttribute('href', '/knowledge');
    expect(within(rail).getByRole('button', { name: '專案' })).toBeInTheDocument();
    expect(within(rail).getByRole('link', { name: '設定' })).toHaveAttribute('href', '/account');
    expect(within(rail).getByRole('button', { name: /^.+（.+）$/ })).toBeInTheDocument();
    expect(within(rail).queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
    expect(within(rail).queryByRole('link', { name: '人員' })).not.toBeInTheDocument();
  });

  it('目前所在的那一格標 aria-current', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner);

    // Act
    renderApp('/overview');

    // Assert
    const rail = await findRail();
    expect(within(rail).getByRole('link', { name: '專案總覽' })).toHaveAttribute('aria-current', 'page');
    expect(within(rail).getByRole('link', { name: '我的工作' })).not.toHaveAttribute('aria-current');
  });

  it('在設定中心的頁面，「設定」是目前所在', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner);

    // Act
    renderApp('/account');

    // Assert
    expect(within(await findRail()).getByRole('link', { name: '設定' })).toHaveAttribute('aria-current', 'page');
  });

  it('收件匣帶未讀數，報讀的說法是「N 則未讀」', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner, { unreadCount: 3 });

    // Act
    renderApp('/my-work');

    // Assert
    const link = within(await findRail()).getByRole('link', { name: '收件匣' });
    await waitFor(() => {
      expect(link).toHaveAccessibleDescription('3 則未讀');
    });
  });

  it('「搜尋」打開快速跳轉', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner);
    const { user } = renderApp('/my-work');

    // Act
    await user.click(within(await findRail()).getByRole('button', { name: '搜尋' }));

    // Assert
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('「專案」打開側欄的抽屜，Esc 關上並把焦點還給它', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user } = renderApp('/my-work');
    const projects = within(await findRail()).getByRole('button', { name: '專案' });
    expect(projects).toHaveAttribute('aria-expanded', 'false');
    expect(projects).toHaveAttribute('aria-controls', 'app-sidebar');

    // Act
    await user.click(projects);

    // Assert
    expect(projects).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('complementary')).toHaveAttribute('data-open', 'true');
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(projects).toHaveAttribute('aria-expanded', 'false');
    });
    expect(projects).toHaveFocus();
  });

  it('在專案的頁面底下，「專案」是目前所在', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/projects/WEB/info');

    // Assert
    expect(within(await findRail()).getByRole('button', { name: '專案' })).toHaveAttribute('aria-current', 'page');
  });

  it('組織有開放 AI 時多一格「AI 助理」，按下打開浮動面板，沒有右下角浮動的按鈕', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner, { projects: [webProject] });
    serveAiOptions(makeAiOptions());
    const { user } = renderApp('/my-work');
    const cell = await within(await findRail()).findByRole('button', { name: 'AI 助理' });
    expect(screen.getAllByRole('button', { name: 'AI 助理' })).toHaveLength(1);

    // Act
    await user.click(cell);

    // Assert
    expect(await screen.findByRole('dialog', { name: 'AI 助理' })).toBeInTheDocument();
    expect(cell).toHaveAttribute('aria-expanded', 'true');
    expect(within(await findRail()).getByRole('link', { name: '我的工作' })).not.toHaveAttribute('aria-current');
  });

  it('帳號按鈕打開帳號選單', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner);
    const { user } = renderApp('/my-work');

    // Act
    await user.click(within(await findRail()).getByRole('button', { name: /^.+（.+）$/ }));

    // Assert
    expect(await screen.findByRole('menuitem', { name: '我的帳號' })).toBeInTheDocument();
  });
});
