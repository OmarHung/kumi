import { readStoredWidth, saveStoredWidth } from '../../lib/storedWidth';

/**
 * 項目的浮動視窗可以拖拉調整的寬度（docs/DESIGN.md 第 7 節 FloatingPanel）：範圍與記住的寬度。
 * 預設寬度在 CSS（components/Dialog 的 size="item"：畫面寬的 85%，最少 --dialog-width-item、最多 --dialog-width-item-max），
 * 使用者拉過的寬度換掉它（--floating-panel-width）。單位一律是畫面上的像素；範圍用 rem 定，跟著根元素的字級變。
 */

/** 個人的顯示偏好，記在這個瀏覽器：使用者拉過的寬度（像素）。沒有就是預設的寬度。 */
export const FLOATING_PANEL_WIDTH_STORAGE_KEY = 'shell.floatingPanelWidth';

/**
 * 最窄 40rem：項目頁的內容在這個寬度排成一欄（說明、欄位上下排）還好讀；再窄就該用側邊面板。
 * 畫面本身比這個窄時，最窄就是畫面放得下的寬度。
 */
const MIN_REM = 40;
const FALLBACK_REM_PX = 16;

export function readFloatingPanelWidth(): number | null {
  return readStoredWidth(FLOATING_PANEL_WIDTH_STORAGE_KEY);
}

export function saveFloatingPanelWidth(width: number | null): void {
  saveStoredWidth(FLOATING_PANEL_WIDTH_STORAGE_KEY, width);
}

export interface FloatingPanelMeasure {
  /** 視窗畫出來的寬度；0 是量不到（沒有版面）。 */
  readonly rendered: number;
  /** 遮罩裡放得下視窗的寬度（畫面寬減掉兩邊的留白）；0 是量不到。 */
  readonly available: number;
  /** 根元素的字級（1rem 是幾個像素）。 */
  readonly rem: number;
}

export const UNMEASURED: FloatingPanelMeasure = { rendered: 0, available: 0, rem: FALLBACK_REM_PX };

export interface FloatingPanelBounds {
  readonly min: number;
  /** null：量不到，沒有上限（畫面上照樣被 CSS 夾在畫面寬之內）。 */
  readonly max: number | null;
}

/** 最窄 40rem（畫面更窄時是放得下的寬度），最寬是遮罩裡放得下的寬度：兩邊至少照樣留著遮罩的留白。 */
export function floatingPanelBounds({ available, rem }: FloatingPanelMeasure): FloatingPanelBounds {
  const min = MIN_REM * rem;
  if (available <= 0) return { min, max: null };
  return { min: Math.min(min, available), max: available };
}

/** 夾回範圍內，取整數的像素（aria-valuenow 與記住的值都是整數）。 */
export function clampFloatingPanelWidth(width: number, { min, max }: FloatingPanelBounds): number {
  return Math.round(Math.min(Math.max(width, min), max ?? Number.POSITIVE_INFINITY));
}

function pixelsOrZero(value: string): number {
  const amount = Number.parseFloat(value);
  return Number.isFinite(amount) ? amount : 0;
}

/** 量視窗（role="dialog" 的外框）與它外面的遮罩；讀不到（jsdom）時字級當成 16px、留白 0。 */
export function measureFloatingPanel(dialog: HTMLElement): FloatingPanelMeasure {
  const fontSize = Number.parseFloat(getComputedStyle(document.documentElement).fontSize);
  const rem = Number.isFinite(fontSize) && fontSize > 0 ? fontSize : FALLBACK_REM_PX;
  const backdrop = dialog.parentElement;
  const padding = backdrop === null ? null : getComputedStyle(backdrop);
  const available =
    backdrop === null || padding === null
      ? 0
      : backdrop.clientWidth - pixelsOrZero(padding.paddingLeft) - pixelsOrZero(padding.paddingRight);
  return { rendered: Math.round(dialog.getBoundingClientRect().width), available: Math.max(0, available), rem };
}
