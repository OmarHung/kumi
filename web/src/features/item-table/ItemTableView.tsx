import { useIsCompact } from '../../lib/useMediaQuery';
import { BulkNotice } from '../bulk-edit/BulkNotice';
import { AddItemDialog } from './AddItemDialog';
import { ItemResults } from './ItemResults';
import { useProjectTable } from './ProjectTableContext';
import { QuickAddItem } from './QuickAddItem';
import type { ItemTableState } from './useItemTableState';
import styles from './ItemTableView.module.css';

/**
 * 專案的表格檢視的內容區：表格上方一列快速新增（寬螢幕、有 item.create 才有），下面是結果。
 * 篩選與「顯示的欄位」在工具列（ItemToolbar），狀態由上一層的 useItemTableState 給，兩邊看到同一份。
 * 窄螢幕的快速新增是工具列上的「新增項目」打開的對話框；對話框畫在這裡、照 table.quickAdd 開關，
 * 不跟著寬窄換掉（視窗在對話框開著時變寬，對話框與打到一半的內容都留著）。
 */
export function ItemTableView({ table }: { readonly table: ItemTableState }) {
  const { can, detail } = useProjectTable();
  const isCompact = useIsCompact();
  return (
    <section className={styles.view} aria-label="項目">
      {!isCompact && <BulkNotice projectKey={detail.project.key} bulk={table.bulk} />}
      {can.create && !isCompact && <QuickAddItem filters={table.filters} state={table.quickAdd} />}
      <ItemResults table={table} />
      {can.create && table.quickAdd.isDialogOpen && <AddItemDialog filters={table.filters} state={table.quickAdd} />}
    </section>
  );
}
