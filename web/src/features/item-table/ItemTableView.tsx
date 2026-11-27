import { useIsCompact } from '../../lib/useMediaQuery';
import { BulkNotice } from '../bulk-edit/BulkNotice';
import { ActiveFlags } from './ActiveFlags';
import { ItemResults } from './ItemResults';
import { useProjectTable } from './ProjectTableContext';
import { QuickAddItem } from './QuickAddItem';
import type { ItemTableState } from './useItemTableState';
import styles from './ItemTableView.module.css';

/**
 * 專案的表格檢視的內容區：表格上方一列快速新增（寬螢幕、有 item.create 才有），下面是結果。
 * 篩選與「顯示的欄位」在工具列（ItemToolbar），狀態由上一層的 useItemTableState 給，兩邊看到同一份。
 * 窄螢幕的快速新增是右下角的圓形按鈕（NewItemFab）打開的對話框；按鈕與對話框都在專案頁（ProjectWorkspace，
 * 四種檢視共用），不跟著寬窄換掉。這裡只在清單最後多留一個按鈕的高度，最後一列不被蓋住。
 */
export function ItemTableView({ table }: { readonly table: ItemTableState }) {
  const { can, detail } = useProjectTable();
  const isCompact = useIsCompact();
  return (
    <section className={styles.view} aria-label="項目" data-has-fab={(can.create && isCompact) || undefined}>
      {!isCompact && <BulkNotice projectKey={detail.project.key} bulk={table.bulk} />}
      <ActiveFlags table={table} />
      {can.create && !isCompact && <QuickAddItem filters={table.filters} state={table.quickAdd} />}
      <ItemResults table={table} />
    </section>
  );
}
