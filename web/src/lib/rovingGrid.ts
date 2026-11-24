import { focusableIn } from './focusable';

/*
 * 表格的鍵盤操作（docs/DESIGN.md 8.3「清單的鍵盤操作」）用到的 DOM 小工具：一格裡哪些元素停得到、
 * 一個元素在第幾列第幾格、按了哪個鍵要到哪一格。不碰 React，lib/useRovingGrid.ts 用它們。
 */

/** 一個停得到的位置：第幾列（表頭也算一列）、第幾格、格子裡第幾個可以操作的元素。 */
export interface GridPosition {
  readonly rowIndex: number;
  readonly cellIndex: number;
  readonly subIndex: number;
}

export type GridMove =
  | 'left'
  | 'right'
  | 'up'
  | 'down'
  | 'rowStart'
  | 'rowEnd'
  | 'gridStart'
  | 'gridEnd'
  | 'pageUp'
  | 'pageDown';

/** PageUp／PageDown 一次走的列數：照列數算，不看畫面的高度（jsdom 沒有版面，格子高度也都一樣）。 */
export const PAGE_ROWS = 10;

/**
 * 編輯中的格子（EditableCell 的編輯器）標這個屬性：裡面的方向鍵、Tab 都是編輯器自己的（輸入框的游標、
 * 下拉的選項、日期的欄位、多選的勾選框），這裡不處理、也不改裡面元素的 tabIndex。
 */
export const CELL_EDITOR_ATTRIBUTE = 'data-cell-editor';

/**
 * 表格裡不是編輯用的按鈕（成員的「移除」）標這個屬性：F2 是「進入編輯」，不按它（Enter、空白鍵照按鈕原本的行為）。
 * 沒標的表身按鈕都當成編輯用的（EditableCell 的按鈕、一直是下拉的角色）。
 */
export const GRID_ACTION_ATTRIBUTE = 'data-grid-action';

const INTERACTIVE = 'a[href], button, input:not([type="hidden"]), select, textarea';

const PLAIN_MOVES: Readonly<Record<string, GridMove>> = {
  ArrowLeft: 'left',
  ArrowRight: 'right',
  ArrowUp: 'up',
  ArrowDown: 'down',
  Home: 'rowStart',
  End: 'rowEnd',
  PageUp: 'pageUp',
  PageDown: 'pageDown',
};

// Ctrl+Home／Ctrl+End；Mac 的鍵盤用 ⌘ 加 Home／End（fn+←／→）。
const COMMAND_MOVES: Readonly<Record<string, GridMove>> = { Home: 'gridStart', End: 'gridEnd' };

export function rowsOf(container: HTMLElement): readonly HTMLTableRowElement[] {
  return Array.from(container.querySelectorAll('tr'));
}

export function rowKeyOf(row: HTMLTableRowElement | undefined): string | null {
  return row?.dataset.rowKey ?? null;
}

function editorOf(cell: Element): HTMLElement | null {
  return cell.querySelector<HTMLElement>(`[${CELL_EDITOR_ATTRIBUTE}]`);
}

/**
 * 一格裡用方向鍵停得到的元素，照文件的順序：格子裡可以操作的元素（標題的連結、編輯用的按鈕、勾選框、「開啟」、
 * 表頭的排序按鈕）；沒有的（唯讀、不適用、表頭沒有排序的欄、停用的勾選框）就是格子自己，上下移動才不會跳欄。
 * 編輯中的格子沒有：焦點在編輯器裡，由編輯器自己管。
 */
export function targetsOf(cell: HTMLTableCellElement): readonly HTMLElement[] {
  if (editorOf(cell) !== null) return [];
  const controls = Array.from(cell.querySelectorAll<HTMLElement>(INTERACTIVE)).filter((element) => !element.matches(':disabled'));
  return controls.length > 0 ? controls : [cell];
}

export function cellAt(rows: readonly HTMLTableRowElement[], rowIndex: number, cellIndex: number): HTMLTableCellElement | null {
  return rows[rowIndex]?.cells.item(cellIndex) ?? null;
}

export interface GridEntry {
  readonly element: HTMLElement;
  /** 是那一格的停駐點（tabIndex 由這裡管）；false 是編輯器裡的元素（tabIndex 照編輯器自己的）。 */
  readonly isTarget: boolean;
}

/** 這個位置要把焦點放到哪裡。編輯中的格子（例如存不進去、還開著的輸入框）是編輯器裡的第一個元素。 */
export function entryAt(rows: readonly HTMLTableRowElement[], position: GridPosition): GridEntry | null {
  const cell = cellAt(rows, position.rowIndex, position.cellIndex);
  if (cell === null) return null;
  const targets = targetsOf(cell);
  const target = targets[Math.min(position.subIndex, targets.length - 1)];
  if (target !== undefined) return { element: target, isTarget: true };
  const editor = editorOf(cell);
  const inEditor = editor === null ? undefined : focusableIn(editor)[0];
  return inEditor === undefined ? null : { element: inEditor, isTarget: false };
}

export interface LocatedElement extends GridPosition {
  /** 元素就是那一格的停駐點（不是編輯器裡的元素）。 */
  readonly isTarget: boolean;
}

/** 元素在第幾列第幾格；不在容器的表格裡（例如對話框透過 portal 把事件傳上來）是 null。 */
export function locate(container: HTMLElement, element: Element): LocatedElement | null {
  const cell = element.closest('td, th');
  if (!(cell instanceof HTMLTableCellElement) || !container.contains(cell)) return null;
  const row = cell.parentElement;
  if (!(row instanceof HTMLTableRowElement)) return null;
  const subIndex = element instanceof HTMLElement ? targetsOf(cell).indexOf(element) : -1;
  return {
    rowIndex: rowsOf(container).indexOf(row),
    cellIndex: cell.cellIndex,
    subIndex: Math.max(subIndex, 0),
    isTarget: subIndex >= 0,
  };
}

function clamp(value: number, max: number): number {
  return Math.min(Math.max(value, 0), Math.max(max, 0));
}

function targetCount(rows: readonly HTMLTableRowElement[], rowIndex: number, cellIndex: number): number {
  const cell = cellAt(rows, rowIndex, cellIndex);
  return cell === null ? 1 : Math.max(targetsOf(cell).length, 1);
}

/** 左右：先走過這一格裡的每一個元素，再到隔壁那一格；到了這一列的頭尾就停住。 */
function sideways(rows: readonly HTMLTableRowElement[], from: GridPosition, step: 1 | -1): GridPosition {
  const subIndex = from.subIndex + step;
  if (subIndex >= 0 && subIndex < targetCount(rows, from.rowIndex, from.cellIndex)) return { ...from, subIndex };
  const cellIndex = from.cellIndex + step;
  const cellCount = rows[from.rowIndex]?.cells.length ?? 0;
  if (cellIndex < 0 || cellIndex >= cellCount) return from;
  const entry = step === 1 ? 0 : targetCount(rows, from.rowIndex, cellIndex) - 1;
  return { rowIndex: from.rowIndex, cellIndex, subIndex: entry };
}

/** 上下：同一欄；到了第一列（表頭）或最後一列就停住。 */
function toRow(rows: readonly HTMLTableRowElement[], from: GridPosition, rowIndex: number): GridPosition {
  const target = clamp(rowIndex, rows.length - 1);
  const cellCount = rows[target]?.cells.length ?? 0;
  return { rowIndex: target, cellIndex: clamp(from.cellIndex, cellCount - 1), subIndex: from.subIndex };
}

function rowEnd(rows: readonly HTMLTableRowElement[], rowIndex: number): GridPosition {
  const cellIndex = Math.max((rows[rowIndex]?.cells.length ?? 0) - 1, 0);
  return { rowIndex, cellIndex, subIndex: targetCount(rows, rowIndex, cellIndex) - 1 };
}

/** 按了這個鍵之後的位置（APG 的 grid：表頭是第 0 列）。 */
export function moveFrom(rows: readonly HTMLTableRowElement[], from: GridPosition, move: GridMove): GridPosition {
  switch (move) {
    case 'left':
      return sideways(rows, from, -1);
    case 'right':
      return sideways(rows, from, 1);
    case 'up':
      return toRow(rows, from, from.rowIndex - 1);
    case 'down':
      return toRow(rows, from, from.rowIndex + 1);
    case 'pageUp':
      return toRow(rows, from, from.rowIndex - PAGE_ROWS);
    case 'pageDown':
      return toRow(rows, from, from.rowIndex + PAGE_ROWS);
    case 'rowStart':
      return { rowIndex: from.rowIndex, cellIndex: 0, subIndex: 0 };
    case 'rowEnd':
      return rowEnd(rows, from.rowIndex);
    case 'gridStart':
      return { rowIndex: 0, cellIndex: 0, subIndex: 0 };
    case 'gridEnd':
      return rowEnd(rows, rows.length - 1);
  }
}

export interface GridKey {
  readonly key: string;
  readonly altKey: boolean;
  readonly ctrlKey: boolean;
  readonly metaKey: boolean;
  readonly shiftKey: boolean;
}

/**
 * 這個按鍵是哪一種移動；不是的回 null（交給瀏覽器與其他快捷鍵）。按著 Alt 的是面板的上一個／下一個
 * （lib/shortcuts.ts），按著 Shift 的留給之後的多選；⌘／Ctrl 只配 Home、End。
 * 文字方向從右到左時，左右鍵照畫面的方向。
 */
export function gridMoveOf(event: GridKey, isRightToLeft: boolean): GridMove | null {
  if (event.altKey || event.shiftKey) return null;
  const move = event.ctrlKey || event.metaKey ? COMMAND_MOVES[event.key] : PLAIN_MOVES[event.key];
  if (move === undefined) return null;
  if (!isRightToLeft) return move;
  if (move === 'left') return 'right';
  return move === 'right' ? 'left' : move;
}

export function isRightToLeft(element: Element): boolean {
  return element.closest('[dir]')?.getAttribute('dir') === 'rtl';
}

/** 設定 tabIndex（值一樣就不動，免得一直觸發 DOM 的變動）。 */
export function setTabIndex(element: HTMLElement, value: 0 | -1): void {
  if (element.getAttribute('tabindex') !== String(value)) element.setAttribute('tabindex', String(value));
}

/**
 * 不再是停駐點的元素降回 -1。它是格子、而格子裡已經有了可以操作的元素（權限變了、編輯結束換回按鈕）時，
 * 拿掉格子的 tabIndex：格子自己是停駐點時才給它 tabIndex。
 */
export function demote(element: HTMLElement): void {
  if (!element.isConnected) return;
  if (element instanceof HTMLTableCellElement && targetsOf(element)[0] !== element) {
    element.removeAttribute('tabindex');
    return;
  }
  setTabIndex(element, -1);
}

/** 一列裡每一格的停駐點都設成 -1（停駐點那一個由呼叫端再設回 0）。 */
export function demoteRow(row: HTMLTableRowElement): void {
  Array.from(row.cells).forEach((cell) => {
    const targets = targetsOf(cell);
    if (targets[0] !== cell && cell.hasAttribute('tabindex')) cell.removeAttribute('tabindex');
    targets.forEach((target) => {
      setTabIndex(target, -1);
    });
  });
}
