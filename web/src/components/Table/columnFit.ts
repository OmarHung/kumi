import { rowsOf } from '../../lib/rovingGrid';

/*
 * 這一欄要多寬才放得下表頭與目前已經載入的每一格（docs/DESIGN.md 7.1「欄寬」）：點兩下欄寬的把手、Shift+Enter，
 * 以及沒拉過的欄第一次畫出來時（useMeasuredWidths）都用它。
 * 在真的瀏覽器量，不用字數估（中英文、標籤、日期加「已過期」的寬度差很多）。
 *
 * 欄寬是固定的（table-layout: fixed），格子裡放不下的值被裁掉或是省略號，直接量格子量到的是欄寬。
 * 所以先在這一欄的格子標上 data-measuring：components/Table/Table.module.css 讓格子的內容照自己的寬度排
 * （width: max-content，拿掉省略號與最大寬度），量完就拿掉。同一個工作裡量完、還原，畫面不會閃。
 */

const MEASURING_ATTRIBUTE = 'data-measuring';

function lengthOf(value: string): number {
  const parsed = Number.parseFloat(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

/** 不算進寬度的子元素：欄寬的把手、只給報讀軟體的隱藏文字（「不適用」「沒有優先度」）。 */
function isIgnored(element: Element): boolean {
  return element.getAttribute('role') === 'separator' || element.classList.contains('visually-hidden');
}

/**
 * 格子裡每一個直接的子節點的框：元素量它自己的框（浮起來的編輯器在 .editable 裡、絕對定位，不撐大它），
 * 文字量文字本身；把手與隱藏文字不算。
 */
function boxesOf(cell: HTMLTableCellElement): readonly DOMRect[] {
  return Array.from(cell.childNodes).flatMap((node) => {
    if (node instanceof Element) {
      return isIgnored(node) ? [] : [node.getBoundingClientRect()];
    }
    if (node.nodeType !== Node.TEXT_NODE || (node.textContent ?? '').trim() === '') return [];
    const range = document.createRange();
    // 沒有版面的環境（jsdom）沒有 Range 的框：量不到，當成沒有內容。
    if (typeof range.getBoundingClientRect !== 'function') return [];
    range.selectNodeContents(node);
    return [range.getBoundingClientRect()];
  });
}

/** 一格的內容要多寬：子節點的框從最左到最右，加上格子左右的內距。 */
function contentWidth(cell: HTMLTableCellElement): number {
  const boxes = boxesOf(cell).filter((box) => box.width > 0);
  if (boxes.length === 0) return 0;
  const left = Math.min(...boxes.map((box) => box.left));
  const right = Math.max(...boxes.map((box) => box.right));
  const style = getComputedStyle(cell);
  return right - left + lengthOf(style.paddingLeft) + lengthOf(style.paddingRight);
}

/** 每一列第 cellIndex 格（表頭也算）。 */
function cellsOf(scope: HTMLElement, cellIndex: number): readonly HTMLTableCellElement[] {
  return rowsOf(scope).flatMap((row) => {
    const cell = row.cells.item(cellIndex);
    return cell === null ? [] : [cell];
  });
}

/**
 * scope（一張表格，或共用欄寬的好幾張表格的容器）裡這幾欄各自最寬的格子（px，進位到整數），表頭也算；
 * 量不到（沒有版面）是 0。好幾欄一起標、一起量：版面只重排一次。範圍（最小、最大）由呼叫端夾。
 */
export function fitColumnWidths(scope: HTMLElement, cellIndexes: readonly number[]): number[] {
  const columns = cellIndexes.map((cellIndex) => cellsOf(scope, cellIndex));
  const cells = columns.flat();
  cells.forEach((cell) => {
    cell.setAttribute(MEASURING_ATTRIBUTE, '');
  });
  try {
    return columns.map((column) => Math.ceil(Math.max(0, ...column.map(contentWidth))));
  } finally {
    cells.forEach((cell) => {
      cell.removeAttribute(MEASURING_ATTRIBUTE);
    });
  }
}

/** 一欄（點兩下把手、Shift+Enter）。 */
export function fitColumnWidth(scope: HTMLElement, cellIndex: number): number {
  return fitColumnWidths(scope, [cellIndex])[0] ?? 0;
}
