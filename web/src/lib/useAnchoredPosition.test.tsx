import { act, renderHook } from '@testing-library/react';
import type { RefObject } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { computeAnchoredPosition, FLOATING_GAP, useAnchoredPosition, VIEWPORT_MARGIN } from './useAnchoredPosition';

const VIEWPORT = { width: 1000, height: 800 };

function rect(left: number, top: number, width: number, height: number) {
  return { left, top, right: left + width, bottom: top + height };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('浮層的位置', () => {
  it('預設放在按鈕下方、左邊對齊', () => {
    // Arrange
    const anchor = rect(100, 50, 80, 32);

    // Act
    const position = computeAnchoredPosition(anchor, { width: 200, height: 150 }, VIEWPORT, 'start');

    // Assert
    expect(position).toEqual({
      top: 82 + FLOATING_GAP,
      left: 100,
      maxHeight: 800 - 82 - FLOATING_GAP - VIEWPORT_MARGIN,
      side: 'bottom',
    });
  });

  it('align="end" 時右邊和按鈕對齊', () => {
    // Arrange
    const anchor = rect(600, 50, 80, 32);

    // Act
    const position = computeAnchoredPosition(anchor, { width: 200, height: 150 }, VIEWPORT, 'end');

    // Assert
    expect(position.left).toBe(680 - 200);
  });

  it('下方放不下、上方比較寬時翻到按鈕上方', () => {
    // Arrange
    const anchor = rect(100, 700, 80, 32);

    // Act
    const position = computeAnchoredPosition(anchor, { width: 200, height: 300 }, VIEWPORT, 'start');

    // Assert
    expect(position.top).toBe(700 - FLOATING_GAP - 300);
    expect(position.maxHeight).toBe(700 - FLOATING_GAP - VIEWPORT_MARGIN);
    expect(position.side).toBe('top');
  });

  it('上下都放不下時放在空間大的一邊，高度限制在那一邊的空間裡', () => {
    // Arrange
    const anchor = rect(100, 200, 80, 32);

    // Act
    const position = computeAnchoredPosition(anchor, { width: 200, height: 2000 }, VIEWPORT, 'start');

    // Assert
    expect(position.top).toBe(232 + FLOATING_GAP);
    expect(position.maxHeight).toBe(800 - 232 - FLOATING_GAP - VIEWPORT_MARGIN);
  });

  it('不會超出視窗的左右邊緣', () => {
    // Arrange
    const nearRight = rect(950, 50, 40, 32);
    const nearLeft = rect(2, 50, 40, 32);

    // Act
    const right = computeAnchoredPosition(nearRight, { width: 200, height: 100 }, VIEWPORT, 'start');
    const left = computeAnchoredPosition(nearLeft, { width: 200, height: 100 }, VIEWPORT, 'end');

    // Assert
    expect(right.left).toBe(1000 - 200 - VIEWPORT_MARGIN);
    expect(left.left).toBe(VIEWPORT_MARGIN);
  });
});

/** 一個位置可以換的按鈕；moveTo 之後 getBoundingClientRect 回新的位置。 */
function movableAnchor(initial: ReturnType<typeof rect>) {
  let current = initial;
  const element = document.createElement('button');
  element.getBoundingClientRect = () => ({ ...current, x: current.left, y: current.top, width: 0, height: 0, toJSON: () => ({}) });
  return {
    ref: { current: element } as RefObject<HTMLElement | null>,
    moveTo(next: ReturnType<typeof rect>) {
      current = next;
    },
  };
}

/** 假的 requestAnimationFrame：runFrame() 才跑排好的那一個畫面。 */
function stubAnimationFrames() {
  let pending = new Map<number, FrameRequestCallback>();
  let nextId = 1;
  const cancel = vi.fn((id: number) => {
    pending.delete(id);
  });
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    pending.set(nextId, callback);
    nextId += 1;
    return nextId - 1;
  });
  vi.stubGlobal('cancelAnimationFrame', cancel);
  return {
    cancel,
    get pendingCount() {
      return pending.size;
    },
    runFrame() {
      const callbacks = [...pending.values()];
      pending = new Map();
      callbacks.forEach((callback) => {
        callback(0);
      });
    },
  };
}

function renderAnchored(anchor: ReturnType<typeof movableAnchor>) {
  const floatingRef: RefObject<HTMLElement | null> = { current: document.createElement('div') };
  return renderHook(() => useAnchoredPosition(anchor.ref, floatingRef, 'start'));
}

describe('useAnchoredPosition', () => {
  it('掛上就算好位置', () => {
    // Arrange
    const anchor = movableAnchor(rect(100, 50, 80, 32));

    // Act
    const { result } = renderAnchored(anchor);

    // Assert
    expect(result.current).toMatchObject({ top: 82 + FLOATING_GAP, side: 'bottom' });
  });

  it('按鈕自己因為版面變動而移動（沒有任何事件）：下一個畫面比對到位置變了就重算', () => {
    // Arrange
    const frames = stubAnimationFrames();
    const anchor = movableAnchor(rect(100, 50, 80, 32));
    const { result } = renderAnchored(anchor);
    act(() => {
      frames.runFrame();
    });
    const beforeMove = result.current?.left;

    // Act
    anchor.moveTo(rect(160, 50, 80, 32));
    act(() => {
      frames.runFrame();
    });

    // Assert
    expect(beforeMove).toBe(100);
    expect(result.current?.left).toBe(160);
  });

  it('裡面的捲動容器捲動時（scroll 事件不往上傳，用捕獲聽到）重算', () => {
    // Arrange
    const anchor = movableAnchor(rect(100, 50, 80, 32));
    const scroller = document.body.appendChild(document.createElement('div'));
    const { result } = renderAnchored(anchor);

    // Act
    anchor.moveTo(rect(100, 300, 80, 32));
    act(() => {
      scroller.dispatchEvent(new Event('scroll'));
    });

    // Assert
    expect(result.current?.top).toBe(332 + FLOATING_GAP);
    scroller.remove();
  });

  it('視窗改大小時重算', () => {
    // Arrange
    const anchor = movableAnchor(rect(100, 50, 80, 32));
    const { result } = renderAnchored(anchor);

    // Act
    anchor.moveTo(rect(40, 50, 80, 32));
    act(() => {
      window.dispatchEvent(new Event('resize'));
    });

    // Assert
    expect(result.current?.left).toBe(40);
  });

  it('卸下（浮層關上）之後不再比對、不再聽事件', () => {
    // Arrange
    const frames = stubAnimationFrames();
    const remove = vi.spyOn(window, 'removeEventListener');
    const { unmount } = renderAnchored(movableAnchor(rect(0, 0, 10, 10)));

    // Act
    unmount();

    // Assert
    expect(frames.cancel).toHaveBeenCalled();
    expect(frames.pendingCount).toBe(0);
    expect(remove.mock.calls.map((call) => call[0])).toEqual(expect.arrayContaining(['resize', 'scroll']));
  });

  it('matchAnchorWidth：至少和按鈕一樣寬（minWidth），貼近右緣時照這個寬度往左推', () => {
    // Arrange
    const anchor = movableAnchor(rect(900, 50, 120, 32));
    Object.defineProperty(anchor.ref.current, 'offsetWidth', { value: 120 });
    const floatingRef: RefObject<HTMLElement | null> = { current: document.createElement('div') };

    // Act
    const { result } = renderHook(() => useAnchoredPosition(anchor.ref, floatingRef, 'start', true));

    // Assert
    expect(result.current).toMatchObject({ minWidth: 120, left: window.innerWidth - 120 - VIEWPORT_MARGIN });
  });

  it('沒有 matchAnchorWidth 時不限最小寬度，位置照浮層自己的寬度', () => {
    // Arrange
    const anchor = movableAnchor(rect(900, 50, 120, 32));
    Object.defineProperty(anchor.ref.current, 'offsetWidth', { value: 120 });

    // Act
    const { result } = renderAnchored(anchor);

    // Assert
    expect(result.current).toMatchObject({ minWidth: null, left: 900 });
  });

  it('還沒有元素時沒有位置', () => {
    // Arrange
    const anchorRef: RefObject<HTMLElement | null> = { current: null };
    const floatingRef: RefObject<HTMLElement | null> = { current: null };

    // Act
    const { result } = renderHook(() => useAnchoredPosition(anchorRef, floatingRef, 'start'));

    // Assert
    expect(result.current).toBeNull();
  });
});
