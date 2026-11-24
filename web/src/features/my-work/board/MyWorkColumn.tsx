import { useDroppable } from '@dnd-kit/core';
import { useId, useRef } from 'react';
import type { StateCategory } from '../../../api/blueprint';
import type { Item } from '../../../api/types';
import { workflowForType } from '../../../domain/blueprint';
import { STATE_CATEGORY_LABELS } from '../../../domain/labels';
import { cx } from '../../../lib/cx';
import { CardSkeleton } from '../../board/BoardCard';
import { blockedReason } from '../../board/moveLabels';
import { useItemSequence } from '../../item-page/ItemPanelContext';
import { StateBadge } from '../../work-items/ItemCells';
import { planDrop } from './boardColumns';
import { useMyWorkBoard } from './MyWorkBoardContext';
import { MyWorkCard } from './MyWorkCard';
import type { CategoryColumnData } from './myWorkBoardDnd';
import boardStyles from '../../board/Board.module.css';

/** 拖曳中這一欄走不走得到（依那張卡片自己專案的藍圖與角色）。沒有在拖、或就是卡片原本的類別時不是落點。 */
function useDropBlock(category: StateCategory): { readonly isTarget: boolean; readonly reason: string | null } {
  const { activeItem, projects } = useMyWorkBoard();
  const detail = activeItem === null ? undefined : projects.get(activeItem.projectKey);
  if (activeItem === null || detail === undefined || activeItem.stateCategory === category) {
    return { isTarget: false, reason: null };
  }
  const workflow = workflowForType(detail.blueprint, activeItem.type);
  const plan = planDrop(workflow, activeItem, category, detail.project.myRole);
  switch (plan.kind) {
    case 'blocked':
      return { isTarget: true, reason: blockedReason(plan.availability, detail.blueprint) };
    case 'empty':
      return { isTarget: true, reason: `流程沒有「${STATE_CATEGORY_LABELS[category]}」的狀態` };
    case 'ignore':
      return { isTarget: false, reason: null };
    default:
      return { isTarget: true, reason: null };
  }
}

interface MyWorkColumnProps {
  readonly category: StateCategory;
  readonly items: readonly Item[];
  /** 第一次載入中：卡片形狀的骨架。 */
  readonly isLoading: boolean;
  /** 還有沒載入的項目：數量寫成「n+」。 */
  readonly hasMore: boolean;
  readonly emptyText: string;
}

/**
 * 看板的一欄：一個狀態類別（各專案的流程不同，沒有共同的狀態欄）。凹下去的軌道，剛好是看板的高度、自己直向捲動，
 * 標題列一直看得到；欄頭沒有色條，類別由 StateBadge 的形狀、顏色與文字說明。也是拖曳的落點（整欄，含標題列）。
 */
export function MyWorkColumn({ category, items, isLoading, hasMore, emptyText }: MyWorkColumnProps) {
  const { pending, activeItem } = useMyWorkBoard();
  const block = useDropBlock(category);
  const data: CategoryColumnData = { kind: 'category-column', category, blockedReason: block.reason };
  const { setNodeRef, isOver } = useDroppable({ id: `category:${category}`, data });
  const headingId = useId();
  // 項目面板的上一個／下一個在同一欄裡走（照這一欄現在的順序）；關上面板時焦點回到這一欄的那張卡片。
  const cardsRef = useRef<HTMLUListElement>(null);
  useItemSequence(items.map((item) => item.key), cardsRef);
  const label = STATE_CATEGORY_LABELS[category];
  const className = cx(
    boardStyles.column,
    activeItem !== null && block.isTarget && (block.reason === null ? boardStyles.canDrop : boardStyles.cannotDrop),
    isOver && block.isTarget && boardStyles.over,
  );
  return (
    <section ref={setNodeRef} className={className} aria-labelledby={headingId} aria-busy={isLoading || undefined}>
      <div className={boardStyles.laneScroll}>
        <div className={boardStyles.laneBody}>
          <header className={boardStyles.columnHeader}>
            <div className={boardStyles.columnTitle}>
              <h2 id={headingId} className={boardStyles.columnName} title={label}>
                <StateBadge label={label} category={category} />
              </h2>
              {!isLoading && (
                <p className={boardStyles.count}>
                  <span aria-hidden="true">{hasMore ? `${String(items.length)}+` : String(items.length)}</span>
                  <span className="visually-hidden">{`已載入 ${String(items.length)} 個項目${hasMore ? '，還有更多' : ''}`}</span>
                </p>
              )}
            </div>
            {block.reason !== null && <p className={boardStyles.blockedNote}>{`走不到：${block.reason}`}</p>}
          </header>
          {isLoading && <CardSkeleton />}
          {!isLoading && items.length === 0 && <p className={boardStyles.empty}>{emptyText}</p>}
          {!isLoading && items.length > 0 && (
            <ul ref={cardsRef} className={boardStyles.cards}>
              {items.map((item, index) => (
                // 每一欄的第一張卡片都標上 data-content-start：scripts/ui-audit.js 取文件裡的第一個，
                // 第一欄是空的也量得到「看板的第一張卡片」（docs/DESIGN.md 6.2）。
                <li key={item.key} data-content-start={index === 0 || undefined}>
                  <MyWorkCard item={item} isPending={pending.some((move) => move.item.key === item.key)} />
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </section>
  );
}
