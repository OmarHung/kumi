import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SidePanel } from './SidePanel';
import { SIDE_PANEL_BESIDE_QUERY, SidePanelLayout } from './SidePanelLayout';
import { SIDE_PANEL_WIDTH_STORAGE_KEY } from './sidePanelWidth';

// jsdom 的根元素字級讀不到，當成 16px：最窄 25rem＝400、預設 28rem＝448、清單至少留 30rem＝480；
// 留白（column-gap）讀不到是 0，蓋在清單上時最寬是清單那一塊減 16rem＝256。
const MIN = 400;
const DEFAULT = 448;
const LIST_MIN = 480;
const OVERLAY_KEEP = 256;

interface Media {
  readonly isBeside?: boolean;
  readonly isCoarse?: boolean;
}

/** 80rem 以上排成右邊一欄；觸控（pointer: coarse）。 */
function stubMedia({ isBeside = true, isCoarse = false }: Media = {}) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: (isBeside && query === SIDE_PANEL_BESIDE_QUERY) || (isCoarse && query === '(pointer: coarse)'),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

/** 畫面上每一個元素都量成這個大小與位置（jsdom 沒有版面，畫面高 768px）；面板只看清單那一塊。 */
function stubLayout(width: number, top = 0, height = 2000) {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, top, width, height));
}

/** 假的 ResizeObserver：測試用 resize() 模擬清單那一塊變了寬度（視窗變小、收起側欄）。 */
function stubResizeObserver() {
  const callbacks: (() => void)[] = [];
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(callback: () => void) {
        callbacks.push(callback);
      }
      observe() {
        return undefined;
      }
      disconnect() {
        return undefined;
      }
    },
  );
  return {
    resize: () => {
      act(() => {
        callbacks.forEach((callback) => {
          callback();
        });
      });
    },
  };
}

function renderLayout(layout: 'fill' | 'page' = 'fill') {
  const onClose = vi.fn();
  render(
    <SidePanelLayout
      layout={layout}
      panel={
        <SidePanel label="項目 AGY-3" title="AGY-3" onClose={onClose}>
          內容
        </SidePanel>
      }
    >
      <button type="button">AGY-2</button>
    </SidePanelLayout>,
  );
  return {
    user: userEvent.setup(),
    onClose,
    handle: screen.getByRole('separator', { name: '調整面板寬度' }),
    panel: screen.getByRole('complementary', { name: '項目 AGY-3' }),
  };
}

function widthOf(handle: HTMLElement): number {
  return Number(handle.getAttribute('aria-valuenow'));
}

function styleOf(panel: HTMLElement, name: string): string | undefined {
  return panel.closest<HTMLElement>('[data-has-panel="true"]')?.style.getPropertyValue(name);
}

function stored(): string | null {
  return localStorage.getItem(SIDE_PANEL_WIDTH_STORAGE_KEY);
}

// jsdom 沒有 setPointerCapture：拖曳要用到。
const setPointerCapture = vi.fn();

beforeEach(() => {
  setPointerCapture.mockClear();
  Element.prototype.setPointerCapture = setPointerCapture;
});

afterEach(() => {
  vi.unstubAllGlobals();
  Reflect.deleteProperty(Element.prototype, 'setPointerCapture');
});

describe('面板的寬度：把手', () => {
  it('面板左緣是調整寬度的把手：垂直的 separator，停得到，帶著目前的寬度與範圍，控制的是面板', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);

    // Act
    const { handle, panel } = renderLayout();

    // Assert
    expect(handle).toHaveAttribute('aria-orientation', 'vertical');
    expect(handle).toHaveAttribute('tabindex', '0');
    expect(handle).toHaveAttribute('aria-valuenow', String(DEFAULT));
    expect(handle).toHaveAttribute('aria-valuetext', `${DEFAULT} 像素`);
    expect(handle).toHaveAttribute('aria-valuemin', String(MIN));
    expect(handle).toHaveAttribute('aria-valuemax', String(1200 - LIST_MIN));
    expect(handle).toHaveAttribute('aria-controls', panel.id);
    expect(panel.id).not.toBe('');
  });

  it('蓋在清單上（80rem 以下）時最寬是內容區減 16rem：大螢幕記住的寬度到筆電上不會蓋滿清單', () => {
    // Arrange
    stubMedia({ isBeside: false });
    stubLayout(800);

    // Act
    const { handle } = renderLayout();

    // Assert
    expect(handle).toHaveAttribute('aria-valuemax', String(800 - OVERLAY_KEEP));
  });

  it('觸控時最窄是 32rem（標題列的按鈕是 44px）', () => {
    // Arrange
    stubMedia({ isCoarse: true });
    stubLayout(1200);

    // Act
    const { handle } = renderLayout();

    // Assert
    expect(handle).toHaveAttribute('aria-valuemin', String(32 * 16));
  });

  it('不在 SidePanelLayout 裡的 SidePanel 沒有把手', () => {
    // Arrange & Act
    render(
      <SidePanel label="項目 AGY-3" title="AGY-3" onClose={() => undefined}>
        內容
      </SidePanel>,
    );

    // Assert
    expect(screen.getByRole('complementary', { name: '項目 AGY-3' })).toBeInTheDocument();
    expect(screen.queryByRole('separator')).not.toBeInTheDocument();
  });

  it('寬度交給 CSS：清單那一塊帶著 --side-panel-current-width', async () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { user, handle, panel } = renderLayout();
    handle.focus();

    // Act
    await user.keyboard('{ArrowLeft}');

    // Assert
    expect(styleOf(panel, '--side-panel-current-width')).toBe(`${DEFAULT + 16}px`);
  });

  it('Tab 的順序是清單 → 把手 → 面板的按鈕；焦點在把手上按 Esc 關面板', async () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { user, onClose, handle } = renderLayout();
    screen.getByRole('button', { name: 'AGY-2' }).focus();

    // Act
    await user.tab();
    const afterList = document.activeElement;
    await user.tab();
    const afterHandle = document.activeElement;
    await user.tab({ shift: true });
    await user.keyboard('{Escape}');

    // Assert
    expect(afterList).toBe(handle);
    expect(afterHandle).toBe(screen.getByRole('button', { name: '關閉' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe('面板的寬度：鍵盤', () => {
  it('← 加寬、→ 縮窄，一次 16px；Shift 一次 64px', async () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { user, handle } = renderLayout();
    handle.focus();

    // Act
    await user.keyboard('{ArrowLeft}');
    const afterLeft = widthOf(handle);
    await user.keyboard('{ArrowRight}{ArrowRight}');
    const afterRight = widthOf(handle);
    await user.keyboard('{Shift>}{ArrowLeft}{/Shift}');

    // Assert
    expect(afterLeft).toBe(DEFAULT + 16);
    expect(afterRight).toBe(DEFAULT - 16);
    expect(widthOf(handle)).toBe(DEFAULT - 16 + 64);
    expect(handle).toHaveAttribute('aria-valuetext', `${DEFAULT - 16 + 64} 像素`);
    expect(stored()).toBe(String(DEFAULT - 16 + 64));
  });

  it('Home 到最窄、End 到最寬；超出範圍的按鍵停在邊界', async () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { user, handle } = renderLayout();
    handle.focus();

    // Act
    await user.keyboard('{Home}');
    const atHome = widthOf(handle);
    await user.keyboard('{ArrowRight}');
    const belowMin = widthOf(handle);
    await user.keyboard('{End}');

    // Assert
    expect(atHome).toBe(MIN);
    expect(belowMin).toBe(MIN);
    expect(widthOf(handle)).toBe(1200 - LIST_MIN);
  });

  it('Enter 回到預設寬度，並拿掉記住的寬度', async () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { user, handle } = renderLayout();
    handle.focus();
    await user.keyboard('{End}');

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(widthOf(handle)).toBe(DEFAULT);
    expect(stored()).toBeNull();
  });

  it('處理過的方向鍵不讓頁面捲動（preventDefault）；其他按鍵照常往外傳', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { handle } = renderLayout();

    // Act
    const isArrowAllowed = fireEvent.keyDown(handle, { key: 'ArrowLeft' });
    const isTabAllowed = fireEvent.keyDown(handle, { key: 'Tab' });

    // Assert
    expect(isArrowAllowed).toBe(false);
    expect(isTabAllowed).toBe(true);
  });

  it.each([
    ['⌘←', { key: 'ArrowLeft', metaKey: true }],
    ['Alt+←（上一頁）', { key: 'ArrowLeft', altKey: true }],
    ['Ctrl+→', { key: 'ArrowRight', ctrlKey: true }],
    ['Ctrl+Home', { key: 'Home', ctrlKey: true }],
    ['Ctrl+End', { key: 'End', ctrlKey: true }],
    ['Shift+Home', { key: 'Home', shiftKey: true }],
    ['Shift+Enter', { key: 'Enter', shiftKey: true }],
  ])('帶著修飾鍵的 %s 不處理，交還給瀏覽器', (_name, init) => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { handle } = renderLayout();

    // Act
    const isAllowed = fireEvent.keyDown(handle, init);

    // Assert
    expect(isAllowed).toBe(true);
    expect(widthOf(handle)).toBe(DEFAULT);
    expect(stored()).toBeNull();
  });

  it('從右到左的文字方向：面板在左邊、把手在它的右緣，→ 加寬', async () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    document.documentElement.setAttribute('dir', 'rtl');
    const { user, handle } = renderLayout();
    handle.focus();

    // Act
    await user.keyboard('{ArrowRight}');
    document.documentElement.removeAttribute('dir');

    // Assert
    expect(widthOf(handle)).toBe(DEFAULT + 16);
  });
});

describe('面板的寬度：拖曳', () => {
  it('往左拉加寬、往右拉縮窄，超出範圍時停在邊界；放開時記住', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { handle } = renderLayout();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 700 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 600 });
    const wider = widthOf(handle);
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 740 });
    const narrower = widthOf(handle);
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 0 });
    const atMax = widthOf(handle);
    const storedWhileDragging = stored();
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 0 });

    // Assert
    expect(wider).toBe(DEFAULT + 100);
    expect(narrower).toBe(DEFAULT - 40);
    expect(atMax).toBe(1200 - LIST_MIN);
    expect(storedWhileDragging).toBeNull();
    expect(stored()).toBe(String(1200 - LIST_MIN));
    expect(setPointerCapture).toHaveBeenCalledWith(1);
  });

  it('沒有按下就移動、按的不是主要按鍵，都不改寬度', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { handle } = renderLayout();

    // Act
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 100 });
    fireEvent.pointerDown(handle, { pointerId: 2, button: 2, clientX: 700 });
    fireEvent.pointerMove(handle, { pointerId: 2, clientX: 100 });

    // Assert
    expect(widthOf(handle)).toBe(DEFAULT);
  });

  it('只按一下沒有拖：不記住畫面上被夾過的寬度，原本記住的寬度留著', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    localStorage.setItem(SIDE_PANEL_WIDTH_STORAGE_KEY, '1400');
    const { handle } = renderLayout();
    const clamped = widthOf(handle);

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 500 });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 500 });

    // Assert
    expect(clamped).toBe(1200 - LIST_MIN);
    expect(stored()).toBe('1400');
  });

  it('指標被瀏覽器收回（lostpointercapture）時照放開處理：記住拖到的寬度，之後的移動不再改', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { handle } = renderLayout();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 700 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 650 });

    // Act
    fireEvent.lostPointerCapture(handle, { pointerId: 1 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 300 });

    // Assert
    expect(widthOf(handle)).toBe(DEFAULT + 50);
    expect(stored()).toBe(String(DEFAULT + 50));
  });

  it('拖曳被取消（pointercancel）和 Esc 一樣：回到開始的寬度、什麼都不記', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { handle } = renderLayout();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 700 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 650 });

    // Act
    fireEvent.pointerCancel(handle, { pointerId: 1 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 300 });

    // Assert
    expect(widthOf(handle)).toBe(DEFAULT);
    expect(stored()).toBeNull();
  });

  it('拖曳中按 Esc：回到開始的寬度、什麼都不記，面板不關、Esc 不往外傳', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    localStorage.setItem(SIDE_PANEL_WIDTH_STORAGE_KEY, '1400');
    const { handle, onClose } = renderLayout();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 700 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 900 });
    const whileDragging = widthOf(handle);

    // Act
    const isEscapeAllowed = fireEvent.keyDown(document.body, { key: 'Escape' });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 800 });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 800 });

    // Assert
    expect(whileDragging).toBe(1200 - LIST_MIN - 200);
    expect(isEscapeAllowed).toBe(false);
    expect(onClose).not.toHaveBeenCalled();
    expect(widthOf(handle)).toBe(1200 - LIST_MIN);
    expect(stored()).toBe('1400');
  });

  it('拖曳中不選到文字：選取被擋下，放開之後照常可以選', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    const { handle } = renderLayout();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 700 });
    const isSelectAllowedWhileDragging = fireEvent(document, new Event('selectstart', { cancelable: true }));
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 700 });
    const isSelectAllowedAfter = fireEvent(document, new Event('selectstart', { cancelable: true }));

    // Assert
    expect(isSelectAllowedWhileDragging).toBe(false);
    expect(isSelectAllowedAfter).toBe(true);
  });

  it('點兩下回到預設寬度', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    localStorage.setItem(SIDE_PANEL_WIDTH_STORAGE_KEY, '600');
    const { handle } = renderLayout();

    // Act
    fireEvent.doubleClick(handle);

    // Assert
    expect(widthOf(handle)).toBe(DEFAULT);
    expect(stored()).toBeNull();
  });
});

describe('面板的寬度：記住與夾回範圍', () => {
  it('記住的寬度在重新整理（重新掛上）之後還在', () => {
    // Arrange
    stubMedia();
    stubLayout(1200);
    localStorage.setItem(SIDE_PANEL_WIDTH_STORAGE_KEY, '600');

    // Act
    const { handle } = renderLayout();

    // Assert
    expect(widthOf(handle)).toBe(600);
  });

  it('清單那一塊變窄時夾回範圍內；記住的寬度不變，變回寬的時候回到原本的寬度', () => {
    // Arrange
    stubMedia();
    const observer = stubResizeObserver();
    stubLayout(1200);
    localStorage.setItem(SIDE_PANEL_WIDTH_STORAGE_KEY, '700');
    const { handle } = renderLayout();
    const before = widthOf(handle);

    // Act
    stubLayout(1000);
    observer.resize();
    const narrowed = widthOf(handle);
    const narrowedMax = handle.getAttribute('aria-valuemax');
    stubLayout(1200);
    observer.resize();

    // Assert
    expect(before).toBe(700);
    expect(narrowed).toBe(1000 - LIST_MIN);
    expect(narrowedMax).toBe(String(1000 - LIST_MIN));
    expect(stored()).toBe('700');
    expect(widthOf(handle)).toBe(700);
  });

  it('記住的寬度比現在的範圍寬時，畫面上用範圍內的最寬', () => {
    // Arrange
    stubMedia({ isBeside: false });
    stubLayout(800);
    localStorage.setItem(SIDE_PANEL_WIDTH_STORAGE_KEY, '1400');

    // Act
    const { handle } = renderLayout();

    // Assert
    expect(widthOf(handle)).toBe(800 - OVERLAY_KEEP);
  });
});

describe('面板的高度（page 版面）', () => {
  it('面板上下還沒捲走、不屬於清單那一塊的高度交給 CSS（--side-panel-block-offset）：面板一直在頁列與畫面的底之間', () => {
    // Arrange：頁列 3rem＝48px；清單那一塊的頂端在 108px（頁列、工具列與內容區的上方留白）。
    stubMedia();
    stubLayout(1200, 108);
    const { panel } = renderLayout('page');
    const atTop = styleOf(panel, '--side-panel-block-offset');

    // Act：捲到中間，清單那一塊上下都超出畫面。
    stubLayout(1200, -300);
    fireEvent.scroll(window);
    const inMiddle = styleOf(panel, '--side-panel-block-offset');
    // 捲到最底：清單那一塊的底在 700px，下面露出 68px 的留白。
    stubLayout(1200, -500, 1200);
    fireEvent.scroll(window);

    // Assert
    expect(atTop).toBe('60px');
    expect(inMiddle).toBe('0px');
    expect(styleOf(panel, '--side-panel-block-offset')).toBe('68px');
  });
});
