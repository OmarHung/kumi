import { fireEvent, render, renderHook, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import {
  ariaKeyShortcuts,
  formatShortcut,
  isMac,
  listShortcuts,
  matchesShortcut,
  SHORTCUTS,
  useShortcut,
  type ShortcutSpec,
} from './shortcuts';

const OPEN_JUMP = SHORTCUTS.openJump;
const ITEM_NEXT = SHORTCUTS.itemNext;
const CLOSE_PANEL = SHORTCUTS.closePanel;

function keyEvent(init: KeyboardEventInit): KeyboardEvent {
  return new KeyboardEvent('keydown', init);
}

/** 掛上 useShortcut，回傳處理函式（測試環境的 navigator.platform 是空字串：不是 Mac，mod 是 Ctrl）。 */
function listen(spec: ShortcutSpec, options?: { readonly isEnabled?: boolean }) {
  const handler = vi.fn();
  const hook = renderHook(
    ({ isEnabled }: { readonly isEnabled?: boolean }) => {
      useShortcut(spec, handler, { isEnabled });
    },
    { initialProps: { isEnabled: options?.isEnabled } },
  );
  return { handler, ...hook };
}

describe('isMac', () => {
  it('Mac、iPhone、iPad 算 Mac；Windows、Linux 不算', () => {
    // Arrange
    const platforms = ['MacIntel', 'macOS', 'iPhone', 'iPad', 'Win32', 'Windows', 'Linux x86_64', ''];

    // Act
    const results = platforms.map((platform) => isMac(platform));

    // Assert
    expect(results).toEqual([true, true, true, true, false, false, false, false]);
  });

  it('沒給平台時看瀏覽器：測試環境不是 Mac', () => {
    // Arrange、Act
    const result = isMac();

    // Assert
    expect(result).toBe(false);
  });
});

describe('matchesShortcut', () => {
  it('⌘K：Mac 是 metaKey，其他平台是 ctrlKey', () => {
    // Arrange
    const meta = keyEvent({ key: 'k', metaKey: true });
    const ctrl = keyEvent({ key: 'k', ctrlKey: true });

    // Act
    const results = [
      matchesShortcut(OPEN_JUMP, meta, true),
      matchesShortcut(OPEN_JUMP, ctrl, true),
      matchesShortcut(OPEN_JUMP, ctrl, false),
      matchesShortcut(OPEN_JUMP, meta, false),
    ];

    // Assert
    expect(results).toEqual([true, false, true, false]);
  });

  it('修飾鍵要剛好一樣：多按了 Shift 或 Alt 就不是同一個快捷鍵', () => {
    // Arrange
    const withShift = keyEvent({ key: 'K', ctrlKey: true, shiftKey: true });
    const withAlt = keyEvent({ key: 'k', ctrlKey: true, altKey: true });
    const bare = keyEvent({ key: 'k' });

    // Act
    const results = [withShift, withAlt, bare].map((event) => matchesShortcut(OPEN_JUMP, event, false));

    // Assert
    expect(results).toEqual([false, false, false]);
  });

  it('字母不分大小寫（大寫鎖定）；別的鍵盤配置打出別的字時看實體按鍵', () => {
    // Arrange
    const capsLock = keyEvent({ key: 'K', ctrlKey: true });
    const russian = keyEvent({ key: 'л', code: 'KeyK', ctrlKey: true });
    const otherKey = keyEvent({ key: 'j', code: 'KeyK', ctrlKey: true });

    // Act
    const results = [capsLock, russian, otherKey].map((event) => matchesShortcut(OPEN_JUMP, event, false));

    // Assert
    expect(results).toEqual([true, true, false]);
  });

  it('Alt+↓ 與 Esc', () => {
    // Arrange
    const altDown = keyEvent({ key: 'ArrowDown', altKey: true });
    const down = keyEvent({ key: 'ArrowDown' });
    const escape = keyEvent({ key: 'Escape' });

    // Act
    const results = [
      matchesShortcut(ITEM_NEXT, altDown, true),
      matchesShortcut(ITEM_NEXT, down, true),
      matchesShortcut(CLOSE_PANEL, escape, false),
    ];

    // Assert
    expect(results).toEqual([true, false, true]);
  });

  it('沒有 key 的 keydown（瀏覽器的自動填入會送）不算', () => {
    // Arrange
    const autofill = new KeyboardEvent('keydown');
    Object.defineProperty(autofill, 'key', { value: undefined });

    // Act
    const result = matchesShortcut(CLOSE_PANEL, autofill, false);

    // Assert
    expect(result).toBe(false);
  });
});

describe('formatShortcut、ariaKeyShortcuts', () => {
  it('Mac 用符號（⌃⌥⇧⌘ 的順序），其他平台用「Ctrl+」', () => {
    // Arrange
    const shiftAlt: ShortcutSpec = { key: 'p', modifiers: ['shift', 'alt', 'mod'], description: '展示用' };

    // Act
    const mac = [OPEN_JUMP, ITEM_NEXT, SHORTCUTS.itemPrev, CLOSE_PANEL, shiftAlt].map((spec) => formatShortcut(spec, true));
    const others = [OPEN_JUMP, ITEM_NEXT, SHORTCUTS.itemPrev, CLOSE_PANEL, shiftAlt].map((spec) => formatShortcut(spec, false));

    // Assert
    expect(mac).toEqual(['⌘K', '⌥↓', '⌥↑', 'Esc', '⌥⇧⌘P']);
    expect(others).toEqual(['Ctrl+K', 'Alt+↓', 'Alt+↑', 'Esc', 'Ctrl+Alt+Shift+P']);
  });

  it('aria-keyshortcuts 用 ARIA 的寫法', () => {
    // Arrange、Act
    const values = [ariaKeyShortcuts(OPEN_JUMP, true), ariaKeyShortcuts(OPEN_JUMP, false), ariaKeyShortcuts(ITEM_NEXT, false)];

    // Assert
    expect(values).toEqual(['Meta+K', 'Control+K', 'Alt+ArrowDown']);
  });

  it('沒給平台時看瀏覽器：測試環境用 Ctrl', () => {
    // Arrange、Act
    const values = [formatShortcut(OPEN_JUMP), ariaKeyShortcuts(OPEN_JUMP)];

    // Assert
    expect(values).toEqual(['Ctrl+K', 'Control+K']);
  });
});

describe('登記表', () => {
  it('listShortcuts 列出全部，給之後的說明用', () => {
    // Arrange、Act
    const entries = listShortcuts(true);

    // Assert
    expect(entries).toEqual([
      { id: 'openJump', description: '搜尋或跳到…', keys: '⌘K' },
      { id: 'itemPrev', description: '上一個項目', keys: '⌥↑' },
      { id: 'itemNext', description: '下一個項目', keys: '⌥↓' },
      { id: 'closePanel', description: '關閉面板', keys: 'Esc' },
    ]);
  });

  it('不用瀏覽器與報讀軟體保留的組合', () => {
    // Arrange：網址列、分頁、上一頁下一頁、重新整理、尋找、列印、書籤；VoiceOver 的 Ctrl+Option、NVDA／JAWS 的 Insert。
    const reservedWithMod = new Set(['l', 't', 'w', 'n', 'r', 'f', 'p', 'd', 'h', 'j', 's', 'o', 'q']);
    const reservedWithAlt = new Set(['ArrowLeft', 'ArrowRight', 'Home', 'd', 'f']);

    // Act
    const problems = Object.values(SHORTCUTS).filter((spec: ShortcutSpec) => {
      const modifiers = spec.modifiers ?? [];
      const hasMod = modifiers.some((modifier) => modifier === 'mod' || modifier === 'ctrl' || modifier === 'meta');
      const hasAlt = modifiers.includes('alt');
      return (
        (hasMod && reservedWithMod.has(spec.key)) ||
        (hasAlt && !hasMod && reservedWithAlt.has(spec.key)) ||
        (modifiers.includes('ctrl') && hasAlt) ||
        spec.key === 'Insert' ||
        spec.key === '/'
      );
    });

    // Assert
    expect(problems).toEqual([]);
  });
});

describe('useShortcut', () => {
  it('按下就呼叫，並擋下瀏覽器預設的動作', () => {
    // Arrange
    const { handler } = listen(OPEN_JUMP);

    // Act
    const isNotPrevented = fireEvent.keyDown(document.body, { key: 'k', ctrlKey: true });

    // Assert
    expect(handler).toHaveBeenCalledTimes(1);
    expect(isNotPrevented).toBe(false);
  });

  it('帶 Ctrl／⌘ 的在輸入框裡照樣可用', () => {
    // Arrange
    const { handler } = listen(OPEN_JUMP);
    render(<input aria-label="標題" />);

    // Act
    fireEvent.keyDown(screen.getByRole('textbox', { name: '標題' }), { key: 'k', ctrlKey: true });

    // Assert
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('單一按鍵與只帶 Alt 的，在輸入框、文字框、下拉選單、contenteditable 裡不作用', () => {
    // Arrange
    const escape = listen(CLOSE_PANEL).handler;
    const next = listen(ITEM_NEXT).handler;
    render(
      <>
        <input aria-label="標題" />
        <textarea aria-label="留言" />
        <select aria-label="狀態">
          <option>進行中</option>
        </select>
        <div contentEditable suppressContentEditableWarning>
          <p data-testid="editable">說明</p>
        </div>
      </>,
    );
    const targets = [
      screen.getByRole('textbox', { name: '標題' }),
      screen.getByRole('textbox', { name: '留言' }),
      screen.getByRole('combobox', { name: '狀態' }),
      screen.getByTestId('editable'),
    ];

    // Act
    targets.forEach((target) => {
      fireEvent.keyDown(target, { key: 'Escape' });
      fireEvent.keyDown(target, { key: 'ArrowDown', altKey: true });
    });

    // Assert
    expect(escape).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('自己做的下拉（components/Select：role="combobox" 的按鈕）和原生的下拉一樣，單一按鍵與只帶 Alt 的不作用', () => {
    // Arrange
    const escape = listen(CLOSE_PANEL).handler;
    const next = listen(ITEM_NEXT).handler;
    render(
      <button type="button" role="combobox" aria-label="負責人" aria-expanded="false">
        範例成員
      </button>,
    );
    const select = screen.getByRole('combobox', { name: '負責人' });

    // Act
    fireEvent.keyDown(select, { key: 'Escape' });
    fireEvent.keyDown(select, { key: 'ArrowDown', altKey: true });

    // Assert
    expect(escape).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('勾選框、按鈕上照樣可用；allowInInputs 的在輸入框裡也可用', () => {
    // Arrange
    const escape = listen(CLOSE_PANEL).handler;
    const inInputs = listen({ ...ITEM_NEXT, allowInInputs: true }).handler;
    render(
      <>
        <input type="checkbox" aria-label="只看我的" />
        <button type="button">下一個</button>
        <input aria-label="標題" />
      </>,
    );

    // Act
    fireEvent.keyDown(screen.getByRole('checkbox', { name: '只看我的' }), { key: 'Escape' });
    fireEvent.keyDown(screen.getByRole('button', { name: '下一個' }), { key: 'Escape' });
    fireEvent.keyDown(screen.getByRole('textbox', { name: '標題' }), { key: 'ArrowDown', altKey: true });

    // Assert
    expect(escape).toHaveBeenCalledTimes(2);
    expect(inInputs).toHaveBeenCalledTimes(1);
  });

  it('輸入法選字時不作用（isComposing，或 Safari 的 keyCode 229）', () => {
    // Arrange
    const { handler } = listen(OPEN_JUMP);

    // Act
    fireEvent.keyDown(document.body, { key: 'k', ctrlKey: true, isComposing: true });
    fireEvent.keyDown(document.body, { key: 'k', ctrlKey: true, keyCode: 229 });

    // Assert
    expect(handler).not.toHaveBeenCalled();
  });

  it('內層已經處理過的（preventDefault 或 stopPropagation）不再處理', () => {
    // Arrange
    const { handler } = listen(CLOSE_PANEL);
    render(
      <>
        <button
          type="button"
          onKeyDown={(event) => {
            event.preventDefault();
          }}
        >
          取消編輯
        </button>
        <button
          type="button"
          onKeyDown={(event) => {
            event.stopPropagation();
          }}
        >
          關上選單
        </button>
      </>,
    );

    // Act
    fireEvent.keyDown(screen.getByRole('button', { name: '取消編輯' }), { key: 'Escape' });
    fireEvent.keyDown(screen.getByRole('button', { name: '關上選單' }), { key: 'Escape' });

    // Assert
    expect(handler).not.toHaveBeenCalled();
  });

  it('處理了就停止往外傳：視窗上的聽眾收不到', () => {
    // Arrange
    listen(CLOSE_PANEL);
    const onWindow = vi.fn();
    window.addEventListener('keydown', onWindow);

    // Act
    fireEvent.keyDown(document.body, { key: 'Escape' });
    window.removeEventListener('keydown', onWindow);

    // Assert
    expect(onWindow).not.toHaveBeenCalled();
  });

  it('強制回應的對話框開著時，單一按鍵與只帶 Alt 的不作用（焦點在對話框裡或掉到頁面本體都一樣）；帶 Ctrl／⌘ 的照樣作用', () => {
    // Arrange
    const next = listen(ITEM_NEXT).handler;
    const escape = listen(CLOSE_PANEL).handler;
    const jump = listen(OPEN_JUMP).handler;
    render(
      <div role="dialog" aria-modal="true" aria-label="刪除 AGY-3？">
        <button type="button">刪除</button>
      </div>,
    );
    const inside = screen.getByRole('button', { name: '刪除' });

    // Act
    const isAllowed = fireEvent.keyDown(inside, { key: 'ArrowDown', altKey: true });
    fireEvent.keyDown(document.body, { key: 'Escape' });
    fireEvent.keyDown(inside, { key: 'k', ctrlKey: true });

    // Assert
    expect(next).not.toHaveBeenCalled();
    expect(escape).not.toHaveBeenCalled();
    expect(isAllowed).toBe(true);
    expect(jump).toHaveBeenCalledTimes(1);
  });

  it('不是強制回應的對話框（role="dialog" 沒有 aria-modal）不擋', () => {
    // Arrange
    const next = listen(ITEM_NEXT).handler;
    render(<div role="dialog" aria-label="顯示的欄位" />);

    // Act
    fireEvent.keyDown(document.body, { key: 'ArrowDown', altKey: true });

    // Assert
    expect(next).toHaveBeenCalledTimes(1);
  });

  it('同一個按鍵登記了兩次時只有一個處理', () => {
    // Arrange
    const first = listen(CLOSE_PANEL).handler;
    const second = listen(CLOSE_PANEL).handler;

    // Act
    fireEvent.keyDown(document.body, { key: 'Escape' });

    // Assert
    expect(first.mock.calls.length + second.mock.calls.length).toBe(1);
  });

  it('isEnabled 是 false 時不作用、不擋預設動作；卸下之後也不作用', () => {
    // Arrange
    const { handler, rerender, unmount } = listen(ITEM_NEXT, { isEnabled: false });

    // Act
    const isNotPrevented = fireEvent.keyDown(document.body, { key: 'ArrowDown', altKey: true });
    rerender({ isEnabled: true });
    fireEvent.keyDown(document.body, { key: 'ArrowDown', altKey: true });
    unmount();
    fireEvent.keyDown(document.body, { key: 'ArrowDown', altKey: true });

    // Assert
    expect(isNotPrevented).toBe(true);
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('呼叫的是最新的處理函式', () => {
    // Arrange
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = renderHook(
      ({ handler }: { readonly handler: () => void }) => {
        useShortcut(CLOSE_PANEL, handler);
      },
      { initialProps: { handler: first } },
    );

    // Act
    rerender({ handler: second });
    fireEvent.keyDown(document.body, { key: 'Escape' });

    // Assert
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });
});
