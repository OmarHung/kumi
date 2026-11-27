import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { memberRef } from '../../test/activity';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { notification, serveNotifications } from '../../test/notifications';
import { currentPath, renderApp } from '../../test/renderApp';

const assigned = notification({ kind: 'assigned', itemKey: 'SMK-7', itemTitle: '首頁設計稿' });
const mentioned = notification({
  kind: 'mentioned',
  itemKey: 'SMK-8',
  itemTitle: '登入流程',
  actor: memberRef,
  excerpt: '麻煩看一下',
});
const replied = notification({ kind: 'replied', itemKey: 'SMK-10', itemTitle: '結帳頁', actor: memberRef, excerpt: '好的' });
const changed = notification({
  kind: 'state_changed',
  itemKey: 'SMK-9',
  itemTitle: '報價單',
  fromStateName: '待辦',
  toStateName: '審查中',
  readAt: '2026-11-18T08:30:00Z',
});

function setup(path: string, items = [assigned, mentioned, replied, changed]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const requests = serveNotifications(items);
  return { requests, ...renderApp(path) };
}

const categoryTabs = () => screen.getByRole('navigation', { name: '通知的分類' });
const unreadToggle = () => screen.getByRole('button', { name: '只看未讀' });
const lastRequest = (requests: ReturnType<typeof setup>['requests']) => requests.lists.at(-1)?.searchParams;

describe('收件匣：分類', () => {
  it('三個分頁：全部、提及與回覆、指派給我；點了網址是 type，請求帶 category，清單只剩那一類', async () => {
    // Arrange
    const { user, router, requests } = setup('/inbox');
    await screen.findByRole('link', { name: /SMK-7/ });
    expect(within(categoryTabs()).getAllByRole('link').map((link) => link.textContent)).toEqual(['全部', '提及與回覆', '指派給我']);

    // Act
    await user.click(within(categoryTabs()).getByRole('link', { name: '提及與回覆' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('link', { name: /SMK-7/ })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: /SMK-8/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /SMK-10/ })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/inbox?type=mentions');
    expect(lastRequest(requests)?.get('category')).toBe('mentions');
    expect(within(categoryTabs()).getByRole('link', { name: '提及與回覆' })).toHaveAttribute('aria-current', 'page');
  });

  it('網址直接帶 type=assigned：目前的分頁是「指派給我」，請求帶 category=assigned；回到「全部」就不帶', async () => {
    // Arrange
    const { user, router, requests } = setup('/inbox?type=assigned');
    await screen.findByRole('link', { name: /SMK-7/ });
    expect(screen.queryByRole('link', { name: /SMK-8/ })).not.toBeInTheDocument();
    expect(within(categoryTabs()).getByRole('link', { name: '指派給我' })).toHaveAttribute('aria-current', 'page');
    expect(lastRequest(requests)?.get('category')).toBe('assigned');

    // Act
    await user.click(within(categoryTabs()).getByRole('link', { name: '全部' }));

    // Assert
    await screen.findByRole('link', { name: /SMK-8/ });
    expect(currentPath(router)).toBe('/inbox');
    expect(lastRequest(requests)?.has('category')).toBe(false);
  });

  it('換分類時搜尋字、只看未讀與開著的面板 item 都留著', async () => {
    // Arrange
    const { user, router } = setup('/inbox?show=all&q=%E7%99%BB%E5%85%A5&item=SMK-8');
    await screen.findByRole('link', { name: /SMK-8/ });

    // Act
    await user.click(within(categoryTabs()).getByRole('link', { name: '提及與回覆' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/inbox?show=all&q=%E7%99%BB%E5%85%A5&item=SMK-8&type=mentions');
    });
  });
});

describe('收件匣：搜尋', () => {
  it('打字停下來才送：網址的 q 與請求的 q 帶著，清單只剩符合的', async () => {
    // Arrange
    const { user, router, requests } = setup('/inbox?show=all');
    await screen.findByRole('link', { name: /SMK-7/ });
    const before = requests.lists.length;

    // Act
    await user.type(screen.getByRole('searchbox', { name: '搜尋通知' }), '登入');

    // Assert
    await waitFor(() => {
      expect(lastRequest(requests)?.get('q')).toBe('登入');
    });
    expect(requests.lists).toHaveLength(before + 1);
    await waitFor(() => {
      expect(screen.queryByRole('link', { name: /SMK-7/ })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: /SMK-8/ })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/inbox?show=all&q=%E7%99%BB%E5%85%A5');
  });

  it('網址帶 q：搜尋框顯示它，請求帶 q；也搜得到做事的人與狀態名稱', async () => {
    // Arrange & Act
    const { requests } = setup('/inbox?show=all&q=%E5%AF%A9%E6%9F%A5');

    // Assert
    expect(await screen.findByRole('link', { name: /SMK-9/ })).toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: '搜尋通知' })).toHaveValue('審查');
    expect(lastRequest(requests)?.get('q')).toBe('審查');
    expect(screen.queryByRole('link', { name: /SMK-7/ })).not.toBeInTheDocument();
  });

  it('有搜尋字而找不到：說找不到符合「…」，「清除搜尋」把 q 拿掉、其他條件照舊', async () => {
    // Arrange
    const { user, router } = setup('/inbox?show=all&type=assigned&q=zzz');
    expect(await screen.findByText('找不到符合「zzz」的通知')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: '清除搜尋' }));

    // Assert
    await screen.findByRole('link', { name: /SMK-7/ });
    expect(currentPath(router)).toBe('/inbox?show=all&type=assigned');
    expect(screen.getByRole('searchbox', { name: '搜尋通知' })).toHaveValue('');
  });
});

describe('收件匣：搜尋加只看未讀', () => {
  it('只看未讀而搜不到：說沒有符合的未讀通知，「看全部通知」保留搜尋字、找到已讀的那一則', async () => {
    // Arrange：報價單（SMK-9）已讀
    const { user, router } = setup('/inbox?q=報價單');
    expect(await screen.findByText('沒有符合「報價單」的未讀通知')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '清除搜尋' })).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: '看全部通知' }));

    // Assert
    await screen.findByRole('link', { name: /SMK-9/ });
    expect(currentPath(router)).toBe('/inbox?q=%E5%A0%B1%E5%83%B9%E5%96%AE&show=all');
  });
});

describe('收件匣：只看未讀', () => {
  it('預設按下，請求帶 unread；放開網址是 show=all、請求不帶 unread，再按一次回到預設', async () => {
    // Arrange
    const { user, router, requests } = setup('/inbox');
    await screen.findByRole('link', { name: /SMK-7/ });
    expect(unreadToggle()).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByRole('link', { name: /SMK-9/ })).not.toBeInTheDocument();
    expect(lastRequest(requests)?.get('unread')).toBe('true');

    // Act
    await user.click(unreadToggle());

    // Assert
    await screen.findByRole('link', { name: /SMK-9/ });
    expect(unreadToggle()).toHaveAttribute('aria-pressed', 'false');
    expect(currentPath(router)).toBe('/inbox?show=all');
    expect(lastRequest(requests)?.has('unread')).toBe(false);
    await user.click(unreadToggle());
    await waitFor(() => {
      expect(screen.queryByRole('link', { name: /SMK-9/ })).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/inbox');
  });

  it('換條件時開著的面板 item 與分類照樣帶著', async () => {
    // Arrange
    const { user, router } = setup('/inbox?type=mentions&item=SMK-8');
    await screen.findByRole('link', { name: /SMK-8/ });

    // Act
    await user.click(unreadToggle());

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/inbox?type=mentions&item=SMK-8&show=all');
    });
  });
});

describe('收件匣：空的狀態', () => {
  it('只看未讀而沒有：說沒有未讀的通知，「看全部通知」放開只看未讀並保留分類', async () => {
    // Arrange
    const { user, router } = setup('/inbox?type=mentions', [assigned, changed]);
    expect(await screen.findByText('沒有未讀的通知')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: '看全部通知' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/inbox?type=mentions&show=all');
    });
    expect(await screen.findByText('這個分類還沒有通知')).toBeInTheDocument();
  });

  it('全部都沒有：說還沒有通知', async () => {
    // Arrange & Act
    setup('/inbox?show=all', []);

    // Assert
    expect(await screen.findByText('還沒有通知')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '清除搜尋' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '看全部通知' })).not.toBeInTheDocument();
  });
});
