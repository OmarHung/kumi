import type { ProjectBlueprint } from '../../api/blueprint';
import type { DeletedItemSummary } from '../../api/itemRecovery';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { deletedItemTypeLabel, UNKNOWN_DELETER } from './deletedItemRows';
import { RestoreButton } from './RestoreButton';
import styles from './DeletedItems.module.css';

export interface DeletedItemsListProps {
  readonly blueprint: ProjectBlueprint;
  readonly items: readonly DeletedItemSummary[];
  /** 算相對時間用的「現在」：整份清單用同一個值。 */
  readonly now: number;
  /** 沒有 item.delete 時是 false：不畫「復原」。 */
  readonly canRestore: boolean;
  readonly pendingKeys: ReadonlySet<string>;
  readonly onRestore: (item: DeletedItemSummary) => void;
}

export function DeletedAt({ item, now }: { readonly item: DeletedItemSummary; readonly now: number }) {
  return (
    <time dateTime={item.deletedAt} title={formatInstant(item.deletedAt)}>
      {formatRelativeTime(item.deletedAt, now)}
    </time>
  );
}

interface RowProps extends Omit<DeletedItemsListProps, 'items'> {
  readonly item: DeletedItemSummary;
  readonly isFirst: boolean;
}

function DeletedItemRow({ item, blueprint, now, canRestore, pendingKeys, onRestore, isFirst }: RowProps) {
  return (
    <tr data-content-start={isFirst || undefined}>
      <th scope="row" className={styles.key}>
        {item.key}
      </th>
      <td className={tableStyles.primaryCell}>{item.title}</td>
      <td className={tableStyles.nowrap}>{deletedItemTypeLabel(blueprint, item)}</td>
      <td className={item.deletedBy === null ? tableStyles.muted : tableStyles.nowrap}>
        {item.deletedBy?.displayName ?? UNKNOWN_DELETER}
      </td>
      <td className={tableStyles.nowrap}>
        <DeletedAt item={item} now={now} />
      </td>
      {canRestore && (
        <td className={styles.action}>
          <RestoreButton item={item} isPending={pendingKeys.has(item.key)} onRestore={onRestore} />
        </td>
      )}
    </tr>
  );
}

/** 寬螢幕：編號（等寬）、標題、類型、刪除的人、刪除時間、最右一顆「復原」。 */
export function DeletedItemsTable({ items, ...rest }: DeletedItemsListProps) {
  return (
    <TableScroll label="已刪除的項目" layout="page">
      <table className={tableStyles.table}>
        <caption className="visually-hidden">已刪除的項目</caption>
        <thead>
          <tr>
            <th scope="col">編號</th>
            <th scope="col">標題</th>
            <th scope="col">類型</th>
            <th scope="col">刪除的人</th>
            <th scope="col">刪除時間</th>
            {rest.canRestore && (
              <th scope="col">
                <span className="visually-hidden">動作</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody>
          {items.map((item, index) => (
            <DeletedItemRow key={item.key} item={item} isFirst={index === 0} {...rest} />
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}
