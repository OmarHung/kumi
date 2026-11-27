import type { ReactNode } from 'react';
import type { Item } from '../../api/types';
import { List } from '../../components/List/List';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { ItemListRow } from '../work-items/ItemListRow';
import { useProjectTable } from './ProjectTableContext';

interface ProjectItemListProps {
  readonly items: readonly Item[];
  readonly isRefreshing: boolean;
  readonly footer: ReactNode;
}

/** 窄螢幕的專案項目：清單取代表格（docs/DESIGN.md 7.1，一筆的排法在 ItemListRow）。連結和表格的編號一樣帶著現在的網址，刪除之後回得來。 */
export function ProjectItemList({ items, isRefreshing, footer }: ProjectItemListProps) {
  const table = useProjectTable();
  // 窄螢幕不用面板；登記順序是為了從整頁用上一頁回來時，焦點放回這份清單上的那一筆。
  useItemSequence(items.map((item) => item.key));
  return (
    <List label="項目" layout="fill" isBusy={isRefreshing} footer={footer}>
      {items.map((item, index) => (
        <ItemListRow key={item.key} item={item} blueprint={table.detail.blueprint} todayDate={table.todayDate} isContentStart={index === 0} />
      ))}
    </List>
  );
}
