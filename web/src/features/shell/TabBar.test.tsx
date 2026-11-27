import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';

afterEach(() => {
  vi.unstubAllGlobals();
});

/** 窄螢幕：只有窄螢幕的查詢符合。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

async function findTabBar() {
  return screen.findByRole('navigation', { name: '主要分頁' });
}

describe('底部分頁列', () => {
  it('寬螢幕不渲染', async () => {
    // Arrange
    signedInAs(exampleOwner);

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('navigation', { name: '主要導覽' });
    expect(screen.queryByRole('navigation', { name: '主要分頁' })).not.toBeInTheDocument();
  });

  it('窄螢幕有我的工作、專案、收件匣、更多四格，沒有 AI 入口時沒有 AI 助理', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner);

    // Act
    renderApp('/my-work');

    // Assert
    const bar = await findTabBar();
    expect(within(bar).getByRole('link', { name: '我的工作' })).toHaveAttribute('href', '/my-work');
    expect(within(bar).getByRole('link', { name: '專案' })).toHaveAttribute('href', '/projects');
    expect(within(bar).getByRole('link', { name: '收件匣' })).toHaveAttribute('href', '/inbox');
    expect(within(bar).getByRole('button', { name: '更多' })).toBeInTheDocument();
    expect(within(bar).queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
  });

  it('目前所在的那一格標 aria-current', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/inbox');

    // Assert
    const bar = await findTabBar();
    expect(within(bar).getByRole('link', { name: '收件匣' })).toHaveAttribute('aria-current', 'page');
    expect(within(bar).getByRole('link', { name: '專案' })).not.toHaveAttribute('aria-current');
  });

  it('在專案的頁面底下「專案」是目前所在，按了回到專案列表', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/projects/WEB/info');

    // Assert
    const link = within(await findTabBar()).getByRole('link', { name: '專案' });
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(link).toHaveAttribute('href', '/projects');
  });

  it('從別的分頁按「專案」回到上次看的專案頁；沒看過時是專案列表', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, router } = renderApp('/my-work');
    const bar = await findTabBar();
    expect(within(bar).getByRole('link', { name: '專案' })).toHaveAttribute('href', '/projects');

    // Act
    await router.navigate('/projects/WEB/info');
    await user.click(within(await findTabBar()).getByRole('link', { name: '收件匣' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/inbox');
    });
    expect(within(await findTabBar()).getByRole('link', { name: '專案' })).toHaveAttribute('href', '/projects/WEB/info');
  });

  it('收件匣帶未讀數，報讀的說法是「N 則未讀」，名稱仍是收件匣', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { unreadCount: 3 });

    // Act
    renderApp('/my-work');

    // Assert
    const link = within(await findTabBar()).getByRole('link', { name: '收件匣' });
    await waitFor(() => {
      expect(link).toHaveAccessibleDescription('3 則未讀');
    });
  });

  it('未讀超過 99 時膠囊寫「99+」，報讀照舊唸完整的數字', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { unreadCount: 120 });

    // Act
    renderApp('/my-work');

    // Assert
    const link = within(await findTabBar()).getByRole('link', { name: '收件匣' });
    await waitFor(() => {
      expect(link).toHaveAccessibleDescription('120 則未讀');
    });
    expect(link.querySelector('[aria-hidden="true"]:not(svg)')).toHaveTextContent(/^99\+$/);
  });

  it('未讀是 0 時不顯示數字', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { unreadCount: 0 });

    // Act
    renderApp('/my-work');

    // Assert
    const link = within(await findTabBar()).getByRole('link', { name: '收件匣' });
    expect(link).toHaveAccessibleDescription('');
  });

  it('「更多」打開側欄的抽屜，關上時焦點回到「更多」', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner);
    const { user } = renderApp('/my-work');
    const more = within(await findTabBar()).getByRole('button', { name: '更多' });
    expect(more).toHaveAttribute('aria-expanded', 'false');
    expect(more).toHaveAttribute('aria-controls', 'app-sidebar');

    // Act
    await user.click(more);

    // Assert
    expect(more).toHaveAttribute('aria-expanded', 'true');
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(more).toHaveAttribute('aria-expanded', 'false');
    });
    expect(more).toHaveFocus();
  });

  it('組織有開放 AI 時多一格「AI 助理」，按下打開浮動面板，再按收起來並把焦點還給它', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject] });
    serveAiOptions(makeAiOptions());
    const { user } = renderApp('/my-work');
    const tab = await within(await findTabBar()).findByRole('button', { name: 'AI 助理' });

    // Act
    await user.click(tab);

    // Assert
    expect(await screen.findByRole('dialog', { name: 'AI 助理' })).toBeInTheDocument();
    expect(tab).toHaveAttribute('aria-expanded', 'true');
    expect(within(await findTabBar()).getByRole('link', { name: '我的工作' })).not.toHaveAttribute('aria-current');
    await user.click(tab);
    await waitFor(() => {
      expect(tab).toHaveAttribute('aria-expanded', 'false');
    });
    expect(tab).toHaveFocus();
  });

  it('抽屜開著時按「AI 助理」：抽屜先收起來，面板關上後「更多」不再是展開的', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { projects: [webProject] });
    serveAiOptions(makeAiOptions());
    const { user } = renderApp('/my-work');
    const bar = await findTabBar();
    const tab = await within(bar).findByRole('button', { name: 'AI 助理' });
    const more = within(bar).getByRole('button', { name: '更多' });
    await user.click(more);
    expect(more).toHaveAttribute('aria-expanded', 'true');

    // Act
    await user.click(tab);

    // Assert
    expect(await screen.findByRole('dialog', { name: 'AI 助理' })).toBeInTheDocument();
    expect(more).toHaveAttribute('aria-expanded', 'false');
  });

  it('窄螢幕沒有右下角浮動的 AI 按鈕', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions());

    // Act
    renderApp('/my-work');

    // Assert
    await within(await findTabBar()).findByRole('button', { name: 'AI 助理' });
    expect(screen.getAllByRole('button', { name: 'AI 助理' })).toHaveLength(1);
  });

  it('面板開著時點別的分頁：先收起面板再換頁，分頁列沒有被 inert', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions());
    const { user, router } = renderApp('/my-work');
    await user.click(await within(await findTabBar()).findByRole('button', { name: 'AI 助理' }));
    await screen.findByRole('dialog', { name: 'AI 助理' });
    const bar = await findTabBar();
    expect(bar.closest('[inert]')).toBeNull();

    // Act
    await user.click(within(bar).getByRole('link', { name: '收件匣' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/inbox');
    });
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'AI 助理', hidden: false })).not.toBeInTheDocument();
    });
  });

  it('在整頁的 AI 助理裡，「AI 助理」是目前所在的連結', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions());

    // Act
    renderApp('/assistant');

    // Assert
    const link = await within(await findTabBar()).findByRole('link', { name: 'AI 助理' });
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(within(await findTabBar()).getByRole('link', { name: '專案' })).not.toHaveAttribute('aria-current');
  });
});
