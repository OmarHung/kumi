import type { KeyboardEvent } from 'react';
import { isComposingKey } from './keyboard';
import { isRightToLeft } from './rovingGrid';

/** 鍵盤調整一次的寬度：一般 16px，按著 Shift 一次 64px。 */
export const RESIZE_KEY_STEP = 16;
export const RESIZE_KEY_STEP_LARGE = 64;

export interface KeyResizeTarget {
  /** 現在的寬度（像素）。 */
  readonly width: number;
  readonly min: number;
  /** null：量不到，沒有上限。 */
  readonly max: number | null;
  /** 把手在哪一邊：start 在文字方向的開頭（側邊面板的左緣），end 在結尾（浮動視窗的右緣）。往把手那一邊按是加寬。 */
  readonly edge: 'start' | 'end';
  /** 把手調的方向：x（預設，←／→）或 y（↑／↓，start 是上緣；不看文字方向）。浮動的 AI 助理面板的上緣把手用。 */
  readonly axis?: 'x' | 'y';
}

/**
 * 調整寬度的把手（role="separator"）上的按鍵對到的寬度（側邊面板與浮動視窗共用）。帶著 Alt、Ctrl、⌘ 的一律不處理
 * （⌘← 與 Alt+← 是上一頁，Ctrl+Home／End 是捲到頭尾），Shift 只用在方向鍵。從右到左的文字方向，左右反過來。
 *
 * 照 WAI-ARIA APG 的 Window Splitter，只有 Enter 刻意不同：APG 的 Enter 是收合／還原，
 * 這裡是回到預設寬度（和點兩下一樣）。面板與視窗本身有關閉按鈕與 Esc，再做一種「收合」只是多一個狀態。
 */
export function resizeKeyTarget(event: KeyboardEvent<HTMLElement>, target: KeyResizeTarget): number | 'reset' | null {
  if (event.altKey || event.ctrlKey || event.metaKey || isComposingKey(event)) return null;
  const isVertical = target.axis === 'y';
  const isArrow = isVertical ? event.key === 'ArrowUp' || event.key === 'ArrowDown' : event.key === 'ArrowLeft' || event.key === 'ArrowRight';
  if (event.shiftKey && !isArrow) return null;
  if (isArrow) {
    const step = event.shiftKey ? RESIZE_KEY_STEP_LARGE : RESIZE_KEY_STEP;
    const isTowardStart = isVertical ? event.key === 'ArrowUp' : (event.key === 'ArrowLeft') !== isRightToLeft(event.currentTarget);
    const isWider = isTowardStart === (target.edge === 'start');
    return target.width + (isWider ? step : -step);
  }
  switch (event.key) {
    case 'Home':
      return target.min;
    case 'End':
      return target.max ?? target.width;
    case 'Enter':
      return 'reset';
    default:
      return null;
  }
}
