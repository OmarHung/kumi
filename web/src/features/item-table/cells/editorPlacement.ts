/*
 * 浮起來的編輯器（EditorFrame）往右還是往左長（docs/DESIGN.md 8.3「就地編輯」）：預設往右長；右邊放不下（最後幾欄、
 * 面板的右半邊）而左邊比較寬時往左長（貼齊格子的右邊），不會被裁掉、撐出橫向捲軸，也不會長進固定欄底下。
 * 結果寫在 DOM 的 data-align 上：只是位置，不必重畫。
 */

export interface Bounds {
  readonly left: number;
  readonly right: number;
}

export type EditorAlign = 'start' | 'end';

export function alignmentFor(cell: Bounds, width: number, bounds: Bounds): EditorAlign {
  const isTooWideForRight = cell.left + width > bounds.right;
  const hasMoreRoomOnLeft = cell.right - bounds.left > bounds.right - cell.left;
  return isTooWideForRight && hasMoreRoomOnLeft ? 'end' : 'start';
}

/** 最近一個橫向會裁切的祖先（表格的捲動區塊、面板）；沒有就是 null（視窗）。 */
function clippingArea(element: HTMLElement): HTMLElement | null {
  for (let node = element.parentElement; node !== null; node = node.parentElement) {
    if (getComputedStyle(node).overflowX !== 'visible') return node;
  }
  return null;
}

function lengthOf(value: string): number {
  const parsed = Number.parseFloat(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

/**
 * 可以長的範圍：會裁切的祖先的內距框（不含捲軸），再扣掉它的 scroll-padding——表格的捲動區塊用 scroll-padding-left
 * 讓出固定欄（和 lib/revealCell.ts 一樣），長進那裡的部分會被下面幾列的固定欄蓋住。
 */
function clippingBounds(element: HTMLElement): Bounds {
  const area = clippingArea(element);
  if (area === null) return { left: 0, right: document.documentElement.clientWidth };
  const style = getComputedStyle(area);
  const left = area.getBoundingClientRect().left + area.clientLeft;
  return {
    left: left + lengthOf(style.getPropertyValue('scroll-padding-left')),
    right: left + area.clientWidth - lengthOf(style.getPropertyValue('scroll-padding-right')),
  };
}

export function placeEditor(frame: HTMLElement): void {
  const anchor = frame.parentElement;
  if (anchor === null) return;
  const cell = anchor.getBoundingClientRect();
  const { width } = frame.getBoundingClientRect();
  if (alignmentFor(cell, width, clippingBounds(anchor)) === 'end') frame.setAttribute('data-align', 'end');
  else frame.removeAttribute('data-align');
}

/**
 * 現在放一次，之後位置可能變了就再放一次：大小變了（出現錯誤說明、多選的勾選）、捲動區塊橫向捲動、視窗改大小
 * （開關面板、轉向）。捲動與改大小一個畫面最多算一次。回傳停止的函式。
 */
export function watchEditorPlacement(frame: HTMLElement): () => void {
  placeEditor(frame);
  let pending = 0;
  const schedule = () => {
    if (pending !== 0) return;
    pending = requestAnimationFrame(() => {
      pending = 0;
      placeEditor(frame);
    });
  };
  const area = frame.parentElement === null ? null : clippingArea(frame.parentElement);
  const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(schedule) : null;
  observer?.observe(frame);
  area?.addEventListener('scroll', schedule, { passive: true });
  window.addEventListener('resize', schedule);
  return () => {
    cancelAnimationFrame(pending);
    observer?.disconnect();
    area?.removeEventListener('scroll', schedule);
    window.removeEventListener('resize', schedule);
  };
}
