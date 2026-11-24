import { readStoredWidth, saveStoredWidth } from '../../lib/storedWidth';

/**
 * 項目面板（SidePanel）可以拖拉調整的寬度：範圍與記住的寬度（docs/DESIGN.md 第 7 節 SidePanel）。
 * 單位一律是畫面上的像素；範圍用 rem 定，跟著根元素的字級變。
 */

/** 個人的顯示偏好，記在這個瀏覽器：使用者拉過的寬度（像素）。沒有就是預設的 --side-panel-width。 */
export const SIDE_PANEL_WIDTH_STORAGE_KEY = 'shell.sidePanelWidth';

/**
 * 最窄 25rem：標題列的類型與編號（「修改 MNT-3」）加上上一個、下一個、關注、分享、更多、浮動視窗、展開、關閉八顆按鈕還放得下
 * （2026-11-21 加了「分享」實測：22rem 時標題只剩 34px，「修改 MNT-3」要 52px；25rem 給標題約 83px）。
 */
const MIN_REM = 25;
/**
 * 觸控時最窄 32rem：八顆按鈕變成 44px，26rem 時標題整個被擠掉（實測 0px）；32rem 給標題約 97px。
 */
const TOUCH_MIN_REM = 32;
/**
 * 排成右邊一欄時清單至少留 30rem：專案表格窄的時候固定的編號與標題兩欄是 22.5rem（6.5rem＋16rem），
 * 再留一欄看得到、捲得動的空間；看板也看得到一欄半。
 */
const LIST_MIN_REM = 30;
/**
 * 蓋在清單上（80rem 以下）時，面板最寬是內容區減掉 16rem：清單左邊露出約 14.5rem（扣掉左邊的留白），
 * 看得到編號欄（6.5rem）與標題的開頭，看得出開著的是哪一列、點得到別的列；在大螢幕拉寬、記住的寬度
 * 到筆電上也不會幾乎蓋滿清單。
 */
const OVERLAY_KEEP_REM = 16;
/** 讀不到 --page-bar-height 時的頁列高度：和 tokens.css 同一個值。 */
const FALLBACK_BAR_REM = 3;
/** 讀不到 --side-panel-width（測試沒有載入 tokens.css）時的預設寬度：和 tokens.css 同一個值。 */
const FALLBACK_DEFAULT_REM = 28;
const FALLBACK_REM_PX = 16;

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成沒有設定（lib/storedWidth.ts）。 */
export function readSidePanelWidth(): number | null {
  return readStoredWidth(SIDE_PANEL_WIDTH_STORAGE_KEY);
}

/** null 是回到預設、拿掉設定。寫不進去時這次照樣用新的寬度，下次載入回到預設。 */
export function saveSidePanelWidth(width: number | null): void {
  saveStoredWidth(SIDE_PANEL_WIDTH_STORAGE_KEY, width);
}

export interface SidePanelMetrics {
  /** 清單那一塊（SidePanelLayout）的寬度；0 是量不到（沒有版面）。 */
  readonly layoutWidth: number;
  /** 內容區左右的留白（--page-gutter，也是清單與面板的間距）。 */
  readonly gutter: number;
  /** 根元素的字級（1rem 是幾個像素）。 */
  readonly rem: number;
}

export interface SidePanelBoundsOptions {
  /** 80rem 以上，面板排成右邊一欄；以下蓋在清單上。 */
  readonly isBeside: boolean;
  /** 觸控（pointer: coarse）：按鈕是 44px。 */
  readonly isCoarse: boolean;
}

export interface SidePanelBounds {
  readonly min: number;
  /** null：量不到清單的寬度，不設上限。 */
  readonly max: number | null;
}

/**
 * 面板寬度的範圍。排成右邊一欄（80rem 以上）時，面板加上清單至少要留的寬度不超過清單那一塊；
 * 蓋在清單上時最寬是內容區（清單那一塊加左右的留白，面板貼著它的右緣）減掉 OVERLAY_KEEP_REM。
 */
export function sidePanelBounds(
  { layoutWidth, gutter, rem }: SidePanelMetrics,
  { isBeside, isCoarse }: SidePanelBoundsOptions,
): SidePanelBounds {
  const min = (isCoarse ? TOUCH_MIN_REM : MIN_REM) * rem;
  if (layoutWidth <= 0) return { min, max: null };
  const max = isBeside ? layoutWidth - LIST_MIN_REM * rem : layoutWidth + 2 * gutter - OVERLAY_KEEP_REM * rem;
  return { min, max: Math.max(min, max) };
}

/** 夾回範圍內，取整數的像素（aria-valuenow 與記住的值都是整數）。 */
export function clampSidePanelWidth(width: number, { min, max }: SidePanelBounds): number {
  const upper = max ?? Number.POSITIVE_INFINITY;
  return Math.round(Math.min(Math.max(width, min), upper));
}

function pixelsOf(value: string, rem: number): number | null {
  const amount = Number.parseFloat(value);
  if (!Number.isFinite(amount)) return null;
  if (value.trim().endsWith('rem')) return amount * rem;
  return value.trim().endsWith('px') ? amount : null;
}

export interface SidePanelMeasure extends SidePanelMetrics {
  /** 預設寬度（tokens.css 的 --side-panel-width）換成像素。 */
  readonly defaultWidth: number;
  /**
   * 清單那一塊的頂端還在頁列下面多遠（還沒捲走的工具列與上方留白），捲過之後是 0。
   * page 版面排成右欄的面板黏在頁列下面，最高要扣掉它：不然一開始面板的底在畫面外面，
   * 焦點到了面板左緣（整個面板高）的把手，瀏覽器會把整頁往下捲。
   */
  readonly topOffset: number;
  /**
   * 清單那一塊的底到畫面的底還有多遠（捲到最後時露出來的內容區下方留白），沒露出來是 0。
   * 面板黏住時不能超出清單那一塊，最高也要扣掉它：不然面板會被往上推、標題列躲到頁列底下。
   */
  readonly bottomOffset: number;
}

/** 還沒量（面板沒開過）：沒有上限，預設寬度用和 tokens.css 一樣的 28rem。 */
export const UNMEASURED: SidePanelMeasure = {
  layoutWidth: 0,
  gutter: 0,
  rem: FALLBACK_REM_PX,
  defaultWidth: FALLBACK_DEFAULT_REM * FALLBACK_REM_PX,
  topOffset: 0,
  bottomOffset: 0,
};

function pixelsOrZero(value: string): number {
  const amount = Number.parseFloat(value);
  return Number.isFinite(amount) ? amount : 0;
}

/**
 * 量清單那一塊（寬度、留白、上下在畫面上的位置）與根元素上的字級、預設寬度、頁列高度；
 * 讀不到（jsdom）時字級當成 16px、預設寬度 28rem、頁列 3rem、留白 0。
 */
export function measureSidePanel(layout: HTMLElement): SidePanelMeasure {
  const root = getComputedStyle(document.documentElement);
  const fontSize = Number.parseFloat(root.fontSize);
  const rem = Number.isFinite(fontSize) && fontSize > 0 ? fontSize : FALLBACK_REM_PX;
  const defaultWidth = pixelsOf(root.getPropertyValue('--side-panel-width'), rem) ?? FALLBACK_DEFAULT_REM * rem;
  const barHeight = pixelsOf(root.getPropertyValue('--page-bar-height'), rem) ?? FALLBACK_BAR_REM * rem;
  const rect = layout.getBoundingClientRect();
  return {
    layoutWidth: rect.width,
    gutter: pixelsOrZero(getComputedStyle(layout).columnGap),
    rem,
    defaultWidth,
    topOffset: Math.max(0, Math.round(rect.top - barHeight)),
    bottomOffset: Math.max(0, Math.round(window.innerHeight - rect.bottom)),
  };
}
