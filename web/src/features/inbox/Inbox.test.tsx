import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { http } from 'msw';
import { failure, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { detailOf, serveItem } from '../../test/items';
import { notification, serveNotifications } from '../../test/notifications';
import { created, memberRef, serveActivity } from '../../test/activity';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';

const assigned = notification({ kind: 'assigned', itemKey: 'SMK-7', itemTitle: '首頁設計稿', createdAt: '2026-11-18T08:00:00Z' });
const mentioned = notification({
  kind: 'mentioned',
  itemKey: 'SMK-8',
  itemTitle: '登入流程',
  actor: memberRef,
  excerpt: '@[範例使用者](mention:p-1) 麻煩看一下',
});
const changed = notification({
  kind: 'state_changed',
  itemKey: 'SMK-9',
  itemTitle: '報價單',
  fromStateName: '待辦',
  toStateName: '審查中',
  readAt: '2026-11-18T08:30:00Z',
});

function setup(path = '/inbox', items = [assigned, mentioned, changed], pageSize?: number) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const requests = serveNotifications(items, pageSize === undefined ? {} : { pageSize });
  return { requests, ...renderApp(path) };
}

function sidebarLink() {
  return within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '收件匣' });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('收件匣', () => {
  it('側欄有「收件匣」，連到 /inbox，旁邊的未讀數是說明（連結的名稱不變）', async () => {
    // Arrange & Act
    setup('/inbox');

    // Assert
    await screen.findByRole('heading', { name: '收件匣', level: 1 });
    expect(sidebarLink()).toHaveAttribute('href', '/inbox');
    await waitFor(() => {
      expect(sidebarLink()).toHaveAccessibleDescription('2 則未讀');
    });
    expect(sidebarLink()).toHaveAccessibleName('收件匣');
    expect(sidebarLink()).toHaveAttribute('aria-current', 'page');
  });

  it('沒有未讀時側欄不顯示數字', async () => {
    // Arrange & Act
    setup('/inbox', [changed]);

    // Assert
    await screen.findByRole('heading', { name: '收件匣', level: 1 });
    await flushPending();
    expect(sidebarLink()).toHaveAccessibleDescription('');
    expect(sidebarLink()).toHaveTextContent(/^收件匣$/);
  });

  it('預設只看未讀：只列未讀的，請求帶 unread=true；分類是 NavTabs（目前的標 aria-current），「只看未讀」是按下的切換', async () => {
    // Arrange & Act
    const { requests } = setup();

    // Assert
    const list = await screen.findByRole('list', { name: '通知' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(2);
    const tabs = screen.getByRole('navigation', { name: '通知的分類' });
    expect(within(tabs).getByRole('link', { name: '全部' })).toHaveAttribute('aria-current', 'page');
    expect(within(tabs).getByRole('link', { name: '提及與回覆' })).not.toHaveAttribute('aria-current');
    expect(screen.getByRole('button', { name: '只看未讀' })).toHaveAttribute('aria-pressed', 'true');
    expect(requests.lists[0]?.searchParams.get('unread')).toBe('true');
  });

  it('每一則：做的人、一句話、項目的編號與標題、摘要（提及換成 @名稱）、相對時間；整則是連到項目頁的連結', async () => {
    // Arrange & Act
    setup();

    // Assert
    const link = await screen.findByRole('link', { name: /SMK-8/ });
    expect(link).toHaveAttribute('href', '/projects/SMK/items/SMK-8');
    expect(link).toHaveTextContent('範例成員');
    expect(link).toHaveTextContent('提及你');
    expect(link).toHaveTextContent('SMK-8 登入流程');
    expect(link).toHaveTextContent('@範例使用者 麻煩看一下');
    expect(within(link).getByText(/前|剛剛|昨天|\d{4}\/\d{2}\/\d{2}/)).toBeInTheDocument();
  });

  it('未讀的一則有看不見、報讀軟體唸得到的「未讀」，已讀的沒有；不只靠字重', async () => {
    // Arrange
    const { user } = setup();
    await screen.findByRole('list', { name: '通知' });

    // Act
    await user.click(screen.getByRole('button', { name: '只看未讀' }));

    // Assert
    const unread = await screen.findByRole('link', { name: /SMK-7/ });
    expect(unread).toHaveAccessibleName(/未讀/);
    expect(within(unread).getByText('未讀：')).toHaveClass('visually-hidden');
    expect(screen.getByRole('link', { name: /SMK-9/ })).not.toHaveAccessibleName(/未讀/);
  });

  it('放開「只看未讀」：網址是 ?show=all，已讀與未讀都在，請求不帶 unread', async () => {
    // Arrange
    const { user, router, requests } = setup();
    await screen.findByRole('list', { name: '通知' });

    // Act
    await user.click(screen.getByRole('button', { name: '只看未讀' }));

    // Assert
    await screen.findByRole('link', { name: /SMK-9/ });
    expect(currentPath(router)).toBe('/inbox?show=all');
    expect(within(screen.getByRole('list', { name: '通知' })).getAllByRole('listitem')).toHaveLength(3);
    expect(requests.lists.at(-1)?.searchParams.has('unread')).toBe(false);
  });

  it('網址直接帶 ?show=all 就是沒有按下「只看未讀」', async () => {
    // Arrange & Act
    setup('/inbox?show=all');

    // Assert
    expect(await screen.findByRole('link', { name: /SMK-9/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '只看未讀' })).toHaveAttribute('aria-pressed', 'false');
  });
});

describe('收件匣：點一則', () => {
  it('在旁邊開項目面板、同時標成已讀：請求只標這一則、側欄的未讀數少一、那一則不再是未讀（還留在原處）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveNotifications([assigned, mentioned, changed]);
    serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
    serveActivity('SMK-7', [created()]);
    const { user, router } = renderApp('/inbox');
    const link = await screen.findByRole('link', { name: /SMK-7/ });
    await waitFor(() => {
      expect(sidebarLink()).toHaveAccessibleDescription('2 則未讀');
    });

    // Act
    await user.click(link);

    // Assert
    expect(await screen.findByRole('complementary', { name: '項目 SMK-7' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/inbox?item=SMK-7');
    await waitFor(() => {
      expect(requests.reads).toEqual([[assigned.notificationId]]);
    });
    await waitFor(() => {
      expect(sidebarLink()).toHaveAccessibleDescription('1 則未讀');
    });
    const row = screen.getByRole('link', { name: /SMK-7/ });
    expect(row).not.toHaveAccessibleName(/未讀/);
    expect(row).toHaveAttribute('aria-current', 'true');
    expect(screen.getByRole('link', { name: /SMK-8/ })).toHaveAccessibleName(/未讀/);
  });

  it('中鍵點開新分頁也標成已讀（瀏覽器只送 auxclick，不送 click）；右鍵不算', async () => {
    // Arrange
    const { requests } = setup();
    const link = await screen.findByRole('link', { name: /SMK-7/ });

    // Act
    fireEvent(link, new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 2 }));
    await flushPending();
    expect(requests.reads).toHaveLength(0);
    fireEvent(link, new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 }));

    // Assert
    await waitFor(() => {
      expect(requests.reads).toEqual([[assigned.notificationId]]);
    });
  });

  it('已經讀過的不會再送一次標成已讀', async () => {
    // Arrange
    const { user, requests } = setup('/inbox?show=all');
    serveItem(detailOf(smkItem({ number: 9, title: '報價單' })));
    serveActivity('SMK-9', [created()]);
    const link = await screen.findByRole('link', { name: /SMK-9/ });

    // Act
    await user.click(link);
    await screen.findByRole('complementary', { name: '項目 SMK-9' });
    await flushPending();

    // Assert
    expect(requests.reads).toHaveLength(0);
  });

  it('標成已讀失敗時說一聲，那一則照舊是未讀', async () => {
    // Arrange
    const { user } = setup();
    serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
    serveActivity('SMK-7', [created()]);
    server.use(http.post('/v1/notifications/read', () => failure(500, 'server.error', '伺服器暫時有問題。')));
    const link = await screen.findByRole('link', { name: /SMK-7/ });

    // Act
    await user.click(link);

    // Assert
    expect(await screen.findByText('無法標成已讀，下次打開收件匣時還是未讀。')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /SMK-7/ })).toHaveAccessibleName(/未讀/);
  });
});

describe('收件匣：全部標為已讀', () => {
  it('按了之後未讀數歸零、「未讀」分頁變成空的，報讀「已將 2 則通知標為已讀」，焦點留在按鈕上', async () => {
    // Arrange
    const { user, requests } = setup();
    await screen.findByRole('list', { name: '通知' });
    const button = screen.getByRole('button', { name: '全部標為已讀' });

    // Act
    await user.click(button);

    // Assert
    expect(await screen.findByText('沒有未讀的通知')).toBeInTheDocument();
    expect(requests.readAlls.count).toBe(1);
    expect(await screen.findByText('已將 2 則通知標為已讀。')).toBeInTheDocument();
    await waitFor(() => {
      expect(sidebarLink()).toHaveAccessibleDescription('');
    });
    expect(screen.getByRole('button', { name: '全部標為已讀' })).toHaveFocus();
  });

  it('「已將 N 則通知標為已讀。」換條件就清掉', async () => {
    // Arrange
    const { user } = setup();
    await screen.findByRole('list', { name: '通知' });
    await user.click(screen.getByRole('button', { name: '全部標為已讀' }));
    expect(await screen.findByText('已將 2 則通知標為已讀。')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '只看未讀' }));

    // Assert
    await screen.findByRole('link', { name: /SMK-9/ });
    expect(screen.queryByText('已將 2 則通知標為已讀。')).not.toBeInTheDocument();
  });

  it('「已將 N 則通知標為已讀。」下一次操作（點一則）就清掉', async () => {
    // Arrange
    const { user } = setup('/inbox?show=all');
    serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
    serveActivity('SMK-7', [created()]);
    await screen.findByRole('link', { name: /SMK-7/ });
    await user.click(screen.getByRole('button', { name: '全部標為已讀' }));
    expect(await screen.findByText('已將 2 則通知標為已讀。')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: /SMK-9/ }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByText('已將 2 則通知標為已讀。')).not.toBeInTheDocument();
    });
  });

  it('「全部」分頁裡的也都變成已讀', async () => {
    // Arrange
    const { user } = setup('/inbox?show=all');
    await screen.findByRole('link', { name: /SMK-7/ });

    // Act
    await user.click(screen.getByRole('button', { name: '全部標為已讀' }));

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('link', { name: /SMK-7/ })).not.toHaveAccessibleName(/未讀/);
    });
    expect(screen.getByRole('link', { name: /SMK-8/ })).not.toHaveAccessibleName(/未讀/);
  });

  it('沒有未讀的時候按鈕是 aria-disabled，按了不送請求', async () => {
    // Arrange
    const { user, requests } = setup('/inbox', [changed]);
    await screen.findByText('沒有未讀的通知');
    const button = screen.getByRole('button', { name: '全部標為已讀' });
    await waitFor(() => {
      expect(button).toHaveAttribute('aria-disabled', 'true');
    });

    // Act
    await user.click(button);
    await flushPending();

    // Assert
    expect(requests.readAlls.count).toBe(0);
  });
});

describe('收件匣：沒有東西、載入更多、讀不到', () => {
  it('「未讀」是空的：說這裡會出現什麼，給「看全部通知」', async () => {
    // Arrange
    const { user, router } = setup('/inbox', [changed]);

    expect(await screen.findByText('沒有未讀的通知')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: '看全部通知' }));

    // Assert
    await screen.findByRole('link', { name: /SMK-9/ });
    expect(currentPath(router)).toBe('/inbox?show=all');
  });

  it('「全部」是空的：說還沒有通知', async () => {
    // Arrange & Act
    setup('/inbox?show=all', []);

    // Assert
    expect(await screen.findByText('還沒有通知')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '看全部通知' })).not.toBeInTheDocument();
  });

  it('一頁放不下時有「載入更多」：帶著游標讀下一頁，讀完按鈕不見', async () => {
    // Arrange
    const { user, requests } = setup('/inbox?show=all', [assigned, mentioned, changed], 2);
    await screen.findByRole('link', { name: /SMK-8/ });
    const list = screen.getByRole('list', { name: '通知' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(2);

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByRole('link', { name: /SMK-9/ })).toBeInTheDocument();
    expect(within(list).getAllByRole('listitem')).toHaveLength(3);
    expect(requests.lists.at(-1)?.searchParams.get('cursor')).toBe('2');
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
  });

  it('讀不到時是可以重試的錯誤', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(http.get('/v1/notifications', () => failure(500, 'server.error', '伺服器暫時有問題。')));

    // Act
    renderApp('/inbox');

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('收件匣載入失敗');
    expect(within(alert).getByRole('button', { name: '重試' })).toBeInTheDocument();
  });
});

describe('收件匣：窄螢幕', () => {
  it('不開面板：點一則直接進項目頁，一樣標成已讀', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveNotifications([assigned, mentioned]);
    serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
    serveActivity('SMK-7', [created()]);
    const { user, router } = renderApp('/inbox');
    const link = await screen.findByRole('link', { name: /SMK-7/ });

    // Act
    await user.click(link);

    // Assert
    await screen.findByRole('heading', { name: '首頁設計稿', level: 1 });
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-7');
    expect(requests.reads).toEqual([[assigned.notificationId]]);
    expect(screen.queryByRole('complementary', { name: '項目 SMK-7' })).not.toBeInTheDocument();
  });
});
