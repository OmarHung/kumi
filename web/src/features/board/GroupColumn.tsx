import { useDroppable } from '@dnd-kit/core';
import { useId, useRef } from 'react';
import type { ItemTypeDefinition } from '../../api/blueprint';
import type { Item } from '../../api/types';
import {
  columnKeyOf,
  dropBlockedReason,
  notTargetReason,
  type GroupColumn as GroupColumnModel,
} from '../../domain/boardGroupColumns';
import { hasActiveBoardFilters } from '../../domain/projectView';
import { cx } from '../../lib/cx';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { BoardCard } from './BoardCard';
import { useBoard } from './BoardContext';
import { QuickAddForm, QuickAddToggle, useBoardQuickAdd, type ColumnSeed } from './BoardQuickAdd';
import type { GroupColumnData } from './groupDnd';
import styles from './Board.module.css';

/** 拖曳中這一欄放不放得進來：沒有在拖、或就是卡片原本的欄時 isTarget 是 false。 */
function useDropBlock(column: GroupColumnModel): { readonly isTarget: boolean; readonly reason: string | null } {
  const { detail } = useProjectTable();
  const { group, activeItem } = useBoard();
  if (activeItem === null || columnKeyOf(activeItem, group) === column.key) {
    return { isTarget: false, reason: null };
  }
  const reason = column.isTarget ? dropBlockedReason(activeItem, group, column.key, detail.blueprint) : notTargetReason(group);
  return { isTarget: true, reason };
}

/** 拖曳的落點（整欄，含標題列）與它的樣子：放得進來、放不進來、指標在上面。 */
function useColumnDrop(column: GroupColumnModel) {
  const { activeItem } = useBoard();
  const block = useDropBlock(column);
  const data: GroupColumnData = { kind: 'group-column', columnKey: column.key, name: column.name, blockedReason: block.reason };
  const { setNodeRef, isOver } = useDroppable({ id: column.key, data });
  const className = cx(
    styles.column,
    activeItem !== null && block.isTarget && (block.reason === null ? styles.canDrop : styles.cannotDrop),
    isOver && block.isTarget && styles.over,
  );
  return { setNodeRef, className, reason: block.reason };
}

interface GroupColumnProps {
  readonly column: GroupColumnModel;
  readonly items: readonly Item[];
  /** 還有沒讀進來的項目：這一欄的數量是下限（「5+」）。 */
  readonly hasMore: boolean;
  /** 這一欄新增項目時帶的值；沒有（「沒有…」那一欄、不能當落點的欄、沒有新增權限）就沒有新增。 */
  readonly seed: ColumnSeed | null;
  /** 新增時能選的類型（依欄位分欄時只有帶這個欄位的）。 */
  readonly types: readonly ItemTypeDefinition[];
  /** 新項目放進哪一份清單。 */
  readonly listKey: readonly unknown[];
}

/**
 * 依負責人、欄位分欄時的一欄：一位成員或一個選項。外觀、捲動與拖曳的落點和依狀態的欄（BoardColumn）一樣，
 * 標題是名稱的文字（沒有狀態的形狀）；項目是一次讀進來再分欄的（GroupBoard），這裡只畫自己的。
 */
export function GroupColumn({ column, items, hasMore, seed, types, listKey }: GroupColumnProps) {
  const { filters, pending } = useBoard();
  const { setNodeRef, className, reason } = useColumnDrop(column);
  const headingId = useId();
  const cardsRef = useRef<HTMLUListElement>(null);
  const quickAdd = useBoardQuickAdd();
  // 項目面板的上一個／下一個在同一欄裡走。
  useItemSequence(items.map((item) => item.key), cardsRef);
  const empty = hasActiveBoardFilters(filters) ? '沒有符合條件的項目' : '這一欄沒有項目';
  return (
    <section ref={setNodeRef} className={className} aria-labelledby={headingId}>
      <div className={styles.laneScroll}>
        <div className={styles.laneBody}>
          <header className={styles.columnHeader}>
            <div className={styles.columnTitle}>
              <h2 id={headingId} className={cx(styles.columnName, column.isEmpty && styles.columnNameEmpty)} title={column.name}>
                <span className={styles.columnNameText}>{column.name}</span>
              </h2>
              <p className={styles.count}>
                <span aria-hidden="true">{hasMore ? `${String(items.length)}+` : String(items.length)}</span>
                <span className="visually-hidden">{`已載入 ${String(items.length)} 個項目${hasMore ? '，還有更多' : ''}`}</span>
              </p>
              {seed !== null && <QuickAddToggle quickAdd={quickAdd} />}
            </div>
            {reason !== null && <p className={styles.blockedNote}>{`放不進去：${reason}`}</p>}
          </header>
          {seed !== null && quickAdd.isOpen && <QuickAddForm listKey={listKey} quickAdd={quickAdd} seed={seed} types={types} />}
          {items.length === 0 ? (
            <p className={styles.empty}>{empty}</p>
          ) : (
            <ul ref={cardsRef} className={styles.cards}>
              {items.map((item, index) => (
                <li key={item.key} data-content-start={index === 0 || undefined}>
                  <BoardCard item={item} isPending={pending.some((move) => move.item.key === item.key)} />
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </section>
  );
}
