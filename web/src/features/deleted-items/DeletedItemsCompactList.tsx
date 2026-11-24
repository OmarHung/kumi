import { List, ListRow, type ListMeta } from '../../components/List/List';
import type { DeletedItemSummary } from '../../api/itemRecovery';
import { deletedItemTypeLabel, UNKNOWN_DELETER } from './deletedItemRows';
import { DeletedAt, type DeletedItemsListProps } from './DeletedItemsTable';
import { RestoreButton } from './RestoreButton';

function metaOf(item: DeletedItemSummary, { blueprint, now }: Pick<DeletedItemsListProps, 'blueprint' | 'now'>): ListMeta[] {
  return [
    { key: 'type', label: '類型', content: deletedItemTypeLabel(blueprint, item) },
    { key: 'deletedBy', label: '刪除的人', content: item.deletedBy?.displayName ?? UNKNOWN_DELETER },
    { key: 'deletedAt', label: '刪除時間', content: <DeletedAt item={item} now={now} /> },
  ];
}

/** 窄螢幕（docs/DESIGN.md 7.1）：一筆兩行，第一行編號與標題、右邊「復原」，第二行類型、刪除的人與時間。內容和表格一樣。 */
export function DeletedItemsCompactList({ items, canRestore, pendingKeys, onRestore, ...rest }: DeletedItemsListProps) {
  return (
    <List label="已刪除的項目">
      {items.map((item, index) => (
        <ListRow
          key={item.key}
          code={item.key}
          title={item.title}
          meta={metaOf(item, rest)}
          isContentStart={index === 0}
          trailing={
            canRestore ? <RestoreButton item={item} isPending={pendingKeys.has(item.key)} onRestore={onRestore} /> : undefined
          }
        />
      ))}
    </List>
  );
}
