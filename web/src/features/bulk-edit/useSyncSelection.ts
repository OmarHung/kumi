import { useEffect } from 'react';
import type { Item } from '../../api/types';
import type { ItemSelection } from './useItemSelection';

/**
 * 表格把載入的項目告訴選取（重抓之後不在清單裡的編號拿掉）。選取只對寬螢幕的表格有意思：
 * 窄螢幕（清單）與離開表格（換檢視、換頁）時清空。
 */
export function useSyncSelection(selection: ItemSelection, items: readonly Item[], isCompact: boolean): void {
  const { syncLoaded, clear } = selection;
  useEffect(() => {
    if (isCompact) clear();
    else syncLoaded(items);
  }, [isCompact, items, syncLoaded, clear]);
  useEffect(() => clear, [clear]);
}
