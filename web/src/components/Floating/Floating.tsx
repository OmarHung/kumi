import { useEffect, useLayoutEffect, useRef, type ReactNode, type RefObject, type SyntheticEvent } from 'react';
import { cx } from '../../lib/cx';
import { useAnchoredPosition, type FloatingAlign } from '../../lib/useAnchoredPosition';
import styles from './Floating.module.css';
import { SUPPORTS_TOP_LAYER, useTopLayer } from './useTopLayer';

export interface FloatingProps {
  /** 浮層跟著它（通常是打開浮層的按鈕）。 */
  readonly anchorRef: RefObject<HTMLElement | null>;
  readonly align: FloatingAlign;
  /** 按鈕被捲出看得到的範圍時呼叫（通常是關上）。要是固定的函式（useCallback）。 */
  readonly onAnchorHidden?: () => void;
  /** 至少和按鈕一樣寬（下拉的清單，docs/DESIGN.md 7.3）。按鈕的寬度要執行時才量得到，所以在這裡設。 */
  readonly matchAnchorWidth?: boolean;
  /**
   * 在浮層上（包括內距與外框）按下滑鼠或手指不改變焦點（下拉的清單：焦點一直在按鈕或篩選框上）。輸入框除外，
   * 篩選框要點得進去、放得了游標。
   */
  readonly keepsAnchorFocus?: boolean;
  /** 座標（含 maxHeight）套上之後呼叫：清單在這時才知道自己實際的高度，下拉用它把反白的選項捲到看得到。 */
  readonly onPositioned?: () => void;
  readonly className?: string;
  readonly children: ReactNode;
}

/**
 * 按鈕被捲出看得到的範圍（包含被捲動容器裁掉）時通知。浮層在 top layer、不會跟著被裁掉，
 * 不處理的話會浮在不相干的內容上面。沒有 IntersectionObserver 的環境（jsdom）不做。
 */
function useAnchorVisibility(anchorRef: RefObject<HTMLElement | null>, onHidden: (() => void) | undefined): void {
  useEffect(() => {
    const anchor = anchorRef.current;
    if (anchor === null || onHidden === undefined || !('IntersectionObserver' in window)) {
      return undefined;
    }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => !entry.isIntersecting)) onHidden();
    });
    observer.observe(anchor);
    return () => {
      observer.disconnect();
    };
  }, [anchorRef, onHidden]);
}

/** 按下時焦點不動：擋掉預設動作（瀏覽器在 mousedown 時移動焦點）。按在輸入框上照常（要能點著放游標）。 */
function keepFocus(event: SyntheticEvent) {
  if (event.target instanceof Element && event.target.closest('input, textarea') !== null) return;
  event.preventDefault();
}

/** 定位完成（座標變了）之後通知；用最新的 callback，呼叫端不必給固定的函式。 */
function usePositioned(position: unknown, onPositioned: (() => void) | undefined): void {
  const latest = useRef(onPositioned);
  useLayoutEffect(() => {
    latest.current = onPositioned;
  });
  useLayoutEffect(() => {
    if (position !== null) latest.current?.();
  }, [position]);
}

/**
 * 選單與浮層的外殼：掛上就是打開。元素留在原本的位置（DOM 的順序不變：Tab 接著按鈕往下走、
 * 點在裡面不算「外面」、事件照樣往上傳），畫面上用 position: fixed 加程式算的座標浮在按鈕旁邊。
 * data-side 是放在按鈕的哪一邊，出現的動畫從那一邊長出來。
 */
export function Floating(props: FloatingProps) {
  const { anchorRef, align, onAnchorHidden, matchAnchorWidth = false, keepsAnchorFocus = false, onPositioned, className, children } = props;
  const ref = useRef<HTMLDivElement>(null);
  useTopLayer(ref);
  useAnchorVisibility(anchorRef, onAnchorHidden);
  const position = useAnchoredPosition(anchorRef, ref, align, matchAnchorWidth);
  usePositioned(position, onPositioned);
  // 座標與按鈕的寬度要執行時才算得出來：只有這裡用 style 物件（內容安全政策允許用程式設樣式，scripts/check-styles.mjs 有允許清單）。
  const style =
    position === null
      ? undefined
      : { top: position.top, left: position.left, maxHeight: position.maxHeight, minWidth: position.minWidth ?? undefined };
  return (
    <div
      ref={ref}
      popover={SUPPORTS_TOP_LAYER ? 'manual' : undefined}
      data-side={position?.side ?? 'bottom'}
      className={cx(styles.floating, className)}
      style={style}
      onPointerDown={keepsAnchorFocus ? keepFocus : undefined}
      onMouseDown={keepsAnchorFocus ? keepFocus : undefined}
    >
      {children}
    </div>
  );
}
