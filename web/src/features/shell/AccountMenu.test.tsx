import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import { ok, signedInAs } from '../../test/api';
import { server } from '../../test/server';
import { exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { THEME_STORAGE_KEY } from '../../lib/theme';
import { WEEK_START_STORAGE_KEY } from '../../lib/weekStart';

afterEach(() => {
  document.documentElement.removeAttribute('data-theme');
});

async function openAccountMenu(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: '範例使用者（範例組織 · 擁有者）' }));
  return screen.getByRole('menu', { name: '帳號' });
}

describe('帳號選單', () => {
  it('導覽項目加了圖示之後，連結的名稱仍然只有文字', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/my-work');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    expect(within(nav).getByRole('link', { name: '我的工作' })).toHaveAttribute('href', '/my-work');
    expect(within(nav).getByRole('link', { name: '專案' })).toHaveAttribute('href', '/projects');
    expect(within(nav).getByRole('link', { name: '人員' })).toHaveAttribute('href', '/people');
  });

  it('打開時焦點到第一項，列出我的帳號、主題三選一、一週從哪天開始二選一與登出，目前的選擇用 aria-checked 標出來', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const { user } = renderApp('/my-work');

    // Act
    const menu = await openAccountMenu(user);

    // Assert
    const items = within(menu).getAllByRole('menuitem');
    // 「我的帳號」是帳號安全那一輪加的（internal/HANDOFF-security-local-ops.md 第 7 節第 1 點），在主題那一組前面。
    expect(items.map((item) => item.textContent)).toEqual(['我的帳號', '登出']);
    expect(items[0]).toHaveFocus();
    const choices = within(menu).getAllByRole('menuitemradio');
    expect(choices.map((item) => [item.textContent, item.getAttribute('aria-checked')])).toEqual([
      ['主題跟著系統', 'true'],
      ['淺色主題', 'false'],
      ['深色主題', 'false'],
      ['一週從週一開始', 'true'],
      ['一週從週日開始', 'false'],
    ]);
  });

  it('選深色主題：套到 <html data-theme>、記在這個瀏覽器，選單關上、焦點回到按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const { user } = renderApp('/my-work');
    const menu = await openAccountMenu(user);

    // Act
    await user.click(within(menu).getByRole('menuitemradio', { name: '深色主題' }));

    // Assert
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    expect(screen.queryByRole('menu', { name: '帳號' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '範例使用者（範例組織 · 擁有者）' })).toHaveFocus();
    const reopened = await openAccountMenu(user);
    expect(within(reopened).getByRole('menuitemradio', { name: '深色主題' })).toHaveAttribute('aria-checked', 'true');
    expect(within(reopened).getByRole('menuitemradio', { name: '主題跟著系統' })).toHaveAttribute('aria-checked', 'false');
  });

  it('改回跟著系統就拿掉 data-theme 與設定', async () => {
    // Arrange
    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    signedInAs(exampleOwner);
    const { user } = renderApp('/my-work');
    const menu = await openAccountMenu(user);

    // Act
    await user.click(within(menu).getByRole('menuitemradio', { name: '主題跟著系統' }));

    // Assert
    expect(document.documentElement).not.toHaveAttribute('data-theme');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
  });

  it('選「一週從週日開始」：記在這個瀏覽器，選單關上、焦點回到按鈕，再打開時打勾的是週日', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const { user } = renderApp('/my-work');
    const menu = await openAccountMenu(user);

    // Act
    await user.click(within(menu).getByRole('menuitemradio', { name: '一週從週日開始' }));

    // Assert
    expect(localStorage.getItem(WEEK_START_STORAGE_KEY)).toBe('sunday');
    expect(screen.queryByRole('menu', { name: '帳號' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '範例使用者（範例組織 · 擁有者）' })).toHaveFocus();
    const reopened = await openAccountMenu(user);
    expect(within(reopened).getByRole('menuitemradio', { name: '一週從週日開始' })).toHaveAttribute('aria-checked', 'true');
    expect(within(reopened).getByRole('menuitemradio', { name: '一週從週一開始' })).toHaveAttribute('aria-checked', 'false');
  });

  it('登出送出中：按鈕上的「組織 · 角色」換成「登出中…」，按鈕標 aria-disabled、打不開', async () => {
    // Arrange
    signedInAs(exampleOwner);
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.delete('/v1/session', async () => {
        await released;
        return ok(null);
      }),
    );
    const { user } = renderApp('/my-work');
    const menu = await openAccountMenu(user);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '登出' }));

    // Assert
    const button = await screen.findByRole('button', { name: '範例使用者（登出中…）' });
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(within(button).getByText('登出中…')).toBeInTheDocument();
    expect(screen.queryByText('範例組織 · 擁有者')).not.toBeInTheDocument();
    await user.click(button);
    expect(screen.queryByRole('menu', { name: '帳號' })).not.toBeInTheDocument();
    release();
    expect(await screen.findByRole('heading', { name: '登入' })).toBeInTheDocument();
  });
});
