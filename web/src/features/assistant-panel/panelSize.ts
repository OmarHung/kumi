/**
 * 浮動的 AI 助理面板的大小：預設、上下限、記在瀏覽器的那一份（個人的顯示偏好，不上伺服器）。
 * 單位是畫面上的像素。上限交給 CSS（夾在畫面之內）：畫面暫時變小時記住的值不被改小，變回大的時候回到原本的大小。
 */

export const PANEL_SIZE_STORAGE_KEY = 'assistant.panel';

export interface PanelSize {
  readonly width: number;
  readonly height: number;
}

export const DEFAULT_PANEL_SIZE: PanelSize = { width: 400, height: 600 };
export const MIN_PANEL_SIZE: PanelSize = { width: 320, height: 400 };
/** 面板離畫面邊緣的距離（右邊是頁面的左右留白，下面是 --space-4）：拖大時最大到「畫面減兩邊的邊距」。 */
export const PANEL_EDGE_MARGIN = 24;

export interface Viewport {
  readonly width: number;
  readonly height: number;
}

function isPositive(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成沒記過。 */
export function readStoredPanelSize(): PanelSize | null {
  try {
    const raw = localStorage.getItem(PANEL_SIZE_STORAGE_KEY);
    if (raw === null) return null;
    const value = JSON.parse(raw) as Partial<PanelSize> | null;
    if (value === null || !isPositive(value.width) || !isPositive(value.height)) return null;
    return { width: Math.max(value.width, MIN_PANEL_SIZE.width), height: Math.max(value.height, MIN_PANEL_SIZE.height) };
  } catch {
    return null;
  }
}

/** null 是回到預設。寫不進去時這次照樣用，下次載入回到預設：只是個人偏好。 */
export function storePanelSize(size: PanelSize | null): void {
  try {
    if (size === null) localStorage.removeItem(PANEL_SIZE_STORAGE_KEY);
    else localStorage.setItem(PANEL_SIZE_STORAGE_KEY, JSON.stringify(size));
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
  }
}

/** 夾在最小到「畫面減邊距」之間（畫面比最小還小時，最小優先：CSS 另外把面板夾在畫面之內）。取整數。 */
export function clampPanelSize(size: PanelSize, viewport: Viewport): PanelSize {
  const maxWidth = Math.max(MIN_PANEL_SIZE.width, viewport.width - 2 * PANEL_EDGE_MARGIN);
  const maxHeight = Math.max(MIN_PANEL_SIZE.height, viewport.height - 2 * PANEL_EDGE_MARGIN);
  return {
    width: Math.round(Math.min(Math.max(size.width, MIN_PANEL_SIZE.width), maxWidth)),
    height: Math.round(Math.min(Math.max(size.height, MIN_PANEL_SIZE.height), maxHeight)),
  };
}

export type ToastPlacement = 'left' | 'above' | 'top';

/** 新通知的提示（24rem 加內距）並排放得下需要的寬度、疊在上面放得下需要的高度（一則的高度再多一點）。 */
const TOAST_FOOTPRINT_WIDTH = 408;
const TOAST_FOOTPRINT_HEIGHT = 160;
const TOAST_GAP = 12;

/**
 * 面板開著時，新通知的提示讓到哪裡（提示在 top layer，不讓位會蓋住輸入框與送出鈕）：
 * 面板左邊放得下就放左邊；不行（畫面不夠寬）就疊在面板上面；上面也放不下（面板很高）或全螢幕的面板，放到畫面上緣。
 * 用畫面上實際的面板大小算（CSS 把面板夾在畫面之內）。
 */
export function toastPlacement(size: PanelSize, viewport: Viewport, isCompact: boolean): ToastPlacement {
  if (isCompact) return 'top';
  const width = Math.min(size.width, viewport.width - 2 * PANEL_EDGE_MARGIN);
  const height = Math.min(size.height, viewport.height - 2 * PANEL_EDGE_MARGIN);
  const leftRoom = viewport.width - width - 2 * PANEL_EDGE_MARGIN - TOAST_GAP;
  if (leftRoom >= TOAST_FOOTPRINT_WIDTH) return 'left';
  const aboveRoom = viewport.height - height - 2 * PANEL_EDGE_MARGIN - TOAST_GAP;
  return aboveRoom >= TOAST_FOOTPRINT_HEIGHT ? 'above' : 'top';
}
