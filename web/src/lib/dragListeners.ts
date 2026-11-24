import { isComposingNativeKey } from './keyboard';

/*
 * 拖曳中才需要的：抓住指標、聽 Esc、擋下文字的選取。拖曳調整寬度（useDragResize.ts）與拖表頭換欄的順序
 * （components/Table/useColumnDrag.ts）共用。
 */

export function capturePointer(element: HTMLElement, pointerId: number): void {
  try {
    element.setPointerCapture(pointerId);
  } catch {
    // 沒有 pointer capture 的環境（測試）或指標已經放開：移動照樣送到把手上，只是拖出把手就收不到。
  }
}

/**
 * 拖曳中聽 Esc：聽在 document 的捕獲階段、處理了就不往外傳——不然同一個 Esc 會關掉旁邊的項目面板
 * （lib/shortcuts.ts 的 closePanel 聽在冒泡階段）。回傳停止聽的函式。
 */
function listenForEscape(onEscape: () => void): () => void {
  const listener = (event: KeyboardEvent) => {
    if (event.key !== 'Escape' || isComposingNativeKey(event)) return;
    event.preventDefault();
    event.stopPropagation();
    onEscape();
  };
  document.addEventListener('keydown', listener, true);
  return () => {
    document.removeEventListener('keydown', listener, true);
  };
}

/**
 * 拖曳中擋下文字的選取。Chrome 有 setPointerCapture 就不會選到（實測），其他瀏覽器沒有驗過，
 * 加上這一道比較保險。回傳停止擋的函式。
 */
function preventSelection(): () => void {
  const listener = (event: Event) => {
    event.preventDefault();
  };
  document.addEventListener('selectstart', listener);
  return () => {
    document.removeEventListener('selectstart', listener);
  };
}

/** 拖曳中才聽的：Esc 取消、擋下文字的選取。回傳全部停止的函式。 */
export function listenWhileDragging(onEscape: () => void): () => void {
  const stopEscape = listenForEscape(onEscape);
  const stopSelection = preventSelection();
  return () => {
    stopEscape();
    stopSelection();
  };
}
