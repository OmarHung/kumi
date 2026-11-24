import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FloatingPanel } from './FloatingPanel';
import { FLOATING_PANEL_WIDTH_STORAGE_KEY } from './floatingPanelWidth';

// jsdom 沒有版面：視窗量成 1200px 寬；遮罩裡放得下的寬度（畫面寬減掉兩邊的留白）是 1652px。
// 根元素的字級讀不到，當成 16px：最窄 40rem＝640。
const RENDERED = 1200;
const AVAILABLE = 1652;
const MIN = 640;

function renderPanel() {
  const onClose = vi.fn();
  render(
    <FloatingPanel label="項目 AGY-3" title="AGY-3" onClose={onClose}>
      內容
    </FloatingPanel>,
  );
  return {
    user: userEvent.setup(),
    onClose,
    dialog: screen.getByRole('dialog'),
    handle: screen.getByRole('separator', { name: '調整視窗寬度' }),
  };
}

function widthOf(dialog: HTMLElement): string {
  return dialog.style.getPropertyValue('--floating-panel-width');
}

/** 左緣的把手：只給滑鼠（aria-hidden），鍵盤用右緣那一個。 */
function startHandle(): HTMLElement {
  const handles = screen.getAllByRole('separator', { hidden: true });
  const start = handles.find((handle) => handle.getAttribute('aria-hidden') === 'true');
  if (start === undefined) throw new Error('找不到左緣的把手');
  return start;
}

function drag(handle: HTMLElement, from: number, to: number, release = true) {
  fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: from });
  fireEvent.pointerMove(handle, { pointerId: 1, clientX: to });
  if (release) fireEvent.pointerUp(handle, { pointerId: 1, clientX: to });
}

beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 0, RENDERED, 800));
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(AVAILABLE);
});

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe('FloatingPanel：拖拉調整寬度（docs/DESIGN.md 第 7 節 FloatingPanel）', () => {
  it('右緣的把手是可以聚焦的分隔線，數值是視窗的寬度；左緣的把手只給滑鼠', () => {
    // Arrange / Act
    const { handle } = renderPanel();

    // Assert
    expect(handle).toHaveAttribute('tabindex', '0');
    expect(handle).toHaveAttribute('aria-valuenow', String(RENDERED));
    expect(handle).toHaveAttribute('aria-valuemin', String(MIN));
    expect(handle).toHaveAttribute('aria-valuemax', String(AVAILABLE));
    expect(startHandle()).not.toHaveAttribute('tabindex');
  });

  it('鍵盤：→ 加寬 16px、Shift+→ 一次 64px、← 變窄；記在這個瀏覽器，視窗用記住的寬度', async () => {
    // Arrange
    const { user, dialog, handle } = renderPanel();
    handle.focus();

    // Act
    await user.keyboard('{ArrowRight}{Shift>}{ArrowRight}{/Shift}{ArrowLeft}');

    // Assert
    expect(widthOf(dialog)).toBe('1264px');
    expect(handle).toHaveAttribute('aria-valuenow', '1264');
    expect(localStorage.getItem(FLOATING_PANEL_WIDTH_STORAGE_KEY)).toBe('1264');
  });

  it('Home 最窄、End 最寬（畫面寬減掉兩邊的留白），Enter 回到預設並拿掉記住的', async () => {
    // Arrange
    const { user, dialog, handle } = renderPanel();
    handle.focus();

    // Act / Assert
    await user.keyboard('{Home}');
    expect(widthOf(dialog)).toBe(`${String(MIN)}px`);
    await user.keyboard('{End}');
    expect(widthOf(dialog)).toBe(`${String(AVAILABLE)}px`);
    await user.keyboard('{Enter}');
    expect(widthOf(dialog)).toBe('');
    expect(localStorage.getItem(FLOATING_PANEL_WIDTH_STORAGE_KEY)).toBeNull();
  });

  it('拖右緣往外 50px：兩邊一起動，寬度多 100px；放開才記住', () => {
    // Arrange
    const { dialog, handle } = renderPanel();

    // Act
    drag(handle, 1200, 1250, false);

    // Assert
    expect(widthOf(dialog)).toBe('1300px');
    expect(localStorage.getItem(FLOATING_PANEL_WIDTH_STORAGE_KEY)).toBeNull();
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 1250 });
    expect(localStorage.getItem(FLOATING_PANEL_WIDTH_STORAGE_KEY)).toBe('1300');
  });

  it('拖左緣往外（往左）也是加寬；拖得比最窄還窄時停在最窄', () => {
    // Arrange
    const { dialog } = renderPanel();

    // Act / Assert
    drag(startHandle(), 300, 250);
    expect(widthOf(dialog)).toBe('1300px');
    drag(startHandle(), 250, 900);
    expect(widthOf(dialog)).toBe(`${String(MIN)}px`);
  });

  it('點兩下把手回到預設寬度', () => {
    // Arrange
    localStorage.setItem(FLOATING_PANEL_WIDTH_STORAGE_KEY, '1400');
    const { dialog, handle } = renderPanel();

    // Act
    fireEvent.doubleClick(handle);

    // Assert
    expect(widthOf(dialog)).toBe('');
    expect(localStorage.getItem(FLOATING_PANEL_WIDTH_STORAGE_KEY)).toBeNull();
  });

  it('記住的寬度打開就用；讀不懂的當成沒有設定', () => {
    // Arrange
    localStorage.setItem(FLOATING_PANEL_WIDTH_STORAGE_KEY, '1400');

    // Act
    const { dialog, handle } = renderPanel();

    // Assert
    expect(widthOf(dialog)).toBe('1400px');
    expect(handle).toHaveAttribute('aria-valuenow', '1400');
  });

  it('記住的寬度比最窄還窄（被改壞、字級調大了）：畫出來的是最窄，記住的不改', () => {
    // Arrange
    localStorage.setItem(FLOATING_PANEL_WIDTH_STORAGE_KEY, '5');

    // Act
    const { dialog, handle } = renderPanel();

    // Assert
    expect(widthOf(dialog)).toBe(`${String(MIN)}px`);
    expect(handle).toHaveAttribute('aria-valuenow', String(MIN));
    expect(localStorage.getItem(FLOATING_PANEL_WIDTH_STORAGE_KEY)).toBe('5');
  });

  it('拖曳中按 Esc：回到原本的寬度、不記，視窗不關', async () => {
    // Arrange
    const { user, dialog, handle, onClose } = renderPanel();
    drag(handle, 1200, 1300, false);

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(widthOf(dialog)).toBe('');
    expect(localStorage.getItem(FLOATING_PANEL_WIDTH_STORAGE_KEY)).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
  });
});
