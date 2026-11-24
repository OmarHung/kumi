import { afterEach, describe, expect, it, vi } from 'vitest';
import { alignmentFor, placeEditor, watchEditorPlacement } from './editorPlacement';

/*
 * 浮起來的編輯器往右還是往左長（docs/DESIGN.md 8.3「就地編輯」）：jsdom 沒有版面，格子、編輯器、捲動區塊的位置用假的。
 */

interface Rect {
  readonly left: number;
  readonly width: number;
}

function stubRect(element: HTMLElement, { left, width }: Rect): void {
  vi.spyOn(element, 'getBoundingClientRect').mockReturnValue(new DOMRect(left, 0, width, 36));
}

/** 捲動區塊（橫向會裁切）裡的一格，格子裡是浮起來的編輯器。 */
function mountCell(area: Rect, cell: Rect, editorWidth: number, scrollPaddingLeft = '0px') {
  const scroller = document.createElement('div');
  scroller.style.overflowX = 'auto';
  scroller.style.setProperty('scroll-padding-left', scrollPaddingLeft);
  const anchor = document.createElement('span');
  const frame = document.createElement('div');
  anchor.append(frame);
  scroller.append(anchor);
  document.body.append(scroller);
  stubRect(scroller, area);
  Object.defineProperty(scroller, 'clientLeft', { configurable: true, value: 0 });
  Object.defineProperty(scroller, 'clientWidth', { configurable: true, value: area.width });
  stubRect(anchor, cell);
  stubRect(frame, { left: cell.left, width: editorWidth });
  return { scroller, anchor, frame };
}

afterEach(() => {
  document.body.replaceChildren();
});

describe('alignmentFor：往右還是往左長', () => {
  it('右邊放得下就往右長', () => {
    expect(alignmentFor({ left: 100, right: 200 }, 250, { left: 0, right: 400 })).toBe('start');
  });

  it('右邊放不下、左邊比較寬就往左長（貼齊格子的右邊）', () => {
    expect(alignmentFor({ left: 300, right: 400 }, 250, { left: 0, right: 420 })).toBe('end');
  });

  it('右邊放不下、但左邊更窄時還是往右長', () => {
    expect(alignmentFor({ left: 60, right: 160 }, 400, { left: 0, right: 420 })).toBe('start');
  });
});

describe('placeEditor：量格子、編輯器與會裁切的捲動區塊', () => {
  it('最後幾欄放不下時往左長（data-align="end"）', () => {
    // Arrange
    const { frame } = mountCell({ left: 0, width: 1000 }, { left: 880, width: 100 }, 288);

    // Act
    placeEditor(frame);

    // Assert
    expect(frame).toHaveAttribute('data-align', 'end');
  });

  it('左邊被固定欄蓋住的範圍（scroll-padding-left）不算可以長的地方', () => {
    // Arrange：固定欄 900px 寬，格子右邊只剩 20px、左邊扣掉固定欄只剩 80px：右邊比較寬，往右長
    const { frame } = mountCell({ left: 0, width: 1100 }, { left: 900, width: 80 }, 288, '900px');

    // Act
    placeEditor(frame);

    // Assert
    expect(frame).not.toHaveAttribute('data-align');
  });

  it('放得下就往右長，原本往左的會改回來', () => {
    // Arrange
    const { frame } = mountCell({ left: 0, width: 1000 }, { left: 100, width: 100 }, 288);
    frame.setAttribute('data-align', 'end');

    // Act
    placeEditor(frame);

    // Assert
    expect(frame).not.toHaveAttribute('data-align');
  });
});

describe('watchEditorPlacement：編輯中位置變了也重新判斷', () => {
  it('捲動區塊橫向捲動、視窗改大小之後重新判斷（下一個畫面才做）', async () => {
    // Arrange
    const { scroller, anchor, frame } = mountCell({ left: 0, width: 1000 }, { left: 100, width: 100 }, 288);
    const stop = watchEditorPlacement(frame);
    expect(frame).not.toHaveAttribute('data-align');

    // Act：捲過去之後格子到了右邊
    stubRect(anchor, { left: 880, width: 100 });
    scroller.dispatchEvent(new Event('scroll'));
    await new Promise((resolve) => requestAnimationFrame(resolve));

    // Assert
    expect(frame).toHaveAttribute('data-align', 'end');

    // Act：視窗改大小，格子又回到左邊
    stubRect(anchor, { left: 100, width: 100 });
    window.dispatchEvent(new Event('resize'));
    await new Promise((resolve) => requestAnimationFrame(resolve));

    // Assert
    expect(frame).not.toHaveAttribute('data-align');

    // Act：停止之後不再管
    stop();
    stubRect(anchor, { left: 880, width: 100 });
    window.dispatchEvent(new Event('resize'));
    await new Promise((resolve) => requestAnimationFrame(resolve));

    // Assert
    expect(frame).not.toHaveAttribute('data-align');
  });
});
