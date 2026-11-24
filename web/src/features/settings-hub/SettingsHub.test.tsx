import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { SessionUser } from '../../api/types';
import { signedInAs } from '../../test/api';
import { serveEmailSettings } from '../../test/emailSettings';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { serveDescriptionTemplates } from '../../test/projectDescription';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { BELOW_WIDE_QUERY } from '../../lib/useMediaQuery';

const exampleAdmin: SessionUser = { ...exampleOwner, role: 'admin', displayName: '範例管理員' };

const PERSONAL = ['我的帳號', '通知', 'AI 帳號', '命令列與整合'];
const ORGANIZATION = ['說明格式', '寄信設定', 'AI 設定'];

function serveHubPages() {
  serveEmailSettings();
  serveDescriptionTemplates([]);
}

async function openHubNav(user: SessionUser, path = '/email-settings') {
  signedInAs(user);
  serveHubPages();
  const view = renderApp(path);
  const nav = await screen.findByRole('navigation', { name: '設定' });
  return { ...view, nav };
}

function linkNames(nav: HTMLElement): string[] {
  return within(nav)
    .getAllByRole('link')
    .map((link) => link.textContent);
}

describe('設定中心：次導覽', () => {
  it('成員只看得到「個人」一組的四項，沒有組織的', async () => {
    // Arrange / Act
    const { nav } = await openHubNav(exampleMember, '/account/notifications');

    // Assert
    expect(linkNames(nav)).toEqual(PERSONAL);
    expect(within(nav).getByText('個人')).toBeInTheDocument();
    expect(within(nav).queryByText('組織')).not.toBeInTheDocument();
  });

  it('管理員多「組織」的三項，沒有主機 AI 代理', async () => {
    // Arrange / Act
    const { nav } = await openHubNav(exampleAdmin);

    // Assert
    expect(linkNames(nav)).toEqual([...PERSONAL, ...ORGANIZATION]);
    expect(within(nav).queryByRole('link', { name: '主機 AI 代理' })).not.toBeInTheDocument();
  });

  it('擁有者四項組織的都有，主機 AI 代理在最後', async () => {
    // Arrange / Act
    const { nav } = await openHubNav(exampleOwner);

    // Assert
    expect(linkNames(nav)).toEqual([...PERSONAL, ...ORGANIZATION, '主機 AI 代理']);
  });

  it('目前所在的那一項標 aria-current="page"，其他都沒有', async () => {
    // Arrange / Act
    const { nav } = await openHubNav(exampleOwner);

    // Assert
    expect(within(nav).getByRole('link', { name: '寄信設定' })).toHaveAttribute('aria-current', 'page');
    const others = within(nav)
      .getAllByRole('link')
      .filter((link) => link.textContent !== '寄信設定');
    expect(others.every((link) => !link.hasAttribute('aria-current'))).toBe(true);
  });

  it('按次導覽換頁：h1 換成頁名、焦點留在按的連結上、次導覽沒有重新掛上', async () => {
    // Arrange
    const { user, nav, router } = await openHubNav(exampleOwner);
    expect(await screen.findByRole('heading', { level: 1, name: '寄信設定' })).toBeInTheDocument();
    const link = within(nav).getByRole('link', { name: '說明格式' });

    // Act
    await user.click(link);

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '說明格式' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/description-templates');
    // 同一個 nav 與同一個連結元素：外框沒有因為換頁而重新掛上，焦點才不會掉。
    expect(screen.getByRole('navigation', { name: '設定' })).toBe(nav);
    expect(within(nav).getByRole('link', { name: '說明格式' })).toBe(link);
    expect(link).toHaveFocus();
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(within(nav).getByRole('link', { name: '寄信設定' })).not.toHaveAttribute('aria-current');
  });

  it('按次導覽換頁時捲回最上面（外框不重新掛上，瀏覽器不會自己捲）', async () => {
    // Arrange
    const { user, nav } = await openHubNav(exampleOwner, '/email-settings');
    const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);

    // Act
    await user.click(within(nav).getByRole('link', { name: '說明格式' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '說明格式' })).toBeInTheDocument();
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('頁列的位置是「設定」，沒有連結（不是一頁）', async () => {
    // Arrange / Act
    await openHubNav(exampleOwner);

    // Assert
    const trail = screen.getByRole('navigation', { name: '位置' });
    expect(within(trail).getByText('設定')).toBeInTheDocument();
    expect(within(trail).queryByRole('link')).not.toBeInTheDocument();
  });
});

describe('設定中心：側欄的「設定」', () => {
  async function sidebarSettingsLink(path: string) {
    signedInAs(exampleOwner);
    serveHubPages();
    renderApp(path);
    const sidebar = await screen.findByRole('navigation', { name: '主要導覽' });
    return within(sidebar).findByRole('link', { name: '設定' });
  }

  it.each(['/account', '/account/notifications', '/ai-settings', '/email-settings', '/description-templates', '/description-templates/new'])(
    '在 %s 是目前所在',
    async (path) => {
      // Arrange / Act
      const link = await sidebarSettingsLink(path);

      // Assert
      expect(link).toHaveAttribute('aria-current', 'page');
      expect(link).toHaveAttribute('href', '/account');
    },
  );

  it('在「我的工作」不是目前所在', async () => {
    // Arrange / Act
    const link = await sidebarSettingsLink('/my-work');

    // Assert
    expect(link).not.toHaveAttribute('aria-current');
  });

  it('成員的側欄也有「設定」，沒有另外的說明格式、寄信設定、AI 設定入口', async () => {
    // Arrange
    signedInAs(exampleMember);
    renderApp('/my-work');
    const sidebar = await screen.findByRole('navigation', { name: '主要導覽' });

    // Act
    const settings = await within(sidebar).findByRole('link', { name: '設定' });

    // Assert
    expect(settings).toBeInTheDocument();
    for (const name of ['說明格式', '寄信設定', 'AI 設定']) {
      expect(within(sidebar).queryByRole('link', { name })).not.toBeInTheDocument();
    }
  });
});

describe('設定中心：沒有權限', () => {
  it('成員打 /email-settings 是沒有權限的畫面，沒有寄信設定的表單', async () => {
    // Arrange
    signedInAs(exampleMember);

    // Act
    renderApp('/email-settings');

    // Assert
    expect(await screen.findByText('只有組織的擁有者與管理員可以設定寄信。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '儲存' })).not.toBeInTheDocument();
  });
});

describe('設定中心：窄螢幕', () => {
  afterEach(() => {
    // jsdom 本來沒有 matchMedia：測試放的假的要拿掉，不留到別的測試。
    Reflect.deleteProperty(window, 'matchMedia');
  });

  function useNarrowScreen() {
    // 只有「不到 64rem」這個查詢符合；其他（窄螢幕的 48rem）不符合。
    window.matchMedia = (query: string) =>
      ({
        matches: query === BELOW_WIDE_QUERY,
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }) as unknown as MediaQueryList;
  }

  it('打了看不到的頁（成員打 /email-settings）：下拉照樣寫頁名，不是空白', async () => {
    // Arrange
    useNarrowScreen();
    signedInAs(exampleMember);
    serveHubPages();

    // Act
    renderApp('/email-settings');

    // Assert
    expect(await screen.findByRole('combobox', { name: '設定的頁面' })).toHaveTextContent('寄信設定');
    expect(await screen.findByText('只有組織的擁有者與管理員可以設定寄信。')).toBeInTheDocument();
  });

  it('次導覽換成工具列上的下拉，選了就換頁', async () => {
    // Arrange
    useNarrowScreen();
    signedInAs(exampleOwner);
    serveHubPages();
    const { user, router } = renderApp('/email-settings');
    const select = await screen.findByRole('combobox', { name: '設定的頁面' });

    // Act
    await chooseOption(user, select, '說明格式');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/description-templates');
    });
    expect(await screen.findByRole('heading', { level: 1, name: '說明格式' })).toBeInTheDocument();
    // 下拉取代了左邊的次導覽：沒有「個人」「組織」那一排連結。
    expect(screen.queryByRole('link', { name: '通知' })).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '設定的頁面' })).toHaveTextContent('說明格式');
  });
});
