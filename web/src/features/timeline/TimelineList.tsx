import type { ProjectBlueprint } from '../../api/blueprint';
import type { Item } from '../../api/types';
import { List, ListRow } from '../../components/List/List';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { stateMeta } from '../work-items/itemListMeta';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import type { TimelineRow } from './timelineModel';

/**
 * 窄螢幕（48rem 以下）取代時間軸的清單：照開始日排，每一筆寫日期範圍。
 * 理由：時間軸要橫向捲、一天只有幾 px 寬，手指點不準也看不出全貌；日期寫成文字，清單就能讀完整件事。
 * 窄螢幕不用面板：連結進整頁（和表格、看板一樣）。
 * 狀態的名稱照項目的專案藍圖（blueprintOf）：專案頁都是同一個，「我的工作」是每個項目自己的；還沒讀到時用類別的名稱。
 */
export function TimelineList({ rows, blueprintOf }: { readonly rows: readonly TimelineRow[]; readonly blueprintOf: (item: Item) => ProjectBlueprint | undefined }) {
  const entry = useItemPageEntry();
  useItemSequence(rows.map((row) => row.item.key));
  return (
    <List label="項目" layout="fill">
      {rows.map((row, index) => (
        <ListRow
          key={row.item.key}
          to={itemPagePath(row.item.projectKey, row.item.key)}
          linkState={entry}
          code={row.item.key}
          title={row.item.title}
          isContentStart={index === 0}
          meta={[
            { key: 'dates', label: '期間', content: row.range },
            stateMeta(row.item, blueprintOf(row.item)),
            ...(row.isOverdue ? [{ key: 'overdue', content: '已過期', isVisuallyHidden: true }] : []),
          ]}
        />
      ))}
    </List>
  );
}
