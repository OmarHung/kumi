import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PRODUCT_NAME } from '../../config/product';
import { badgedFaviconUrl, DEFAULT_TAB_BADGE } from '../inbox/faviconBadge';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

describe('外框', () => {
  it('顯示使用者名稱與組織', async () => {
    // Arrange
    signedInAs(exampleOwner);

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByText('範例使用者')).toBeInTheDocument();
    expect(screen.getByText('範例組織 · 擁有者')).toBeInTheDocument();
  });

  it('目前所在的頁面在側欄標示出來', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/projects/WEB');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    expect(await within(nav).findByRole('link', { name: /官網改版/ })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(nav).getByRole('link', { name: '專案' })).not.toHaveAttribute('aria-current');
  });

  it('登出後回到登入頁', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const deleted = vi.fn();
    server.use(
      http.delete('/v1/session', () => {
        deleted();
        return ok(null);
      }),
    );
    const { user, router } = renderApp('/my-work');

    // Act
    await signOutFromMenu(user);

    // Assert
    expect(await screen.findByRole('heading', { name: '登入' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/login');
    expect(deleted).toHaveBeenCalledTimes(1);
  });

  it('工作階段已經失效時登出也算成功', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.delete('/v1/session', () => failure(401, 'auth.required', '請先登入。')));
    const { user } = renderApp('/my-work');

    // Act
    await signOutFromMenu(user);

    // Assert
    expect(await screen.findByRole('heading', { name: '登入' })).toBeInTheDocument();
  });

  it('登出失敗時說明，而且留在原本的畫面', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.delete('/v1/session', () => HttpError()));
    const { user, router } = renderApp('/my-work');

    // Act
    await signOutFromMenu(user);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('登出失敗');
    expect(currentPath(router)).toBe('/my-work');
  });

  it('窄螢幕的選單打開時焦點移進選單，按 Esc 關閉並回到選單按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user } = renderApp('/my-work');
    const menuButton = await screen.findByRole('button', { name: '選單' });

    // Act
    await user.click(menuButton);
    const expandedAfterOpen = menuButton.getAttribute('aria-expanded');
    const focusedAfterOpen = document.activeElement;
    await user.keyboard('{Escape}');

    // Assert
    expect(expandedAfterOpen).toBe('true');
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    expect(focusedAfterOpen).toBe(within(nav).getByRole('link', { name: '我的工作' }));
    expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    expect(menuButton).toHaveFocus();
  });

  it('從選單點連結之後選單自動關起來', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, router } = renderApp('/my-work');
    const menuButton = await screen.findByRole('button', { name: '選單' });
    await user.click(menuButton);

    // Act
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    await user.click(within(nav).getByRole('link', { name: '專案' }));

    // Assert
    expect(currentPath(router)).toBe('/projects');
    expect(menuButton).toHaveAttribute('aria-expanded', 'false');
  });

  it('登入後找不到的網址顯示 404，可以回到我的工作', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const { user, router } = renderApp('/no-such-page');

    // Act
    expect(
      await screen.findByRole('heading', { level: 1, name: '找不到這個頁面' }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: '回到我的工作' }));

    // Assert
    expect(currentPath(router)).toBe('/my-work');
  });
});

describe('分頁上的未讀提示', () => {
  // 測試環境沒有 index.html，自己掛一個和它一樣的圖示 link。
  let icon: HTMLLinkElement;

  beforeEach(() => {
    icon = document.createElement('link');
    icon.rel = 'icon';
    icon.setAttribute('href', '/favicon.svg');
    document.head.append(icon);
  });

  afterEach(() => {
    icon.remove();
  });

  it('有未讀時分頁標題前面是未讀數，圖示換成帶圓點的', async () => {
    // Arrange
    signedInAs(exampleOwner, { unreadCount: 3 });

    // Act
    renderApp('/my-work');

    // Assert
    await waitFor(() => {
      expect(document.title).toBe(`(3) 我的工作 · ${PRODUCT_NAME}`);
    });
    expect(icon).toHaveAttribute('href', badgedFaviconUrl(DEFAULT_TAB_BADGE));
  });

  it('登出之後分頁標題不帶數字，圖示換回原本的', async () => {
    // Arrange
    signedInAs(exampleOwner, { unreadCount: 3 });
    server.use(http.delete('/v1/session', () => ok(null)));
    const { user } = renderApp('/my-work');
    await waitFor(() => {
      expect(icon).toHaveAttribute('href', badgedFaviconUrl(DEFAULT_TAB_BADGE));
    });

    // Act
    await signOutFromMenu(user);

    // Assert
    expect(await screen.findByRole('heading', { name: '登入' })).toBeInTheDocument();
    await waitFor(() => {
      expect(document.title).toBe(`登入 · ${PRODUCT_NAME}`);
    });
    expect(icon).toHaveAttribute('href', '/favicon.svg');
  });
});

// 登出在帳號選單裡（internal/HANDOFF-ui-ux-refactor.md 3A.4）：先打開選單再選「登出」。
async function signOutFromMenu(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: /範例使用者/ }));
  await user.click(screen.getByRole('menuitem', { name: '登出' }));
}

function HttpError() {
  return failure(500, 'server.internal_error', 'boom');
}
