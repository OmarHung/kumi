import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { ok, signedInAs } from '../../test/api';
import { server } from '../../test/server';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import {
  completeReply,
  makeAssistantMessage,
  makeMessage,
  makeAiOptions,
  makeConversationSummary,
  rejectReply,
  serveAiOptions,
  serveConversationList,
  serveConversations,
  serveCreateConversation,
  serveReply,
} from '../../test/assistant';
import { clearPanelMemory } from './panelMemory';
import { PANEL_SIZE_STORAGE_KEY } from './panelSize';
import { LAUNCHER_HIDDEN_STORAGE_KEY } from './launcherPreference';
import { PANEL_PROJECT_STORAGE_KEY } from './panelMemory';

const NEW = makeConversationSummary({ id: 'conv-new' });

function openApp(path = '/projects/SMK', options = makeAiOptions()) {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  serveAiOptions(options);
  serveConversations('SMK', []);
  serveConversations('AGY', []);
  return renderApp(path);
}

async function openPanel(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: 'AI 助理' }));
  return screen.findByRole('dialog', { name: 'AI 助理' });
}

async function goTo(router: ReturnType<typeof renderApp>['router'], path: string) {
  await act(async () => {
    await router.navigate(path);
  });
}

describe('面板的輸入框', () => {
  it('送出按法的選單鈕很窄：只留按鍵，完整的說法給報讀軟體；附加檔案也在同一列', async () => {
    // Arrange
    const { user } = openApp();

    // Act
    const dialog = await openPanel(user);

    // Assert
    const picker = await within(dialog).findByRole('combobox', { name: '送出的按法' });
    expect(picker).toHaveTextContent(/↵/);
    expect(within(picker).getByText(/Enter 送出/)).toHaveClass('visually-hidden');
    expect(within(dialog).getByRole('button', { name: '附加檔案' })).toBeInTheDocument();
  });
});

describe('右下角的按鈕', () => {
  it('組織有開放的模型：每一頁都有', async () => {
    // Arrange / Act
    openApp('/projects');

    // Assert
    expect(await screen.findByRole('button', { name: 'AI 助理' })).toBeInTheDocument();
  });

  it('沒有開放的模型、但可以接自己的 AI 帳號：也有', async () => {
    // Arrange / Act
    openApp('/projects', makeAiOptions({ available: false, canAddPersonal: true, connections: [], default: null }));

    // Assert
    expect(await screen.findByRole('button', { name: 'AI 助理' })).toBeInTheDocument();
  });

  it('沒有開放的模型、也不能接自己的：沒有', async () => {
    // Arrange / Act
    openApp('/projects', makeAiOptions({ available: false, canAddPersonal: false, connections: [], default: null }));
    await screen.findAllByRole('link', { name: /冒煙測試/ });

    // Assert
    expect(screen.queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
  });

  it('整頁的 AI 助理裡不顯示', async () => {
    // Arrange / Act
    openApp('/projects/SMK/assistant');
    await screen.findByRole('textbox', { name: '訊息' });

    // Assert
    expect(screen.queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
  });
});

describe('右下角的按鈕：藏起來與叫回來', () => {
  it('按鈕右上角的「×」藏起來、記在瀏覽器，焦點到主要內容', async () => {
    // Arrange
    const { user } = openApp();
    await screen.findByRole('button', { name: 'AI 助理' });

    // Act
    await user.click(screen.getByRole('button', { name: '隱藏 AI 助理按鈕' }));

    // Assert
    expect(screen.queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
    expect(localStorage.getItem(LAUNCHER_HIDDEN_STORAGE_KEY)).toBe('true');
    expect(document.documentElement.dataset.assistant).toBeUndefined();
    expect(document.getElementById('main-content')).toHaveFocus();
  });

  it('記住藏起來：下次載入沒有按鈕', async () => {
    // Arrange
    localStorage.setItem(LAUNCHER_HIDDEN_STORAGE_KEY, 'true');

    // Act
    openApp('/projects');
    await screen.findAllByRole('link', { name: /冒煙測試/ });

    // Assert
    expect(screen.queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
  });

  it('帳號選單的「顯示 AI 助理按鈕」叫回來，焦點在按鈕上；沒藏時選單沒有這一項', async () => {
    // Arrange
    const { user } = openApp();
    await screen.findByRole('button', { name: 'AI 助理' });
    await user.click(screen.getByRole('button', { name: /範例使用者/ }));
    expect(screen.queryByRole('menuitem', { name: '顯示 AI 助理按鈕' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: '隱藏 AI 助理按鈕' }));

    // Act
    await user.click(screen.getByRole('button', { name: /範例使用者/ }));
    await user.click(await screen.findByRole('menuitem', { name: '顯示 AI 助理按鈕' }));

    // Assert
    const button = await screen.findByRole('button', { name: 'AI 助理' });
    await waitFor(() => {
      expect(button).toHaveFocus();
    });
    expect(localStorage.getItem(LAUNCHER_HIDDEN_STORAGE_KEY)).toBeNull();
  });

  it('登出清掉面板的記憶時，不清藏起來的偏好', () => {
    // Arrange
    localStorage.setItem(LAUNCHER_HIDDEN_STORAGE_KEY, 'true');

    // Act
    clearPanelMemory();

    // Assert
    expect(localStorage.getItem(LAUNCHER_HIDDEN_STORAGE_KEY)).toBe('true');
  });
});

describe('開關與焦點', () => {
  it('送出時到了用量上限（409 ai.usage_limit_reached）：顯示伺服器的說明，輸入的字留著', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    rejectReply('SMK', 'conv-new', 409, 'ai.usage_limit_reached', '全組織這個月的 AI 回覆已經到了上限。');
    const { user } = openApp();
    const panel = await openPanel(user);
    const input = await within(panel).findByRole('textbox', { name: '訊息' });

    // Act
    await user.type(input, '留著的字{Enter}');

    // Assert
    expect(await within(panel).findByRole('alert')).toHaveTextContent('全組織這個月的 AI 回覆已經到了上限。');
    expect(input).toHaveValue('留著的字');
  });

  it('打開：非 modal 的對話框、焦點在輸入框，背後的頁面照樣能操作', async () => {
    // Arrange
    const { user } = openApp();

    // Act
    const panel = await openPanel(user);

    // Assert
    expect(panel).toHaveAttribute('aria-modal', 'false');
    expect(await within(panel).findByRole('textbox', { name: '訊息' })).toHaveFocus();
    expect(screen.queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
    expect(screen.getByRole('main')).not.toHaveAttribute('inert');
  });

  it('Esc 收起來，焦點回到按鈕；再打開還是同一份輸入', async () => {
    // Arrange
    const { user } = openApp();
    const panel = await openPanel(user);
    const input = await within(panel).findByRole('textbox', { name: '訊息' });
    await user.type(input, '草稿');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog', { name: 'AI 助理' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'AI 助理' })).toHaveFocus();
    await openPanel(user);
    expect(await screen.findByRole('textbox', { name: '訊息' })).toHaveValue('草稿');
  });

  it('關閉鈕收起來，焦點回到按鈕', async () => {
    // Arrange
    const { user } = openApp();
    const panel = await openPanel(user);

    // Act
    await user.click(within(panel).getByRole('button', { name: '關閉' }));

    // Assert
    expect(screen.queryByRole('dialog', { name: 'AI 助理' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'AI 助理' })).toHaveFocus();
  });

  it('「開成整頁」連到整頁版，面板收起來也沒有按鈕', async () => {
    // Arrange
    const { user, router } = openApp();
    const panel = await openPanel(user);

    // Act
    await user.click(within(panel).getByRole('link', { name: '開成整頁' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK/assistant');
    await screen.findByRole('textbox', { name: '訊息' });
    expect(screen.queryByRole('dialog', { name: 'AI 助理' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
  });
});

describe('開成整頁與焦點', () => {
  it('回覆中「開成整頁」停用並說明原因，不換頁', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user, router } = openApp();
    const panel = await openPanel(user);
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '你好{Enter}');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });

    // Act
    const button = await within(panel).findByRole('button', { name: '開成整頁' });
    await user.click(button);

    // Assert
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveAccessibleDescription('回覆還在進行，等它結束或先按「停止」。');
    expect(currentPath(router)).toBe('/projects/SMK');
    reply.stream()?.close();
  });

  it('不在專案頁、還沒選專案：打開時焦點在專案選單，不在調整大小的把手上', async () => {
    // Arrange
    const { user } = openApp('/projects');

    // Act
    const panel = await openPanel(user);

    // Assert
    expect(await within(panel).findByRole('combobox', { name: '專案' })).toHaveFocus();
  });

  it('選了專案之後輸入框出現，焦點不被搶走；路由上不存在的專案不記進瀏覽器', async () => {
    // Arrange
    const { user } = openApp('/projects/NOPE');

    // Act
    const panel = await openPanel(user);
    await within(panel).findByText(/專案載入失敗/);

    // Assert
    expect(localStorage.getItem(PANEL_PROJECT_STORAGE_KEY)).toBeNull();
  });

  it('登出時清掉記住的專案與大小', () => {
    // Arrange
    localStorage.setItem(PANEL_PROJECT_STORAGE_KEY, 'SMK');
    localStorage.setItem(PANEL_SIZE_STORAGE_KEY, JSON.stringify({ width: 400, height: 600 }));

    // Act
    clearPanelMemory();

    // Assert
    expect(localStorage.getItem(PANEL_PROJECT_STORAGE_KEY)).toBeNull();
    expect(localStorage.getItem(PANEL_SIZE_STORAGE_KEY)).toBeNull();
  });
});

describe('調整大小', () => {
  function handles(panel: HTMLElement) {
    return {
      width: within(panel).getByRole('separator', { name: '調整面板寬度' }),
      height: within(panel).getByRole('separator', { name: '調整面板高度' }),
    };
  }
  function stored() {
    return JSON.parse(localStorage.getItem(PANEL_SIZE_STORAGE_KEY) ?? 'null') as unknown;
  }

  it('左緣調寬、上緣調高：角色、方向、名稱、數值與控制的面板', async () => {
    // Arrange / Act
    const { user } = openApp();
    const panel = await openPanel(user);
    const { width, height } = handles(panel);

    // Assert
    expect(width).toHaveAttribute('aria-orientation', 'vertical');
    expect(width).toHaveAttribute('aria-valuenow', '400');
    expect(width).toHaveAttribute('aria-valuemin', '320');
    expect(width).toHaveAttribute('aria-valuemax', String(window.innerWidth - 48));
    expect(width).toHaveAttribute('aria-valuetext', '400 像素');
    expect(width).toHaveAttribute('aria-controls', panel.id);
    expect(height).toHaveAttribute('aria-orientation', 'horizontal');
    expect(height).toHaveAttribute('aria-valuenow', '600');
    expect(height).toHaveAttribute('aria-valuemin', '400');
    expect(height).toHaveAttribute('aria-valuemax', String(window.innerHeight - 48));
    expect(height).toHaveAttribute('aria-valuetext', '600 像素');
    expect(height).toHaveAttribute('aria-controls', panel.id);
  });

  it('左上角只給指標：不是 separator、不是停駐點', async () => {
    // Arrange / Act
    const { user } = openApp();
    const panel = await openPanel(user);

    // Assert
    expect(within(panel).getAllByRole('separator')).toHaveLength(2);
    const corner = panel.querySelector('[aria-hidden="true"][class*="resizerCorner"]');
    expect(corner).not.toBeNull();
    expect(corner).not.toHaveAttribute('tabindex');
  });

  it('左緣：←／→ 調寬（Shift 一次 64px），只動寬度、記在瀏覽器', async () => {
    // Arrange
    const { user } = openApp();
    const { width, height } = handles(await openPanel(user));

    // Act
    width.focus();
    await user.keyboard('{ArrowLeft}');
    await user.keyboard('{Shift>}{ArrowLeft}{/Shift}');
    await user.keyboard('{ArrowRight}');

    // Assert
    expect(width).toHaveAttribute('aria-valuetext', '464 像素');
    expect(height).toHaveAttribute('aria-valuetext', '600 像素');
    expect(stored()).toEqual({ width: 464, height: 600 });
  });

  it('上緣：↑／↓ 調高（往上是變高），只動高度', async () => {
    // Arrange
    const { user } = openApp();
    const { width, height } = handles(await openPanel(user));

    // Act
    height.focus();
    await user.keyboard('{ArrowUp}{ArrowUp}{ArrowDown}');
    await user.keyboard('{Shift>}{ArrowUp}{/Shift}');

    // Assert
    expect(height).toHaveAttribute('aria-valuetext', '680 像素');
    expect(width).toHaveAttribute('aria-valuetext', '400 像素');
    expect(stored()).toEqual({ width: 400, height: 680 });
  });

  it('不會小過 320×400，也不會大過畫面減邊距；Home 最小、End 最大', async () => {
    // Arrange
    localStorage.setItem(PANEL_SIZE_STORAGE_KEY, JSON.stringify({ width: 330, height: 410 }));
    const { user } = openApp();
    const { width, height } = handles(await openPanel(user));

    // Act
    width.focus();
    await user.keyboard('{Shift>}{ArrowRight}{/Shift}');
    height.focus();
    await user.keyboard('{Shift>}{ArrowDown}{/Shift}');

    // Assert
    expect(width).toHaveAttribute('aria-valuenow', '320');
    expect(height).toHaveAttribute('aria-valuenow', '400');
    await user.keyboard('{End}');
    expect(height).toHaveAttribute('aria-valuenow', String(window.innerHeight - 48));
    width.focus();
    await user.keyboard('{End}');
    expect(width).toHaveAttribute('aria-valuenow', String(window.innerWidth - 48));
    await user.keyboard('{Home}');
    expect(width).toHaveAttribute('aria-valuenow', '320');
  });

  it('下次打開用記住的大小；各自按 Enter 回到預設，兩邊都回到預設才清掉記錄', async () => {
    // Arrange
    localStorage.setItem(PANEL_SIZE_STORAGE_KEY, JSON.stringify({ width: 480, height: 520 }));
    const { user } = openApp();
    const { width, height } = handles(await openPanel(user));
    expect(width).toHaveAttribute('aria-valuetext', '480 像素');
    expect(height).toHaveAttribute('aria-valuetext', '520 像素');

    // Act
    width.focus();
    await user.keyboard('{Enter}');

    // Assert
    expect(width).toHaveAttribute('aria-valuetext', '400 像素');
    expect(stored()).toEqual({ width: 400, height: 520 });
    height.focus();
    await user.keyboard('{Enter}');
    expect(height).toHaveAttribute('aria-valuetext', '600 像素');
    expect(localStorage.getItem(PANEL_SIZE_STORAGE_KEY)).toBeNull();
  });

  it('點兩下：左緣與上緣各回到預設，左上角兩個一起', async () => {
    // Arrange
    localStorage.setItem(PANEL_SIZE_STORAGE_KEY, JSON.stringify({ width: 480, height: 520 }));
    const { user } = openApp();
    const panel = await openPanel(user);
    const { width, height } = handles(panel);

    // Act / Assert
    fireEvent.doubleClick(width);
    expect(width).toHaveAttribute('aria-valuenow', '400');
    expect(height).toHaveAttribute('aria-valuenow', '520');
    fireEvent.doubleClick(height);
    expect(height).toHaveAttribute('aria-valuenow', '600');
    expect(localStorage.getItem(PANEL_SIZE_STORAGE_KEY)).toBeNull();
  });

  it('點兩下左上角：兩個一起回到預設', async () => {
    // Arrange
    localStorage.setItem(PANEL_SIZE_STORAGE_KEY, JSON.stringify({ width: 480, height: 520 }));
    const { user } = openApp();
    const panel = await openPanel(user);
    const corner = panel.querySelector<HTMLElement>('[class*="resizerCorner"]');
    if (corner === null) throw new Error('找不到左上角');

    // Act
    fireEvent.doubleClick(corner);

    // Assert
    expect(handles(panel).width).toHaveAttribute('aria-valuenow', '400');
    expect(handles(panel).height).toHaveAttribute('aria-valuenow', '600');
    expect(localStorage.getItem(PANEL_SIZE_STORAGE_KEY)).toBeNull();
  });

  it('滑鼠拖左緣：往左變寬，放開才記住；高度不動', async () => {
    // Arrange
    const { user } = openApp();
    const { width, height } = handles(await openPanel(user));

    // Act
    fireEvent.pointerDown(width, { pointerId: 1, button: 0, clientX: 500, clientY: 300 });
    fireEvent.pointerMove(width, { pointerId: 1, clientX: 460, clientY: 250 });

    // Assert
    expect(width).toHaveAttribute('aria-valuenow', '440');
    expect(height).toHaveAttribute('aria-valuenow', '600');
    expect(localStorage.getItem(PANEL_SIZE_STORAGE_KEY)).toBeNull();
    fireEvent.pointerUp(width, { pointerId: 1 });
    expect(stored()).toEqual({ width: 440, height: 600 });
  });

  it('滑鼠拖上緣：往上變高，寬度不動', async () => {
    // Arrange
    const { user } = openApp();
    const { width, height } = handles(await openPanel(user));

    // Act
    fireEvent.pointerDown(height, { pointerId: 1, button: 0, clientX: 500, clientY: 300 });
    fireEvent.pointerMove(height, { pointerId: 1, clientX: 400, clientY: 270 });
    fireEvent.pointerUp(height, { pointerId: 1 });

    // Assert
    expect(height).toHaveAttribute('aria-valuenow', '630');
    expect(width).toHaveAttribute('aria-valuenow', '400');
    expect(stored()).toEqual({ width: 400, height: 630 });
  });

  it('滑鼠拖左上角：往左上拉兩個一起變大；拖到一半按 Esc 回到原本', async () => {
    // Arrange
    const { user } = openApp();
    const panel = await openPanel(user);
    const { width, height } = handles(panel);
    const corner = panel.querySelector<HTMLElement>('[class*="resizerCorner"]');
    if (corner === null) throw new Error('找不到左上角');

    // Act
    fireEvent.pointerDown(corner, { pointerId: 1, button: 0, clientX: 500, clientY: 500 });
    fireEvent.pointerMove(corner, { pointerId: 1, clientX: 460, clientY: 470 });

    // Assert
    expect(width).toHaveAttribute('aria-valuenow', '440');
    expect(height).toHaveAttribute('aria-valuenow', '630');
    fireEvent.pointerUp(corner, { pointerId: 1 });
    expect(stored()).toEqual({ width: 440, height: 630 });

    // Act: 再拖一次、按 Esc
    fireEvent.pointerDown(corner, { pointerId: 2, button: 0, clientX: 500, clientY: 500 });
    fireEvent.pointerMove(corner, { pointerId: 2, clientX: 400, clientY: 400 });
    fireEvent.keyDown(document, { key: 'Escape' });

    // Assert
    expect(width).toHaveAttribute('aria-valuenow', '440');
    expect(height).toHaveAttribute('aria-valuenow', '630');
    expect(stored()).toEqual({ width: 440, height: 630 });
    expect(screen.getByRole('dialog', { name: 'AI 助理' })).toBeInTheDocument();
  });

  it('只按一下沒有移動：什麼都不記', async () => {
    // Arrange
    const { user } = openApp();
    const { width } = handles(await openPanel(user));

    // Act
    fireEvent.pointerDown(width, { pointerId: 1, button: 0, clientX: 500, clientY: 300 });
    fireEvent.pointerUp(width, { pointerId: 1 });

    // Assert
    expect(localStorage.getItem(PANEL_SIZE_STORAGE_KEY)).toBeNull();
  });
});

describe('專案', () => {
  it('在專案頁跟著路由；換到別的專案頁就換過去，新的對話', async () => {
    // Arrange
    const { user, router } = openApp();
    const panel = await openPanel(user);
    expect(await within(panel).findByLabelText('目前的專案')).toHaveTextContent('冒煙測試');

    // Act
    await goTo(router, '/projects/AGY');

    // Assert
    await waitFor(() => {
      expect(within(panel).getByLabelText('目前的專案')).toHaveTextContent('接案測試');
    });
  });

  it('不在專案頁：選單列出專案，選了記在瀏覽器；下次不在專案頁時用它', async () => {
    // Arrange
    const first = openApp('/projects');
    const panel = await openPanel(first.user);
    expect(await within(panel).findByText(/先在上面選一個專案，助理會幫你調整它的設定/)).toBeInTheDocument();

    // Act
    await chooseOption(first.user, within(panel).getByRole('combobox', { name: '專案' }), '接案測試（AGY）');

    // Assert
    expect(await within(panel).findByRole('textbox', { name: '訊息' })).toBeInTheDocument();
    expect(localStorage.getItem(PANEL_PROJECT_STORAGE_KEY)).toBe('AGY');
    first.unmount();
    const second = openApp('/projects');
    const again = await openPanel(second.user);
    expect(await within(again).findByRole('combobox', { name: '專案' })).toHaveTextContent('接案測試');
  });

  it('回覆中換到別的專案頁：先不換，回覆完了才換', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user, router } = openApp();
    const panel = await openPanel(user);
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '你好{Enter}');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });
    reply.stream()?.event('start', { userMessage: { id: 'u1', role: 'user', createdAt: '2026-11-23T02:00:00Z', parts: [{ type: 'text', text: '你好' }], status: null, error: null, selection: null, connectionName: null, usage: null, attachments: [] }, assistantMessageId: 'a1' });

    // Act
    await goTo(router, '/projects/AGY');

    // Assert
    expect(within(panel).getByLabelText('目前的專案')).toHaveTextContent('冒煙測試');
    act(() => {
      reply.stream()?.event('done', { message: { id: 'a1', role: 'assistant', createdAt: '2026-11-23T02:00:01Z', parts: [{ type: 'text', text: '好' }], status: 'complete', error: null, selection: null, connectionName: null, usage: null, attachments: [] } });
      reply.stream()?.close();
    });
    await waitFor(() => {
      expect(within(panel).getByLabelText('目前的專案')).toHaveTextContent('接案測試');
    });
  });
});

describe('送訊息與串流', () => {
  it('開新的對話不換網址；回覆長出來；關掉面板、換頁都不中斷', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user, router } = openApp();
    const panel = await openPanel(user);
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '拆驗收{Enter}');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });
    completeStart(reply.stream());

    // Act: 關掉面板、換頁
    await user.click(within(panel).getByRole('button', { name: '關閉' }));
    await goTo(router, '/projects/AGY');
    reply.stream()?.event('text', { delta: '先讀設定。' });
    await user.click(screen.getByRole('button', { name: 'AI 助理' }));

    // Assert
    expect(await screen.findByText('先讀設定。')).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/AGY');
    expect(reply.sent[0]?.body.text).toBe('拆驗收');
    reply.stream()?.close();
  });

  it('完整的一輪：回覆與模型小字', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好，我先讀目前的設定。');
    });
    const { user } = openApp();
    const panel = await openPanel(user);

    // Act
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '拆驗收{Enter}');

    // Assert
    expect(await within(panel).findByText('好，我先讀目前的設定。')).toBeInTheDocument();
    expect(await within(panel).findByText('Claude Opus 5.5 · 高')).toBeInTheDocument();
  });
});

function completeStart(stream: ReturnType<typeof serveReply>['stream'] extends () => infer R ? R : never) {
  stream?.event('start', {
    userMessage: { id: 'u1', role: 'user', createdAt: '2026-11-23T02:00:00Z', parts: [{ type: 'text', text: '拆驗收' }], status: null, error: null, selection: null, connectionName: null, usage: null, attachments: [] },
    assistantMessageId: 'a1',
  });
}

describe('對話選單', () => {
  it('列出這個專案的對話；選一則就換過去', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    serveConversations('SMK', [makeConversationSummary({ id: 'conv-9', title: '加客戶欄位' })]);
    const { user } = renderApp('/projects/SMK');
    const panel = await openPanel(user);

    // Act
    await user.click(await within(panel).findByRole('button', { name: '對話' }));
    const item = await screen.findByRole('menuitemradio', { name: '加客戶欄位' });
    expect(screen.getByRole('menuitemradio', { name: '新的對話' })).toHaveAttribute('aria-checked', 'true');
    await user.click(item);

    // Assert
    expect(within(panel).getByRole('link', { name: '開成整頁' })).toHaveAttribute('href', '/projects/SMK/assistant/conv-9');
  });

  it('列出所有專案的對話，依專案分組：目前的專案在最前面，不選專案的叫「不選專案」', async () => {
    // Arrange
    const { user } = openApp();
    serveConversations('SMK', [makeConversationSummary({ id: 'conv-s', title: '加客戶欄位', updatedAt: '2026-11-25T02:00:00Z' })]);
    serveConversations('AGY', [makeConversationSummary({ id: 'conv-a', title: '拆驗收', updatedAt: '2026-11-26T02:00:00Z' })]);
    serveConversations(null, [makeConversationSummary({ id: 'conv-o', title: '開新專案', updatedAt: '2026-11-24T02:00:00Z' })]);
    const panel = await openPanel(user);

    // Act
    await user.click(await within(panel).findByRole('button', { name: '對話' }));
    await screen.findByRole('menuitemradio', { name: '拆驗收' });

    // Assert
    const names = screen.getAllByRole('menuitemradio').map((item) => item.textContent);
    expect(names).toEqual(['新的對話', '加客戶欄位', '拆驗收', '開新專案']);
    expect(screen.getByRole('menuitemradio', { name: '加客戶欄位' })).toHaveAccessibleDescription('冒煙測試');
    expect(screen.getByRole('menuitemradio', { name: '拆驗收' })).toHaveAccessibleDescription('接案測試');
    expect(screen.getByRole('menuitemradio', { name: '開新專案' })).toHaveAccessibleDescription('不選專案');
  });

  it('在專案頁選了別的專案的對話：面板換到那個專案，頁首換成專案的選單，選得回來', async () => {
    // Arrange
    const { user } = openApp();
    serveConversations('AGY', [makeConversationSummary({ id: 'conv-a', title: '拆驗收' })]);
    const panel = await openPanel(user);
    await user.click(await within(panel).findByRole('button', { name: '對話' }));

    // Act
    await user.click(await screen.findByRole('menuitemradio', { name: '拆驗收' }));

    // Assert
    expect(await within(panel).findByRole('combobox', { name: '專案' })).toHaveTextContent('接案測試');
    expect(within(panel).getByRole('link', { name: '開成整頁' })).toHaveAttribute('href', '/projects/AGY/assistant/conv-a');
  });

  it('不選專案時選了專案的對話：面板換到那個專案', async () => {
    // Arrange
    const { user } = openApp('/my-work');
    serveConversations(null, []);
    serveConversations('SMK', [makeConversationSummary({ id: 'conv-s', title: '加客戶欄位' })]);
    const panel = await openPanel(user);
    await chooseOption(user, await within(panel).findByRole('combobox', { name: '專案' }), '不選專案');
    await user.click(await within(panel).findByRole('button', { name: '對話' }));

    // Act
    await user.click(await screen.findByRole('menuitemradio', { name: '加客戶欄位' }));

    // Assert
    expect(await within(panel).findByRole('combobox', { name: '專案' })).toHaveTextContent('冒煙測試');
    expect(within(panel).getByRole('link', { name: '開成整頁' })).toHaveAttribute('href', '/projects/SMK/assistant/conv-s');
  });
});

describe('模型選單與沒有模型', () => {
  it('兩個連線分組，自己的連線在組名後面加「（我的）」', async () => {
    // Arrange
    const base = makeAiOptions();
    const options = makeAiOptions({
      connections: [
        ...base.connections,
        { id: 'conn-2', name: 'OpenRouter', kind: 'openrouter', personal: true, hosted: false, notice: null, models: [{ id: 'm1', displayName: 'Model One', efforts: [], defaultEffort: null }] },
      ],
    });
    const { user } = openApp('/projects/SMK', options);
    const panel = await openPanel(user);

    // Act
    await user.click(await within(panel).findByRole('combobox', { name: '模型' }));

    // Assert
    expect(await screen.findByText('OpenRouter（我的）')).toBeInTheDocument();
    expect(screen.getByText('Anthropic')).toBeInTheDocument();
  });

  it('組織沒設定 AI、可以接自己的：說明與連到「我的 AI 助理」的連結', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK', makeAiOptions({ available: false, canAddPersonal: true, connections: [], default: null }));

    // Act
    const panel = await openPanel(user);

    // Assert
    expect(await within(panel).findByText('組織還沒設定 AI。你可以在「我的 AI 助理」接自己的 AI 帳號。')).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: '前往「我的 AI 助理」' })).toHaveAttribute('href', '/account/ai');
  });

  it('沒有 blueprint.read：說明沒有權限', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read_own'] })] });
    serveAiOptions();
    serveConversations('SMK', []);
    const { user } = renderApp('/projects/SMK');

    // Act
    const panel = await openPanel(user);

    // Assert
    expect(await within(panel).findByText(/沒有讀取這個專案設定的權限/)).toBeInTheDocument();
  });
});

describe('第一則訊息之後的對話選單', () => {
  it('勾選剛開的那則；選「新的對話」回到空白，下一則訊息開另一則對話', async () => {
    // Arrange
    const created: string[] = [];
    let counter = 0;
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    let list = [] as ReturnType<typeof makeConversationSummary>[];
    serveConversationList('SMK', () => list);
    server.use(
      http.post('/v1/projects/SMK/assistant/conversations', () => {
        counter += 1;
        const summary = makeConversationSummary({ id: `conv-${String(counter)}`, title: null });
        created.push(summary.id);
        list = [...list, summary];
        return ok(summary, 201);
      }),
    );
    for (const id of ['conv-1', 'conv-2']) {
      serveReply('SMK', id, (stream) => {
        completeReply(stream, `回覆 ${id}`);
      });
    }
    const { user } = renderApp('/projects/SMK');
    const panel = await openPanel(user);
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '第一則{Enter}');
    await within(panel).findByText('回覆 conv-1');

    // Act
    await user.click(within(panel).getByRole('button', { name: '對話' }));

    // Assert: 勾選剛開的那則（還沒有標題，不能也叫「新的對話」），不是「新的對話」
    expect(await screen.findByRole('menuitemradio', { name: '（還沒有標題）' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('menuitemradio', { name: '新的對話' })).toHaveAttribute('aria-checked', 'false');

    // Act: 選「新的對話」再送一則
    await user.click(screen.getByRole('menuitemradio', { name: '新的對話' }));
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '第二則{Enter}');

    // Assert
    expect(await within(panel).findByText('回覆 conv-2')).toBeInTheDocument();
    expect(created).toEqual(['conv-1', 'conv-2']);
    expect(within(panel).queryByText('回覆 conv-1')).not.toBeInTheDocument();
  });

  it('回覆結束時伺服器還沒放掉租約：清單輪詢到放掉為止，標題後面不留「（回覆中）」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    let isRunning = true;
    serveConversationList('SMK', () => [makeConversationSummary({ id: 'conv-1', title: '加欄位', isRunning })]);
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-1', title: '加欄位', isRunning: true }));
    const reply = serveReply('SMK', 'conv-1', () => undefined);
    const { user } = renderApp('/projects/SMK');
    const panel = await openPanel(user);
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '你好{Enter}');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });
    reply.stream()?.event('start', { userMessage: makeMessage(), assistantMessageId: 'msg-a1' });
    await user.click(within(panel).getByRole('button', { name: '對話' }));
    expect(await screen.findByRole('menuitemradio', { name: '加欄位（回覆中）' })).toBeInTheDocument();
    await user.keyboard('{Escape}');

    // Act: 串流先結束，伺服器稍晚才放掉租約
    act(() => {
      completeReply(reply.stream() as NonNullable<ReturnType<typeof reply.stream>>, '好', makeMessage(), makeAssistantMessage());
    });
    setTimeout(() => {
      isRunning = false;
    }, 300);
    await waitFor(() => {
      expect(within(panel).getByRole('button', { name: '送出' })).toBeInTheDocument();
    });
    await user.click(within(panel).getByRole('button', { name: '對話' }));

    // Assert
    expect(await screen.findByRole('menuitemradio', { name: '加欄位' }, { timeout: 8000 })).toBeInTheDocument();
    expect(screen.queryByRole('menuitemradio', { name: '加欄位（回覆中）' })).not.toBeInTheDocument();
  }, 20_000);
});
