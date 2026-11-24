/*
 * 取得焦點的格子（或清單上的連結）捲到看得到的地方（docs/DESIGN.md 7.1、8.3「清單的鍵盤操作」）。
 *
 * 一般的格子交給 scrollIntoView：它照捲動容器的 scroll-padding 讓出黏住的表頭與固定欄。
 * 固定在左邊的格子（position: sticky）不能這樣做：Chrome 把黏住的格子當成在原本的位置，
 * 橫向捲過去之後聚焦編號或標題，容器會被捲回一大段（實測 600 → 240）。黏住的格子一直看得到，所以只修正直向。
 * 黏住的表頭也一直看得到，不捲（用鍵盤走到表頭時，useRovingGrid 另外把它下面第一列捲進來）。
 */

export type RevealKind = 'native' | 'vertical' | 'skip';

export interface RevealTarget {
  /** 在表頭（thead）裡。 */
  readonly isHeader: boolean;
  /** 格子自己是 position: sticky（固定欄）。 */
  readonly isSticky: boolean;
}

export function revealKindOf({ isHeader, isSticky }: RevealTarget): RevealKind {
  if (isHeader) return 'skip';
  return isSticky ? 'vertical' : 'native';
}

/**
 * 直向要捲多少（正的往下）元素才在看得到的範圍裡（已經扣掉 scroll-padding）：和 scrollIntoView 的 nearest 一樣，
 * 看得到就不捲；比範圍高時對齊上緣。
 */
export function nearestDelta(start: number, end: number, viewStart: number, viewEnd: number): number {
  if (start < viewStart) return start - viewStart;
  if (end > viewEnd) return Math.min(end - viewEnd, start - viewStart);
  return 0;
}

const SCROLLING = /(auto|scroll)/;

function lengthOf(value: string): number {
  const parsed = Number.parseFloat(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

/** 直向真的捲得動（內容比它高）的第一個祖先，沒有就是整頁。 */
function verticalScroller(element: Element): Element {
  for (let node = element.parentElement; node !== null; node = node.parentElement) {
    if (SCROLLING.test(getComputedStyle(node).overflowY) && node.scrollHeight > node.clientHeight) return node;
  }
  return document.scrollingElement ?? document.documentElement;
}

/** 離元素最近、設了捲動的祖先（表格的捲動區塊），不管現在捲不捲得動。 */
export function scrollAreaOf(element: Element): Element | null {
  for (let node = element.parentElement; node !== null; node = node.parentElement) {
    const style = getComputedStyle(node);
    if (SCROLLING.test(`${style.overflowX} ${style.overflowY}`)) return node;
  }
  return null;
}

/** 橫向真的捲得動的第一個祖先（表格的捲動區塊）。 */
export function horizontalScroller(element: Element): Element | null {
  for (let node = element.parentElement; node !== null; node = node.parentElement) {
    if (SCROLLING.test(getComputedStyle(node).overflowX) && node.scrollWidth > node.clientWidth) return node;
  }
  return null;
}

/** 只修正直向：比對格子與捲動容器扣掉 scroll-padding（與元素自己的 scroll-margin）之後的範圍。 */
function revealVertically(cell: Element, element: Element): void {
  const scroller = verticalScroller(cell);
  const style = getComputedStyle(scroller);
  const isPage = scroller === document.scrollingElement;
  const top = isPage ? 0 : scroller.getBoundingClientRect().top + scroller.clientTop;
  const height = isPage ? window.innerHeight : scroller.clientHeight;
  const margin = lengthOf(getComputedStyle(element).scrollMarginTop);
  const viewStart = top + lengthOf(style.scrollPaddingTop) + margin;
  const viewEnd = top + height - lengthOf(style.scrollPaddingBottom);
  const rect = cell.getBoundingClientRect();
  const delta = nearestDelta(rect.top, rect.bottom, viewStart, viewEnd);
  if (delta !== 0) scroller.scrollTop += delta;
}

/**
 * element 所在的格子捲到看得到的地方；不在表格裡（看板的卡片）就是元素自己用 scrollIntoView。
 * 給表格的鍵盤操作（lib/useRovingGrid.ts）與項目面板換項目時（清單上那一筆）用。
 */
export function revealCell(element: Element): void {
  const cell = element.closest('td, th');
  const target = cell ?? element;
  const kind = revealKindOf({
    isHeader: target.closest('thead') !== null,
    isSticky: cell !== null && getComputedStyle(cell).position === 'sticky',
  });
  if (kind === 'skip') return;
  if (kind === 'vertical') {
    revealVertically(target, element);
    return;
  }
  if (typeof target.scrollIntoView === 'function') target.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

/** Home、Ctrl+Home：橫向捲回最前面（第一格是黏住的編號，捲動它本身不會讓容器回到最左邊）。 */
export function scrollToRowStart(element: Element): void {
  const scroller = horizontalScroller(element);
  if (scroller !== null) scroller.scrollLeft = 0;
}
