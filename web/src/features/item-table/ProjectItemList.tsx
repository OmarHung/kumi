import type { ReactNode } from 'react';
import type { Item } from '../../api/types';
import { List, ListRow, type ListMeta } from '../../components/List/List';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { assigneeMeta, dueMeta, stateMeta } from '../work-items/itemListMeta';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import { useProjectTable, type ProjectTableContextValue } from './ProjectTableContext';

/**
 * 第二行：狀態、負責人、到期日（有設才有），欄位與說法在 work-items/itemListMeta.tsx（和我的工作、下層項目共用）。
 */
function itemMeta(item: Item, { detail, todayDate }: Pick<ProjectTableContextValue, 'detail' | 'todayDate'>): ListMeta[] {
  return [stateMeta(item, detail.blueprint), assigneeMeta(item), ...dueMeta(item, todayDate)];
}

interface ProjectItemListProps {
  readonly items: readonly Item[];
  readonly isRefreshing: boolean;
  readonly footer: ReactNode;
}

/** 窄螢幕的專案項目：清單取代表格（docs/DESIGN.md 7.1）。連結和表格的編號一樣帶著現在的網址，刪除之後回得來。 */
export function ProjectItemList({ items, isRefreshing, footer }: ProjectItemListProps) {
  const entry = useItemPageEntry();
  const table = useProjectTable();
  // 窄螢幕不用面板；登記順序是為了從整頁用上一頁回來時，焦點放回這份清單上的那一筆。
  useItemSequence(items.map((item) => item.key));
  return (
    <List label="項目" layout="fill" isBusy={isRefreshing} footer={footer}>
      {items.map((item, index) => (
        <ListRow
          key={item.key}
          to={itemPagePath(item.projectKey, item.key)}
          linkState={entry}
          code={item.key}
          title={item.title}
          meta={itemMeta(item, table)}
          isContentStart={index === 0}
        />
      ))}
    </List>
  );
}
