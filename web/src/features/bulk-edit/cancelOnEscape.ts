import type { KeyboardEvent } from 'react';

/** 開著的下拉、選單、浮層與對話框：Esc 是它們的（關上自己），不是「取消選取」。 */
const OPEN_POPUP = '[aria-expanded="true"], [role="dialog"], [role="listbox"], [role="menu"]';
/** 格子裡的編輯器（就地編輯）：Esc 是取消編輯。 */
const CELL_EDITOR = '[data-cell-editor]';

/**
 * Esc 取消選取（docs/DESIGN.md 8.3「批次操作」）：焦點在表格或批次動作列上、沒有開著的編輯器或浮層時。
 * 開著的下拉與浮層自己處理 Esc（關上、不往外傳），但它們在同一段 DOM 裡，編輯器與對話框也可能把事件透過 React 傳上來，
 * 所以這裡也看一眼：已經被處理過的、焦點在開著的東西裡的、不在這一塊 DOM 裡的（portal）都不接手。
 */
export function cancelOnEscape(event: KeyboardEvent<HTMLElement>, onCancel: () => void): void {
  if (event.key !== 'Escape' || event.defaultPrevented) return;
  const { target, currentTarget } = event;
  if (!(target instanceof Element) || !currentTarget.contains(target)) return;
  if (target.closest(OPEN_POPUP) !== null || target.closest(CELL_EDITOR) !== null) return;
  onCancel();
}
