import { QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import type { ItemNotification } from '../../api/notificationTypes';
import type { Notification } from '../../api/types';
import { memberRef } from '../../test/activity';
import { flushPending } from '../../test/flush';
import { accountNotification, notification, serveNotifications } from '../../test/notifications';
import { createItemPanelRegistry, ItemPanelRegistryContext, type ItemPanelRegistry } from '../item-page/ItemPanelRegistry';
import type { ItemPanelValue } from '../item-page/ItemPanelContext';
import { NotificationToasts } from './NotificationToasts';
import { toastAnnouncement } from './toastText';
import { TOAST_DISMISS_MS } from './useAutoDismiss';
import type { ToastMode } from './toastMode';
import type { ToastAnnouncement } from './useNotificationToasts';

const SPEAK_WAIT_MS = 150;
const COMMENT_ID = '0190c000-0000-7000-8000-000000000001';

const mention = (overrides: Partial<ItemNotification> = {}) =>
  notification({
    kind: 'mentioned',
    actor: memberRef,
    projectKey: 'MNT',
    itemKey: 'MNT-12',
    itemTitle: '修正登入頁',
    excerpt: '請看 @[王小明](mention:abc) 的設計稿',
    commentId: COMMENT_ID,
    ...overrides,
  });

/** 提示本身不管資料從哪來：用一個小外殼持有清單，和 useNotificationToasts 一樣把 dismiss 接回去。 */
interface HarnessProps {
  readonly initial: readonly Notification[];
  readonly mode?: ToastMode;
  readonly announcement?: ToastAnnouncement;
  readonly registry?: ItemPanelRegistry;
  readonly marksRead?: boolean;
  /** 放在畫面上的其他東西（例如開著的對話框）。 */
  readonly extra?: ReactNode;
}

function Harness({ initial, mode = 'full', announcement = { text: '', seq: 0 }, registry, extra, marksRead }: HarnessProps) {
  const [entries, setEntries] = useState(initial);
  const [repeats, setRepeats] = useState(0);
  return (
    <>
      <button type="button">之前的按鈕</button>
      <main id="main-content" tabIndex={-1}>
        內容
      </main>
      {extra}
      <button
        type="button"
        onClick={() => {
          setRepeats((count) => count + 1);
        }}
      >
        再唸一次
      </button>
      <ItemPanelRegistryContext value={registry ?? null}>
        <NotificationToasts
          marksRead={marksRead}
          view={{
            entries,
            mode,
            announcement: { text: announcement.text, seq: announcement.seq + repeats },
            dismiss: (ids) => {
              setEntries((current) => current.filter((entry) => !ids.includes(entry.notificationId)));
            },
          }}
        />
      </ItemPanelRegistryContext>
    </>
  );
}

function renderToasts(props: Parameters<typeof Harness>[0]) {
  const router = createMemoryRouter(
    [
      { path: '/projects/:projectKey/items/:itemKey', element: <p>項目頁</p> },
      { path: '/inbox', element: <p>收件匣頁</p> },
      { path: '*', element: <Harness {...props} /> },
    ],
    { initialEntries: ['/my-work'] },
  );
  const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
  const queryClient = createQueryClient({ queries: { retry: false } });
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { user, router };
}

describe('新通知的提示', () => {
  beforeEach(() => {
    // user-event 與 Testing Library 內部用真的時間等一下：時鐘要照真實的時間往前走，測試再用 advanceTimersByTime 加速。
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('完整：誰做了什麼、項目編號與標題、內容；整則是連到那一則留言的連結', () => {
    // Arrange & Act
    renderToasts({ initial: [mention()] });

    // Assert
    const region = screen.getByRole('region', { name: '新通知' });
    const link = within(region).getByRole('link');
    expect(link).toHaveAttribute('href', `/projects/MNT/items/MNT-12#comment-${COMMENT_ID}`);
    expect(link).toHaveTextContent(`${memberRef.displayName} 提及你`);
    expect(link).toHaveTextContent('MNT-12 修正登入頁');
    expect(link).toHaveTextContent('請看 @王小明 的設計稿');
  });

  it('精簡：沒有內容', () => {
    // Arrange & Act
    renderToasts({ initial: [mention()], mode: 'compact' });

    // Assert
    const link = within(screen.getByRole('region', { name: '新通知' })).getByRole('link');
    expect(link).toHaveTextContent('MNT-12 修正登入頁');
    expect(link).not.toHaveTextContent('設計稿');
  });

  it('不顯示：沒有提示', () => {
    // Arrange & Act
    renderToasts({ initial: [mention()], mode: 'off' });

    // Assert
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('報讀的字放在一直都在的 live region（polite）裡', () => {
    // Arrange & Act
    renderToasts({ initial: [], announcement: { text: '新通知：王小明 提及你，MNT-12 修正登入頁', seq: 1 } });
    act(() => {
      vi.advanceTimersByTime(SPEAK_WAIT_MS);
    });

    // Assert
    const liveRegion = screen.getByText('新通知：王小明 提及你，MNT-12 修正登入頁');
    expect(liveRegion).toHaveAttribute('aria-live', 'polite');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('8 秒後自己消失', () => {
    // Arrange
    renderToasts({ initial: [mention()] });

    // Act
    act(() => {
      vi.advanceTimersByTime(TOAST_DISMISS_MS - 1000);
    });
    const isStillThere = screen.queryByRole('region', { name: '新通知' }) !== null;
    act(() => {
      vi.advanceTimersByTime(1500);
    });

    // Assert
    expect(isStillThere).toBe(true);
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('滑鼠移上去暫停，離開之後接著算剩下的時間', async () => {
    // Arrange
    const { user } = renderToasts({ initial: [mention()] });
    const link = screen.getByRole('link');
    act(() => {
      vi.advanceTimersByTime(6000);
    });

    // Act
    await user.hover(link);
    act(() => {
      vi.advanceTimersByTime(60_000);
    });
    const isShownWhileHovering = screen.queryByRole('link') !== null;
    await user.unhover(link);
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    const isShownBeforeEnd = screen.queryByRole('link') !== null;
    act(() => {
      vi.advanceTimersByTime(1500);
    });

    // Assert
    expect(isShownWhileHovering).toBe(true);
    expect(isShownBeforeEnd).toBe(true);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('焦點在裡面時暫停，焦點離開之後才開始算', async () => {
    // Arrange
    const { user } = renderToasts({ initial: [mention()] });
    act(() => {
      screen.getByRole('link').focus();
    });
    expect(screen.getByRole('link')).toHaveFocus();

    // Act
    act(() => {
      vi.advanceTimersByTime(60_000);
    });
    const isShownWhileFocused = screen.queryByRole('link') !== null;
    await user.tab();
    await user.tab();
    act(() => {
      vi.advanceTimersByTime(TOAST_DISMISS_MS);
    });

    // Assert
    expect(isShownWhileFocused).toBe(true);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('Esc 關掉焦點所在的那一則，焦點放到下一則', async () => {
    // Arrange
    const first = mention({ itemKey: 'MNT-1', itemTitle: '第一則' });
    const second = mention({ itemKey: 'MNT-2', itemTitle: '第二則' });
    const { user } = renderToasts({ initial: [first, second] });
    act(() => {
      screen.getByRole('link', { name: /第一則/ }).focus();
    });
    expect(screen.getByRole('link', { name: /第一則/ })).toHaveFocus();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('link', { name: /第一則/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /第二則/ })).toHaveFocus();
  });

  it('關閉按鈕關掉那一則；最後一則關掉之後焦點放到頁面的主要內容', async () => {
    // Arrange
    const { user } = renderToasts({ initial: [mention()] });
    const close = screen.getByRole('button', { name: '關閉這則通知' });

    // Act
    await user.click(close);

    // Assert
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
    expect(document.getElementById('main-content')).toHaveFocus();
  });

  it('點了連結換到項目頁，並且收掉這一則', async () => {
    // Arrange
    const { user, router } = renderToasts({ initial: [mention()] });

    // Act
    await user.click(screen.getByRole('link'));

    // Assert
    expect(router.state.location.pathname).toBe('/projects/MNT/items/MNT-12');
    expect(router.state.location.hash).toBe(`#comment-${COMMENT_ID}`);
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('超過三則：留最新的兩則，其餘併成「還有 N 則新通知」連到收件匣', async () => {
    // Arrange
    const entries = ['甲', '乙', '丙', '丁', '戊'].map((title) => mention({ itemTitle: title }));
    const { user, router } = renderToasts({ initial: entries });

    // Act
    const folded = screen.getByRole('link', { name: '還有 3 則新通知' });
    const shown = within(screen.getByRole('region', { name: '新通知' })).getAllByRole('link');
    await user.click(folded);

    // Assert
    expect(shown).toHaveLength(3);
    expect(shown[1]).toHaveTextContent('丁');
    expect(shown[2]).toHaveTextContent('戊');
    expect(router.state.location.pathname).toBe('/inbox');
  });

  it('連續兩則報讀的字一樣：先清空再唸，報讀軟體才看得到變化', async () => {
    // Arrange
    const text = '新通知：王小明 提及你，MNT-12 修正登入頁';
    const { user } = renderToasts({ initial: [], announcement: { text, seq: 1 } });
    act(() => {
      vi.advanceTimersByTime(SPEAK_WAIT_MS);
    });
    expect(screen.getByText(text)).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '再唸一次' }));
    const isClearedFirst = screen.queryByText(text) === null;
    act(() => {
      vi.advanceTimersByTime(SPEAK_WAIT_MS);
    });

    // Assert
    expect(isClearedFirst).toBe(true);
    expect(screen.getByText(text)).toBeInTheDocument();
  });

  it('提示整層放在 body 底下、帶不受 inert 影響的標記', () => {
    // Arrange & Act
    renderToasts({ initial: [mention()] });

    // Assert
    const layer = screen.getByRole('region', { name: '新通知' }).parentElement;
    expect(layer?.parentElement).toBe(document.body);
    expect(layer).toHaveAttribute('data-inert-exempt');
  });
});

/** 確認類的對話框的替身：和 components/Dialog 一樣放在 body 底下、role="dialog" aria-modal。 */
function DialogStub({ isFloatingWindow }: { readonly isFloatingWindow: boolean }) {
  const [isOpen, setIsOpen] = useState(true);
  return (
    <>
      {isOpen &&
        createPortal(
          <div role="dialog" aria-modal="true" aria-label="確認" data-shortcuts-through={isFloatingWindow || undefined}>
            <button
              type="button"
              onClick={() => {
                setIsOpen(false);
              }}
            >
              關掉對話框
            </button>
          </div>,
          document.body,
        )}
    </>
  );
}

/** 可以從外面開關的視窗（和 ModalFrame 一樣：放在 body 底下的 role="dialog" aria-modal）。 */
function WindowStub() {
  const [isOpen, setIsOpen] = useState(true);
  return (
    <>
      {isOpen &&
        createPortal(
          <div role="dialog" aria-modal="true" aria-label="浮動視窗" data-shortcuts-through>
            <button
              type="button"
              onClick={() => {
                setIsOpen(false);
              }}
            >
              關掉視窗
            </button>
          </div>,
          document.body,
        )}
    </>
  );
}

describe('新通知的提示：aria-modal 視窗開著時', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('提示層放在視窗的元素裡（無障礙樹才包含它）；視窗關掉之後回到 body，提示還在', async () => {
    // Arrange
    const { user } = renderToasts({ initial: [mention()], extra: <WindowStub /> });
    const region = await screen.findByRole('region', { name: '新通知' });
    const layer = region.parentElement;

    // Assert（開著）
    expect(region.closest('[aria-modal="true"]')).not.toBeNull();
    expect(layer).toHaveAttribute('data-inert-exempt');

    // Act
    await user.click(screen.getByRole('button', { name: '關掉視窗' }));

    // Assert（關掉）
    const after = await screen.findByRole('region', { name: '新通知' });
    expect(after.closest('[aria-modal="true"]')).toBeNull();
    expect(after.parentElement?.parentElement).toBe(document.body);
    expect(within(after).getByRole('link')).toHaveTextContent('MNT-12 修正登入頁');
  });
});

describe('新通知的提示：對話框開著時', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('確認類的對話框開著時先不顯示，也不唸；關掉之後才顯示、才唸', async () => {
    // Arrange
    const text = '新通知：王小明 提及你，MNT-12 修正登入頁';
    const { user } = renderToasts({ initial: [mention()], announcement: { text, seq: 1 }, extra: <DialogStub isFloatingWindow={false} /> });
    act(() => {
      vi.advanceTimersByTime(SPEAK_WAIT_MS);
    });
    const wasHidden = screen.queryByRole('region', { name: '新通知' }) === null;
    const wasQuiet = screen.queryByText(text) === null;

    // Act
    await user.click(screen.getByRole('button', { name: '關掉對話框' }));
    act(() => {
      vi.advanceTimersByTime(SPEAK_WAIT_MS);
    });

    // Assert
    expect(wasHidden).toBe(true);
    expect(wasQuiet).toBe(true);
    expect(await screen.findByRole('region', { name: '新通知' })).toBeInTheDocument();
    expect(screen.getByText(text)).toBeInTheDocument();
  });

  it('項目的浮動視窗（data-shortcuts-through）會開很久：提示照樣顯示', () => {
    // Arrange & Act
    renderToasts({ initial: [mention()], extra: <DialogStub isFloatingWindow /> });

    // Assert
    expect(screen.getByRole('region', { name: '新通知' })).toBeInTheDocument();
  });
});

function panelStub(overrides: Partial<ItemPanelValue> = {}): ItemPanelValue {
  return {
    openKey: null,
    isEnabled: true,
    open: vi.fn(),
    show: vi.fn(),
    close: vi.fn(),
    closeWith: vi.fn(() => Promise.resolve()),
    registry: undefined as never,
    ...overrides,
  };
}

describe('新通知的提示：點了怎麼打開', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('這一頁有項目面板（寬螢幕）：在旁邊的面板開，帶著留言的 hash，不換頁', async () => {
    // Arrange
    const registry = createItemPanelRegistry();
    const panel = panelStub();
    registry.register(panel);
    const { user, router } = renderToasts({ initial: [mention()], registry });

    // Act
    await user.click(screen.getByRole('link'));

    // Assert
    expect(panel.open).toHaveBeenCalledWith('MNT-12', `#comment-${COMMENT_ID}`);
    expect(router.state.location.pathname).toBe('/my-work');
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
  });

  it('面板沒有啟用（窄螢幕）：換到項目頁', async () => {
    // Arrange
    const registry = createItemPanelRegistry();
    const panel = panelStub({ isEnabled: false });
    registry.register(panel);
    const { user, router } = renderToasts({ initial: [mention()], registry });

    // Act
    await user.click(screen.getByRole('link'));

    // Assert
    expect(panel.open).not.toHaveBeenCalled();
    expect(router.state.location.pathname).toBe('/projects/MNT/items/MNT-12');
  });

  it('按著 Ctrl 點（開新分頁）不開面板', async () => {
    // Arrange
    const registry = createItemPanelRegistry();
    const panel = panelStub();
    registry.register(panel);
    const { user } = renderToasts({ initial: [mention()], registry });

    // Act
    await user.keyboard('{Control>}');
    await user.click(screen.getByRole('link'));
    await user.keyboard('{/Control}');

    // Assert
    expect(panel.open).not.toHaveBeenCalled();
  });

  it('點了標成已讀；範例（marksRead 為 false）不送', async () => {
    // Arrange
    const requests = serveNotifications([]);
    const first = renderToasts({ initial: [mention()] });

    // Act
    await first.user.click(screen.getByRole('link'));
    await flushPending();

    // Assert
    expect(requests.reads).toHaveLength(1);
  });

  it('marksRead 為 false：點了不送標成已讀', async () => {
    // Arrange
    const requests = serveNotifications([]);
    const { user } = renderToasts({ initial: [mention()], marksRead: false });

    // Act
    await user.click(screen.getByRole('link'));
    await flushPending();

    // Assert
    expect(requests.reads).toEqual([]);
  });
});

describe('新通知的提示：帳號通知', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const tokenCreated = () =>
    accountNotification({
      event: 'access_token_created',
      tokens: [{ name: 'CI', scopes: ['items:read', 'items:write'], expiresAt: null }],
      machineName: null,
    });

  it('一句話說明做了什麼，第二行是「帳號安全」，連到「命令列與整合」', () => {
    // Arrange & Act
    renderToasts({ initial: [tokenCreated()] });

    // Assert
    const link = within(screen.getByRole('region', { name: '新通知' })).getByRole('link');
    expect(link).toHaveAttribute('href', '/account/integrations');
    expect(link).toHaveTextContent('建立了存取權杖「CI」（讀取項目、修改項目），從 203.0.113.5');
    expect(link).toHaveTextContent('帳號安全');
  });

  it('點了換到「命令列與整合」，不在旁邊的面板開項目，並且標成已讀、收掉這一則', async () => {
    // Arrange
    const requests = serveNotifications([]);
    const registry = createItemPanelRegistry();
    const panel = panelStub();
    registry.register(panel);
    const entry = tokenCreated();
    const { user, router } = renderToasts({ initial: [entry], registry });

    // Act
    await user.click(screen.getByRole('link'));
    await flushPending();

    // Assert
    expect(panel.open).not.toHaveBeenCalled();
    expect(router.state.location.pathname).toBe('/account/integrations');
    expect(screen.queryByRole('region', { name: '新通知' })).not.toBeInTheDocument();
    expect(requests.reads).toEqual([[entry.notificationId]]);
  });

  it('報讀的是同一句話加「帳號安全」，完整與精簡一樣', () => {
    // Arrange
    const entry = tokenCreated();
    const expected = '新通知：建立了存取權杖「CI」（讀取項目、修改項目），從 203.0.113.5，帳號安全';

    // Act
    renderToasts({ initial: [], announcement: { text: toastAnnouncement(entry, 'full'), seq: 1 } });
    act(() => {
      vi.advanceTimersByTime(SPEAK_WAIT_MS);
    });

    // Assert
    expect(toastAnnouncement(entry, 'full')).toBe(expected);
    expect(toastAnnouncement(entry, 'compact')).toBe(expected);
    expect(screen.getByText(expected)).toHaveAttribute('aria-live', 'polite');
  });
});
