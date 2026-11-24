import { useLayoutEffect, useState, type FocusEvent, type KeyboardEvent, type RefObject } from 'react';
import { isComposingKey } from './keyboard';
import { revealCell, scrollAreaOf, scrollToRowStart } from './revealCell';
import {
  cellAt,
  demote,
  demoteRow,
  entryAt,
  GRID_ACTION_ATTRIBUTE,
  gridMoveOf,
  isRightToLeft,
  locate,
  moveFrom,
  rowKeyOf,
  rowsOf,
  setTabIndex,
  type GridEntry,
  type GridMove,
  type GridPosition,
} from './rovingGrid';
import { isFocusLost } from './updateThenFocus';

/*
 * 表格只有一個 Tab 停駐點（docs/DESIGN.md 8.1 第 8 點、8.3「清單的鍵盤操作」）：同一時間只有一格的 tabIndex 是 0，
 * 方向鍵在格子之間移動，Tab 用來離開。語意照舊是 table／row／cell（不是 role="grid"），裡面的按鈕、連結都還在。
 *
 * tabIndex 直接寫在 DOM 上，不經過 React：格子的元件（EditableCell、連結、勾選框）不必知道自己在不在停駐點上，
 * 換一個停駐點也不必重畫整張表格（50 列 × 十幾欄）。格子的內容換了（編輯結束、載入更多、重抓、顯示的欄位變了）
 * 由 MutationObserver 補上。
 */

/** 停駐點：位置加上那一列的鍵（tr 的 data-row-key），列換了位置、被拿掉時靠它找回來。 */
interface Stop extends GridPosition {
  readonly rowKey: string | null;
}

export interface RovingGridProps {
  readonly onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
  readonly onFocus: (event: FocusEvent<HTMLElement>) => void;
  readonly onBlur: (event: FocusEvent<HTMLElement>) => void;
}

export interface RovingGridHandle {
  /** 焦點放到這一列、停駐點那一欄的格子（例如「載入更多」不見之後，到新載入的第一列）。找不到那一列回 false。 */
  readonly focusRow: (rowKey: string) => boolean;
}

function hasModifier(event: KeyboardEvent): boolean {
  return event.altKey || event.ctrlKey || event.metaKey || event.shiftKey;
}

function firstDataRow(rows: readonly HTMLTableRowElement[]): Stop {
  const rowIndex = Math.max(
    rows.findIndex((row) => row.parentElement?.tagName === 'TBODY'),
    0,
  );
  return { rowIndex, cellIndex: 0, subIndex: 0, rowKey: rowKeyOf(rows[rowIndex]) };
}

/**
 * 用鍵盤走到表頭（Ctrl+Home、PageUp）：表頭黏在上方、一直看得到（revealCell 不捲它）；
 * 把它下面第一列的那一格捲進來，畫面才回到最上面。
 */
function revealBelowHeader(rows: readonly HTMLTableRowElement[], position: GridPosition): void {
  if (rows[position.rowIndex]?.parentElement?.tagName !== 'THEAD') return;
  const below = cellAt(rows, firstDataRow(rows).rowIndex, position.cellIndex);
  if (below !== null) revealCell(below);
}

/**
 * 開著自己的清單（aria-expanded="true"：打開的下拉、選單）：按鍵都是那個清單的。下拉開著時沒有處理的左右鍵
 * 照常往外傳（規範 7.3），表格這時接手的話焦點離開按鈕、清單跟著關上。
 */
function isPopupOpen(element: HTMLElement): boolean {
  return element.getAttribute('aria-expanded') === 'true';
}

function isRowStart(move: GridMove): boolean {
  return move === 'rowStart' || move === 'gridStart';
}

/**
 * 停駐點那一列不見了（重抓、換條件、被其他操作拿掉）：照原本的順序找它後面第一個還在的列，沒有就往前找；
 * 一個都不在了（整份清單換掉）就用同一個位置。
 */
function recoverRow(rows: readonly HTMLTableRowElement[], stop: Stop, previousKeys: readonly (string | null)[]): number {
  const indexByKey = new Map(rows.map((row, index) => [rowKeyOf(row), index]));
  const sameIndex = Math.min(stop.rowIndex, rows.length - 1);
  if (stop.rowKey === null) return sameIndex;
  const found = indexByKey.get(stop.rowKey);
  if (found !== undefined) return found;
  const at = previousKeys.indexOf(stop.rowKey);
  const isPresent = (key: string | null) => key !== null && indexByKey.has(key);
  const neighbour =
    previousKeys.slice(at + 1).find(isPresent) ?? previousKeys.slice(0, Math.max(at, 0)).reverse().find(isPresent);
  return (neighbour === undefined ? undefined : indexByKey.get(neighbour)) ?? sameIndex;
}

/** 停駐點那一格不在了：用列的鍵找回那一列，格子的位置夾在那一列的格數裡（隱藏了最後一欄就到前一欄）。 */
function resolveByPosition(rows: readonly HTMLTableRowElement[], stop: Stop, previousKeys: readonly (string | null)[]): Stop {
  if (rows.length === 0) return stop;
  const rowIndex = Math.max(recoverRow(rows, stop, previousKeys), 0);
  const cellCount = rows[rowIndex]?.cells.length ?? 0;
  return { ...stop, rowIndex, cellIndex: Math.min(stop.cellIndex, Math.max(cellCount - 1, 0)), rowKey: rowKeyOf(rows[rowIndex]) };
}

/** 變動碰到的列：格子的內容換了（編輯器換回按鈕）就是那一列；加進來的列（載入更多）就是新的列。 */
function touchedRows(records: readonly MutationRecord[], container: HTMLElement): readonly HTMLTableRowElement[] {
  const touched = new Set<HTMLTableRowElement>();
  const addRows = (node: Node) => {
    if (node instanceof HTMLTableRowElement) touched.add(node);
    else if (node instanceof Element) node.querySelectorAll('tr').forEach((row) => touched.add(row));
  };
  records.forEach((record) => {
    const row = record.target instanceof Element ? record.target.closest('tr') : null;
    if (row === null) record.addedNodes.forEach(addRows);
    else touched.add(row);
  });
  return Array.from(touched).filter((row) => row.isConnected && container.contains(row));
}

/** 一張表格（或放著好幾張表格的容器）的停駐點與焦點。由 useRovingGrid 建一個、掛在容器上。 */
class GridController {
  private container: HTMLElement | null = null;
  private stop: Stop | null = null;
  /** 停駐點那一格（td／th）：格子還在就以它現在的位置為準（前面多顯示一欄、列換了位置都跟得上）。 */
  private stopCell: HTMLTableCellElement | null = null;
  /** 現在 tabIndex 是 0 的元素。 */
  private current: HTMLElement | null = null;
  /** 上一次看到的列的順序：停駐點那一列被拿掉時，找它原本的鄰居。 */
  private keys: readonly (string | null)[] = [];
  /** 焦點在表格裡（包括格子被拿掉、焦點跟著掉到頁面本體的那一刻）。 */
  private hasFocus = false;
  private disconnect: (() => void) | null = null;

  private rows(): readonly HTMLTableRowElement[] {
    return this.container === null ? [] : rowsOf(this.container);
  }

  /** 停駐點在現在的表格裡的位置：那一格還在就用它（元素優先於位置），不在了才用列的鍵與原本的位置。 */
  private resolve(rows: readonly HTMLTableRowElement[], stop: Stop): Stop {
    const cell = this.stopCell;
    const row = cell?.parentElement;
    if (cell !== null && row instanceof HTMLTableRowElement && this.container?.contains(cell) === true) {
      const rowIndex = rows.indexOf(row);
      if (rowIndex >= 0) return { ...stop, rowIndex, cellIndex: cell.cellIndex, rowKey: rowKeyOf(row) };
    }
    return resolveByPosition(rows, stop, this.keys);
  }

  /**
   * 只有這一格是 0，原本是 0 的那一個降回 -1。編輯中的格子沒有停駐點可以設（焦點在編輯器裡、編輯器自己停得到）。
   * 位置對不到任何格子時退回第一個資料格：任何時候表格都至少有一個停駐點。
   */
  private settle(wanted: Stop): GridEntry | null {
    const rows = this.rows();
    const fallback = firstDataRow(rows);
    const wantedEntry = entryAt(rows, wanted);
    const next = wantedEntry === null ? fallback : wanted;
    const entry = wantedEntry ?? entryAt(rows, fallback);
    this.stop = next;
    this.stopCell = cellAt(rows, next.rowIndex, next.cellIndex);
    const target = entry?.isTarget === true ? entry.element : null;
    if (this.current !== null && this.current !== target) demote(this.current);
    this.current = target;
    if (target !== null) setTabIndex(target, 0);
    return entry;
  }

  /** 掛到容器上（換了容器就先從舊的拿下來）；null 是拿下來。 */
  connect(container: HTMLElement | null): void {
    if (container === this.container) return;
    this.disconnect?.();
    this.disconnect = null;
    if (container !== null) this.disconnect = this.attach(container);
  }

  private attach(container: HTMLElement): () => void {
    this.container = container;
    const rows = this.rows();
    rows.forEach(demoteRow);
    this.settle(this.stop === null ? firstDataRow(rows) : this.resolve(rows, this.stop));
    this.keys = rows.map(rowKeyOf);
    const observer = new MutationObserver(this.handleMutations);
    observer.observe(container, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
    document.addEventListener('pointerdown', this.handlePointerDown, true);
    return () => {
      observer.disconnect();
      document.removeEventListener('pointerdown', this.handlePointerDown, true);
      this.container = null;
      this.current = null;
    };
  }

  /**
   * 格子的內容或列換了：新的元素都不是停駐點；停駐點那一格不見了就到相鄰的一格。
   * 焦點原本在表格裡、跟著被拿掉的元素掉到頁面本體時，交給新的停駐點（例如重抓之後那一列不在條件裡了）。
   */
  private readonly handleMutations = (records: readonly MutationRecord[]) => {
    const { container, stop } = this;
    if (container === null || stop === null) return;
    const rows = this.rows();
    touchedRows(records, container).forEach(demoteRow);
    const entry = this.settle(this.resolve(rows, stop));
    this.keys = rows.map(rowKeyOf);
    if (this.hasFocus && isFocusLost()) entry?.element.focus({ preventScroll: true });
  };

  /**
   * 點在表格外面：焦點跟著離開，之後表格裡的元素被拿掉時不把焦點搶回來。
   * 例外是表格外面那一層捲動區塊本身（拖它的捲動條）：Chrome 拖捲動條不會移動焦點。
   */
  private readonly handlePointerDown = (event: PointerEvent) => {
    const { container } = this;
    const { target } = event;
    if (container === null || !(target instanceof Node) || container.contains(target)) return;
    if (target === scrollAreaOf(container)) return;
    this.hasFocus = false;
  };

  /** 任何格子取得焦點（方向鍵、點擊、程式的 focus()、updateThenFocus）就成為唯一的停駐點。 */
  readonly handleFocus = (event: FocusEvent<HTMLElement>) => {
    const { container } = this;
    const element = event.target;
    if (container === null || !container.contains(element)) return;
    this.hasFocus = true;
    revealCell(element);
    const position = locate(container, element);
    if (position === null) return;
    const subIndex = position.isTarget ? position.subIndex : (this.stop?.subIndex ?? 0);
    this.settle({ ...position, subIndex, rowKey: rowKeyOf(this.rows()[position.rowIndex]) });
  };

  readonly handleBlur = (event: FocusEvent<HTMLElement>) => {
    const { container } = this;
    if (container === null || !container.contains(event.target)) return;
    const next = event.relatedTarget;
    if (next !== null && container.contains(next)) return;
    if (next !== null) {
      this.hasFocus = false;
      return;
    }
    // 焦點去了「沒有元素」：點了空白處、切到別的視窗，或那一格被拿掉了（有的瀏覽器在移除時送出 blur）。
    // 等這一輪的畫面更新與 handleMutations 跑完再看：元素還在就是真的離開了，不在了就是被拿掉、已經交給新的停駐點。
    const left = event.target;
    window.setTimeout(() => {
      if (left.isConnected && !(this.container?.contains(document.activeElement) ?? false)) this.hasFocus = false;
    }, 0);
  };

  /**
   * 方向鍵、Home、End、PageUp、PageDown 在格子之間移動；F2 在可以編輯的格子上等於按下它（Enter 照按鈕原本的行為）；
   * 焦點在格子自己（唯讀、不適用、表頭）上時空白鍵不捲動容器。
   * 不處理：已經被內層處理過的、輸入法選字中的、不是停駐點的元素上的（編輯器、對話框透過 portal 傳上來的）、
   * 開著自己的清單的元素上的。
   */
  readonly handleKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    const { container } = this;
    if (container === null || event.defaultPrevented || isComposingKey(event)) return;
    const element = event.target;
    if (!(element instanceof HTMLElement) || !container.contains(element) || isPopupOpen(element)) return;
    const position = locate(container, element);
    if (position === null || !position.isTarget) return;
    if ((event.key === 'F2' || event.key === ' ') && !hasModifier(event)) {
      this.press(event, element);
      return;
    }
    const move = gridMoveOf(event, isRightToLeft(container));
    if (move === null) return;
    event.preventDefault();
    const rows = this.rows();
    const next = moveFrom(rows, position, move);
    entryAt(rows, next)?.element.focus({ preventScroll: true });
    revealBelowHeader(rows, next);
    if (isRowStart(move)) scrollToRowStart(container);
  };

  /**
   * F2：可以編輯的格子（表身裡的按鈕，標了 GRID_ACTION_ATTRIBUTE 的動作按鈕除外）進入編輯。
   * 空白鍵：在格子自己上不做事（瀏覽器預設是捲動容器）。
   */
  private press(event: KeyboardEvent<HTMLElement>, element: HTMLElement): void {
    if (event.key === ' ') {
      if (element instanceof HTMLTableCellElement) event.preventDefault();
      return;
    }
    if (element instanceof HTMLButtonElement && element.closest('tbody') !== null && !element.hasAttribute(GRID_ACTION_ATTRIBUTE)) {
      event.preventDefault();
      element.click();
    }
  }

  readonly focusRow = (rowKey: string): boolean => {
    const rows = this.rows();
    const rowIndex = rows.findIndex((row) => rowKeyOf(row) === rowKey);
    const entry = rowIndex < 0 ? null : entryAt(rows, { rowIndex, cellIndex: this.stop?.cellIndex ?? 0, subIndex: 0 });
    // preventScroll：停駐點那一欄可能是固定欄（瀏覽器自己的捲動會把表格橫向捲回去），捲動交給 handleFocus。
    entry?.element.focus({ preventScroll: true });
    return entry !== null;
  };
}

export interface RovingGrid {
  /** 展開到表格（或容器）上：方向鍵、焦點進出。 */
  readonly gridProps: RovingGridProps;
  readonly handle: RovingGridHandle;
}

/**
 * 掛在一張表格上（containerRef 指向 table，或放著好幾張表格的容器）：容器裡每一個 tr 是一列，表頭是第 0 列。
 * 列要帶 data-row-key（項目編號），停駐點那一列被拿掉時才找得到原本的鄰居。
 */
export function useRovingGrid(containerRef: RefObject<HTMLElement | null>): RovingGrid {
  const [grid] = useState(() => new GridController());
  // 每次畫完都看一次容器是不是同一個元素：換了元素（例如表格整個重新掛上）就從舊的拿下來、掛到新的上。
  useLayoutEffect(() => {
    grid.connect(containerRef.current);
  });
  useLayoutEffect(
    () => () => {
      grid.connect(null);
    },
    [grid],
  );
  const [api] = useState(() => ({
    gridProps: { onKeyDown: grid.handleKeyDown, onFocus: grid.handleFocus, onBlur: grid.handleBlur },
    handle: { focusRow: grid.focusRow },
  }));
  return api;
}
