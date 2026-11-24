import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { created, memberRef, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { createFakeLiveConnection } from '../../test/liveConnection';
import { notification, serveNotifications } from '../../test/notifications';
import { renderApp } from '../../test/renderApp';
import { setItemPanelMode } from '../../lib/itemPanelMode';
import { currentPath } from '../../test/renderApp';
import { setToastMode } from './toastMode';

const COMMENT_ID = '0190c000-0000-7000-8000-000000000001';
const at = (minute: number) => `2026-11-22T08:${String(minute).padStart(2, '0')}:00Z`;

// 同一則：每次都新做一則會是不同的編號，時間又落在容許範圍裡，會被當成新的。
const KNOWN = notification({ kind: 'assigned', itemKey: 'SMK-7', createdAt: at(1) });
const known = () => KNOWN;
const incoming = () =>
  notification({
    kind: 'mentioned',
    itemKey: 'SMK-8',
    itemTitle: '登入流程',
    actor: memberRef,
    excerpt: '請看這個',
    commentId: COMMENT_ID,
    createdAt: at(5),
  });

async function openApp(path: string, existing = [known()]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [smkItem({ number: 7, title: '首頁設計稿' }), smkItem({ number: 8, title: '登入流程' })]);
  serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
  serveItem(detailOf(smkItem({ number: 8, title: '登入流程' })));
  serveActivity('SMK-7', [created()]);
  serveActivity('SMK-8', [created()]);
  const requests = serveNotifications(existing);
  const live = createFakeLiveConnection();
  const view = renderApp(path, { liveConnection: () => live });
  // 連上、開始時靜靜讀一次未讀。
  await waitFor(() => {
    expect(requests.lists.length).toBeGreaterThan(0);
  });
  await flushPending();
  return { ...view, live, requests };
}

/** 伺服器多了新的未讀、推 inbox；等到網頁讀過最新的未讀、處理完。 */
async function pushAndSettle(live: { emitInbox: () => void }, entries = [incoming(), known()]) {
  const requests = serveNotifications(entries);
  live.emitInbox();
  await waitFor(() => {
    expect(requests.lists.length).toBeGreaterThan(0);
  });
  await flushPending();
}

/** 報讀用的那一塊（live region），用它唸的字找。 */
const findAnnouncement = () => screen.findByText(/^新通知：/);

afterEach(() => {
  vi.restoreAllMocks();
});

describe('新通知的提示：收到即時推送', () => {
  it('開始時的未讀不跳；之後新來的跳，唸一次，點了打開項目並標成已讀', async () => {
    // Arrange
    const { live, user, router } = await openApp('/people');
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();

    // Act
    const entry = incoming();
    serveNotifications([entry, known()]);
    live.emitInbox();

    // Assert
    const region = await screen.findByRole('region', { name: '新通知' });
    const link = within(region).getByRole('link');
    expect(link).toHaveTextContent('提及你');
    expect(link).toHaveTextContent('SMK-8 登入流程');
    expect(link).toHaveTextContent('請看這個');
    expect(await findAnnouncement()).toHaveTextContent(`新通知：${memberRef.displayName} 提及你，SMK-8 登入流程：請看這個`);

    await user.click(link);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/projects/SMK/items/SMK-8');
    });
    expect(router.state.location.hash).toBe(`#comment-${COMMENT_ID}`);
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('點了標成已讀（送出這一則的編號）', async () => {
    // Arrange
    const { live, user } = await openApp('/my-work');
    const entry = incoming();
    const { reads } = serveNotifications([entry, known()]);
    live.emitInbox();

    // Act
    await user.click(await screen.findByRole('link', { name: /提及你/ }));

    // Assert
    await waitFor(() => {
      expect(reads).toEqual([[entry.notificationId]]);
    });
  });

  it('精簡：不顯示內容、報讀也不唸內容', async () => {
    // Arrange
    setToastMode('compact');
    const { live } = await openApp('/my-work');

    // Act
    serveNotifications([incoming(), known()]);
    live.emitInbox();

    // Assert
    const link = await screen.findByRole('link', { name: /提及你/ });
    expect(link).not.toHaveTextContent('請看這個');
    const spoken = await findAnnouncement();
    expect(spoken).toHaveTextContent('SMK-8 登入流程');
    expect(spoken).not.toHaveTextContent('請看這個');
  });

  it('不顯示：不跳；之後改回完整也不補', async () => {
    // Arrange
    setToastMode('off');
    const { live } = await openApp('/my-work');

    const entry = incoming();

    // Act
    await pushAndSettle(live, [entry, known()]);
    setToastMode('full');
    live.emitInbox();
    await flushPending();

    // Assert
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('改成不顯示：畫面上還在的提示立刻收掉', async () => {
    // Arrange
    const { live } = await openApp('/my-work');
    serveNotifications([incoming(), known()]);
    live.emitInbox();
    await screen.findByRole('region', { name: '新通知' });

    // Act
    setToastMode('off');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
    });
  });

  it('在收件匣頁不跳', async () => {
    // Arrange
    const { live } = await openApp('/inbox');
    await screen.findByRole('heading', { name: '收件匣' });

    // Act
    await pushAndSettle(live);

    // Assert
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('分頁看不見時不跳，回來時也不補', async () => {
    // Arrange
    const { live } = await openApp('/my-work');
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');

    const entry = incoming();

    // Act
    await pushAndSettle(live, [entry, known()]);
    visibility.mockReturnValue('visible');
    await pushAndSettle(live, [entry, known()]);

    // Assert
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('通知的項目正開在項目頁上不跳，其他項目的照跳', async () => {
    // Arrange
    const { live } = await openApp('/projects/SMK/items/SMK-8');
    await screen.findByRole('heading', { name: '登入流程' });

    // Act
    const elsewhere = notification({ kind: 'commented', itemKey: 'SMK-7', itemTitle: '首頁設計稿', actor: memberRef, createdAt: at(6) });
    serveNotifications([elsewhere, incoming(), known()]);
    live.emitInbox();

    // Assert
    const region = await screen.findByRole('region', { name: '新通知' });
    expect(within(region).getAllByRole('link')).toHaveLength(1);
    expect(within(region).getByRole('link')).toHaveTextContent('SMK-7');
  });

  it('通知的項目正開在項目面板（?item=）時不跳', async () => {
    // Arrange
    const { live } = await openApp('/projects/SMK?item=SMK-8');
    await screen.findByRole('heading', { name: '登入流程' });

    // Act
    await pushAndSettle(live);

    // Assert
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('重連之後重新開始：斷線期間來的不補', async () => {
    // Arrange
    const { live } = await openApp('/my-work');
    const entry = incoming();
    const requests = serveNotifications([entry, known()]);

    // Act
    live.emitReconnected();
    await waitFor(() => {
      expect(requests.lists.length).toBeGreaterThan(0);
    });
    await flushPending();
    await pushAndSettle(live, [entry, known()]);

    // Assert
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('一次來好幾則：最多三個提示，其餘併成「還有 N 則新通知」', async () => {
    // Arrange
    const { live } = await openApp('/my-work');
    const many = [9, 8, 7, 6, 5].map((minute) => notification({ kind: 'commented', itemKey: 'SMK-8', actor: memberRef, createdAt: at(minute) }));

    // Act
    serveNotifications([...many, known()]);
    live.emitInbox();

    // Assert
    expect(await screen.findByRole('link', { name: '還有 3 則新通知' })).toHaveAttribute('href', '/inbox');
    expect(within(screen.getByRole('region', { name: '新通知' })).getAllByRole('link')).toHaveLength(3);
  });

  it('專案的清單頁（寬螢幕有項目面板）：點了在旁邊的面板開，網址加 ?item，不離開清單', async () => {
    // Arrange
    const { live, user, router } = await openApp('/projects/SMK');
    await screen.findByText('首頁設計稿');
    serveNotifications([incoming(), known()]);
    live.emitInbox();

    // Act
    await user.click(await screen.findByRole('link', { name: /提及你/ }));

    // Assert
    await waitFor(() => {
      expect(router.state.location.search).toContain('item=SMK-8');
    });
    expect(router.state.location.pathname).toBe('/projects/SMK');
    expect(router.state.location.hash).toBe(`#comment-${COMMENT_ID}`);
    expect(await screen.findByRole('complementary', { name: /SMK-8/ })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('沒有項目面板的頁面（人員）：換到項目頁', async () => {
    // Arrange
    const { live, user, router } = await openApp('/people');
    serveNotifications([incoming(), known()]);
    live.emitInbox();

    // Act
    await user.click(await screen.findByRole('link', { name: /提及你/ }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/items/SMK-8');
    });
  });

  it('項目的浮動視窗開著（背後 inert）：提示照樣顯示、不被 inert、點得到，點了在視窗裡換成那個項目', async () => {
    // Arrange
    setItemPanelMode('modal');
    const { live, user, router } = await openApp('/projects/SMK?item=SMK-7');
    await screen.findByRole('dialog');

    // Act
    serveNotifications([incoming(), known()]);
    live.emitInbox();
    const region = await screen.findByRole('region', { name: '新通知' });

    // Assert
    expect(region.closest('[inert]')).toBeNull();
    expect(region.closest('[aria-modal="true"]')).not.toBeNull();
    expect(document.querySelector('#root, body > div:not([data-inert-exempt])')?.hasAttribute('inert')).toBe(true);
    await user.click(within(region).getByRole('link'));
    await waitFor(() => {
      expect(router.state.location.search).toContain('item=SMK-8');
    });
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('浮動視窗關掉之後提示層回到 body，提示還在', async () => {
    // Arrange
    setItemPanelMode('modal');
    const { live, user } = await openApp('/projects/SMK?item=SMK-7');
    const dialog = await screen.findByRole('dialog');
    serveNotifications([incoming(), known()]);
    live.emitInbox();
    await screen.findByRole('region', { name: '新通知' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const region = await screen.findByRole('region', { name: '新通知' });
    expect(region.closest('[aria-modal="true"]')).toBeNull();
    expect(region.parentElement?.parentElement).toBe(document.body);
  });

  it('Esc 只關掉提示，不會同時關掉後面的面板', async () => {
    // Arrange
    const { live, router } = await openApp('/projects/SMK?item=SMK-7');
    await screen.findByRole('complementary', { name: /SMK-7/ });
    serveNotifications([incoming(), known()]);
    live.emitInbox();
    const link = await screen.findByRole('link', { name: /提及你/ });
    act(() => {
      link.focus();
    });

    // Act
    const { default: userEvent } = await import('@testing-library/user-event');
    await userEvent.setup().keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
    });
    expect(router.state.location.search).toContain('item=SMK-7');
    expect(screen.getByRole('complementary', { name: /SMK-7/ })).toBeInTheDocument();
  });
});
