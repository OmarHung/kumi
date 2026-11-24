import { useLayoutEffect, useState, type RefObject } from 'react';

/** 浮層對齊按鈕的哪一邊：start 是左邊對齊，end 是右邊對齊。 */
export type FloatingAlign = 'start' | 'end';

/** 浮層放在按鈕的哪一邊（出現的動畫跟著它的方向）。 */
export type FloatingSide = 'top' | 'bottom';

export interface AnchoredPosition {
  readonly top: number;
  readonly left: number;
  readonly maxHeight: number;
  readonly side: FloatingSide;
}

/** 浮層實際用的版面：位置加上最小寬度（matchAnchorWidth 時是按鈕的寬度，否則 null）。 */
export interface AnchoredLayout extends AnchoredPosition {
  readonly minWidth: number | null;
}

interface Edges {
  readonly top: number;
  readonly bottom: number;
  readonly left: number;
  readonly right: number;
}

interface Size {
  readonly width: number;
  readonly height: number;
}

/** 浮層和按鈕之間的距離（px）。 */
export const FLOATING_GAP = 4;
/** 浮層離視窗邊緣至少這麼遠（px）。 */
export const VIEWPORT_MARGIN = 8;
// 空間再小也至少給這麼高，裡面自己捲動，不會縮成一條線。
const MIN_FLOATING_HEIGHT = 120;

/**
 * 浮層的座標（相對於視窗，給 position: fixed 用）。預設放在按鈕下方；下方放不下、上方比較寬時翻到上方。
 * 高度限制在放的那一邊的空間裡（超過就在浮層裡捲動），左右不超出視窗。
 */
export function computeAnchoredPosition(
  anchor: Edges,
  floating: Size,
  viewport: Size,
  align: FloatingAlign,
): AnchoredPosition {
  const spaceBelow = viewport.height - anchor.bottom - FLOATING_GAP - VIEWPORT_MARGIN;
  const spaceAbove = anchor.top - FLOATING_GAP - VIEWPORT_MARGIN;
  const isBelow = floating.height <= spaceBelow || spaceBelow >= spaceAbove;
  const maxHeight = Math.max(MIN_FLOATING_HEIGHT, isBelow ? spaceBelow : spaceAbove);
  const shownHeight = Math.min(floating.height, maxHeight);
  const top = isBelow ? anchor.bottom + FLOATING_GAP : anchor.top - FLOATING_GAP - shownHeight;
  const preferredLeft = align === 'start' ? anchor.left : anchor.right - floating.width;
  const maxLeft = viewport.width - floating.width - VIEWPORT_MARGIN;
  const left = Math.max(VIEWPORT_MARGIN, Math.min(preferredLeft, maxLeft));
  return { top, left, maxHeight, side: isBelow ? 'bottom' : 'top' };
}

function viewportSize(): Size {
  // clientWidth 不含捲軸；jsdom 是 0，退回 innerWidth。
  return { width: document.documentElement.clientWidth || window.innerWidth, height: window.innerHeight };
}

function isSame(a: AnchoredLayout | null, b: AnchoredLayout): boolean {
  return (
    a !== null && a.top === b.top && a.left === b.left && a.maxHeight === b.maxHeight && a.side === b.side && a.minWidth === b.minWidth
  );
}

/**
 * 浮層的大小：scrollHeight 是內容原本的高度（已經被 maxHeight 限制住時，才知道空間變大之後要不要放寬），
 * 不含上下的外框：補回去，翻到上方時和按鈕的距離才是 FLOATING_GAP。
 * 至少和按鈕一樣寬時（下拉的清單），左右的夾擠照實際會畫出來的寬度算，貼近右緣時才推得夠。
 */
function floatingSize(floating: HTMLElement, minWidth: number | null): Size {
  const borders = floating.offsetHeight - floating.clientHeight;
  return { width: Math.max(floating.offsetWidth, minWidth ?? 0), height: floating.scrollHeight + borders };
}

function edgesKey(edges: Edges): string {
  return `${String(edges.top)} ${String(edges.left)} ${String(edges.bottom)} ${String(edges.right)}`;
}

/**
 * 按鈕自己因為版面變動而移動時（旁邊多了一段文字、清單重新排序）沒有任何事件可以聽：
 * 打開期間每一個畫面比對一次按鈕的位置，變了才重算。回傳停止比對的函式。
 */
function watchAnchor(anchor: HTMLElement, onMove: () => void): () => void {
  let last = edgesKey(anchor.getBoundingClientRect());
  let frame = requestAnimationFrame(function compare() {
    const current = edgesKey(anchor.getBoundingClientRect());
    if (current !== last) {
      last = current;
      onMove();
    }
    frame = requestAnimationFrame(compare);
  });
  return () => {
    cancelAnimationFrame(frame);
  };
}

/**
 * 浮層掛上時算出座標；視窗或任何捲動容器捲動（用捕獲聽，scroll 事件不往上傳）、視窗改大小、
 * 浮層自己的大小變了、按鈕自己移動了都重算。matchAnchorWidth 時另外量按鈕的寬度當最小寬度（下拉的清單）；
 * 量不到（jsdom 的 offsetWidth 一律是 0）就不限。
 * 用在掛上就是打開的浮層（components/Floating）；還沒有元素時回 null。
 */
export function useAnchoredPosition(
  anchorRef: RefObject<HTMLElement | null>,
  floatingRef: RefObject<HTMLElement | null>,
  align: FloatingAlign,
  matchAnchorWidth = false,
): AnchoredLayout | null {
  const [position, setPosition] = useState<AnchoredLayout | null>(null);
  useLayoutEffect(() => {
    const anchor = anchorRef.current;
    const floating = floatingRef.current;
    if (anchor === null || floating === null) {
      return undefined;
    }
    function update() {
      if (anchor === null || floating === null) return;
      const minWidth = matchAnchorWidth && anchor.offsetWidth > 0 ? anchor.offsetWidth : null;
      const size = floatingSize(floating, minWidth);
      const next = { ...computeAnchoredPosition(anchor.getBoundingClientRect(), size, viewportSize(), align), minWidth };
      setPosition((current) => (isSame(current, next) ? current : next));
    }
    update();
    window.addEventListener('resize', update);
    window.addEventListener('scroll', update, true);
    const observer = 'ResizeObserver' in window ? new ResizeObserver(update) : null;
    observer?.observe(floating);
    const stopWatching = watchAnchor(anchor, update);
    return () => {
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', update, true);
      observer?.disconnect();
      stopWatching();
    };
  }, [anchorRef, floatingRef, align, matchAnchorWidth]);
  return position;
}
