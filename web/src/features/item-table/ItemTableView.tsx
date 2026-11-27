import { createPortal } from 'react-dom';
import { useIsBelowWide, useIsCompact } from '../../lib/useMediaQuery';
import { BulkNotice } from '../bulk-edit/BulkNotice';
import { ActiveFlags } from './ActiveFlags';
import { ItemResults } from './ItemResults';
import { useProjectTable } from './ProjectTableContext';
import { QuickAddItem } from './QuickAddItem';
import { useQuickAddHost } from './QuickAddSlot';
import type { ItemTableState } from './useItemTableState';
import styles from './ItemTableView.module.css';

/**
 * 專案的表格檢視的內容區：表頭下面的第一列是快速新增（64rem 以上、有 item.create 才有），再下面是結果。
 * 篩選與「顯示的欄位」在工具列（ItemToolbar），狀態由上一層的 useItemTableState 給，兩邊看到同一份。
 * 64rem 以下的快速新增是右下角的按鈕（NewItemFab）打開的對話框；按鈕與對話框都在專案頁（ProjectWorkspace，
 * 四種檢視共用），不跟著寬窄換掉。這裡只在清單最後多留一個按鈕的高度，最後一列不被蓋住。
 * 快速新增的位置由結果決定（有表格時併進表格，沒有表格時單獨一列，見 ItemResults），表單只畫一次、畫進搬得動的容器（QuickAddSlot）。
 */
export function ItemTableView({ table }: { readonly table: ItemTableState }) {
  const { can, detail } = useProjectTable();
  const isCompact = useIsCompact();
  const isBelowWide = useIsBelowWide();
  // 窄螢幕一定也不到寬螢幕；兩個都問，不靠媒體查詢的實作剛好一致。
  const isNarrow = isBelowWide || isCompact;
  const hasFab = can.create && isNarrow;
  const host = useQuickAddHost(can.create && !isNarrow);
  return (
    <section className={styles.view} aria-label="項目" data-has-fab={hasFab || undefined}>
      {!isCompact && <BulkNotice projectKey={detail.project.key} bulk={table.bulk} />}
      <ActiveFlags table={table} />
      <ItemResults table={table} quickAddHost={host} />
      {host !== null && createPortal(<QuickAddItem filters={table.filters} state={table.quickAdd} />, host)}
    </section>
  );
}
