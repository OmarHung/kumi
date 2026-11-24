import type { ColumnWidths, WidthBounds } from '../../components/Table/columnWidths';
import { clampWidth, DEFAULT_BOUNDS } from '../../components/Table/columnWidths';
import type { TableColumn, TableVariables } from '../../components/Table/Table';
import type { MeasuredColumn } from '../../components/Table/useMeasuredWidths';
import type { ColumnWidthKind, ItemColumnMeta } from './columns';
import styles from './ItemTable.module.css';

/*
 * 專案的表格的欄寬（docs/DESIGN.md 7.1「欄寬」）：每一欄的預設寬度（ItemTable.module.css 的區域變數）、可以調的範圍，
 * 以及使用者拉的寬度怎麼套上去。記在這個瀏覽器、每個專案一份（和顯示的欄位同一套，columnVisibility.ts）。
 */

const STORAGE_PREFIX = 'item-table.widths.';

export function widthStorageKey(projectKey: string): string {
  return STORAGE_PREFIX + projectKey;
}

/** 固定在左邊的欄最寬到這裡：再寬的話，表格變窄時（開著面板）固定欄就蓋滿畫面，其他欄捲不出來。 */
const KEY_BOUNDS: WidthBounds = { min: 3, max: 16 };
// 標題是認項目用的，最少留六個字左右。
const TITLE_BOUNDS: WidthBounds = { min: 6, max: 32 };

export function boundsOf(meta: ItemColumnMeta | undefined): WidthBounds {
  if (meta?.pin === 'key') return KEY_BOUNDS;
  if (meta?.pin === 'title') return TITLE_BOUNDS;
  return DEFAULT_BOUNDS;
}

/**
 * 沒拉過時照內容量寬度的欄（照顯示的順序，index 是第幾格）：固定的編號與標題以外的每一欄。
 * 固定欄維持原本的預設寬度（編號 6.5rem、標題看表格的寬度 16rem／22rem）：標題照內容量會把長標題撐得很寬。
 */
export function measuredColumnsOf(
  columns: readonly { readonly id: string; readonly meta: ItemColumnMeta | undefined }[],
): readonly MeasuredColumn[] {
  return columns.flatMap((column, index) =>
    column.meta?.pin === undefined ? [{ id: column.id, index, bounds: boundsOf(column.meta) }] : [],
  );
}

/** 量之前（與量不到時）的預設寬度。 */
const KIND_CLASSES: Readonly<Record<ColumnWidthKind, string | undefined>> = {
  short: styles.colShort,
  name: styles.colName,
  date: styles.colDate,
  due: styles.colDue,
  text: styles.colText,
};

/**
 * 一欄的 col：固定欄的寬度是捲動區塊上的變數（欄寬、標題的省略號、捲動讓出的寬度用同一個，pinnedVariables），
 * 其他欄拉過就是那個寬度，沒拉過用預設寬度的 class。
 */
export function tableColumnOf(id: string, meta: ItemColumnMeta | undefined, widths: ColumnWidths): TableColumn {
  if (meta?.pin === 'select') return { key: id, className: styles.colSelect };
  if (meta?.pin === 'key') return { key: id, className: styles.colKey };
  if (meta?.pin === 'title') return { key: id, className: styles.colTitle };
  const width = widths[id];
  // 記住的寬度畫在範圍裡：localStorage 可能被改壞（{"due_date": 9000}）。
  return {
    key: id,
    className: KIND_CLASSES[meta?.width ?? 'name'],
    width: width === undefined ? undefined : clampWidth(width, boundsOf(meta)),
  };
}

/**
 * 固定欄拉過的寬度，換掉捲動區塊上的變數。標題欄夾在表格寬度（容器 item-table 的 100cqi）減掉編號欄與一段保留的寬度之內：
 * 寬螢幕拉寬之後開了面板，標題欄跟著縮，不會蓋住整個表格（預設寬度也是看容器的寬度換，ItemTable.module.css）。
 */
export function pinnedVariables(widths: ColumnWidths): TableVariables {
  // 畫在範圍裡：記住的寬度可能被改壞（{"key": 5000} 的編號欄會蓋滿畫面）。
  const key = widths.key === undefined ? undefined : clampWidth(widths.key, KEY_BOUNDS);
  const title = widths.title === undefined ? undefined : clampWidth(widths.title, TITLE_BOUNDS);
  return {
    ...(key === undefined ? {} : { '--item-key-width': `${String(key)}px` }),
    ...(title === undefined
      ? {}
      : { '--item-title-width': `min(${String(title)}px, 100cqi - var(--item-key-width) - var(--item-scroll-reserve))` }),
  };
}
