import { useDroppable } from '@dnd-kit/core';
import { useId, useMemo } from 'react';
import type { WorkflowState } from '../../api/blueprint';
import { useBoardColumnItems } from '../../api/queries';
import { queryKeys } from '../../api/queryKeys';
import type { Item } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { CURRENT_CYCLE } from '../../domain/planningFilter';
import { boardColumnQuery, matchesBoardFilters } from '../../domain/projectView';
import { moveAvailability } from '../../domain/workflow';
import { cx } from '../../lib/cx';
import { flattenItemPages, insertByNumber } from '../item-table/itemCache';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { usePlanning } from '../item-table/usePlanning';
import { useBoard } from './BoardContext';
import { ColumnBody, ColumnHeader } from './BoardColumnParts';
import type { ColumnData } from './boardDnd';
import { QuickAddForm, useBoardQuickAdd } from './BoardQuickAdd';
import { blockedReason } from './moveLabels';
import type { BoardMove } from './useBoardMoves';
import styles from './Board.module.css';
import { useWeekStart } from '../../lib/weekStart';

/**
 * 這一欄要畫的卡片：伺服器的結果，拿掉正在送出、要移去別欄的，再照編號放進正在移過來的。
 * 送出中的移動不寫進快取：背景重抓帶回舊位置也不會出現兩張；失敗時什麼都不必復原，卡片自然回到原欄。
 * 送出中換了流程或條件時，移過來的卡片要符合這一欄的類型與條件（accepts）才畫出來。
 */
function columnItems(
  base: readonly Item[],
  state: WorkflowState,
  pending: readonly BoardMove[],
  accepts: (item: Item) => boolean,
): readonly Item[] {
  const moving = new Set(pending.map((move) => move.item.key));
  const staying = base.filter((item) => !moving.has(item.key) && item.state === state.key);
  return pending
    .filter((move) => move.to === state.key && accepts(move.item))
    .reduce<readonly Item[]>(
      (items, move) => insertByNumber(items, { ...move.item, state: state.key, stateCategory: state.category }),
      staying,
    );
}

/** 送出中的卡片要符合這一欄的條件：這個流程的類型、搜尋、負責人。 */
function usePendingFilter(): (item: Item) => boolean {
  const { personId } = useCurrentUser();
  const { detail, todayDate } = useProjectTable();
  const { cycles } = usePlanning(detail.project.key);
  const weekStart = useWeekStart();
  const { types, filters } = useBoard();
  const typeKeys = new Set(types.map((itemType) => itemType.key));
  return (item) => typeKeys.has(item.type) && matchesBoardFilters(item, filters, personId, todayDate, weekStart, cycles);
}

/** 拖曳中這一欄走不走得到（依藍圖與自己的角色）。沒有在拖、或就是卡片原本的欄時是 null。 */
function useDropBlock(state: WorkflowState): { readonly isTarget: boolean; readonly reason: string | null } {
  const { detail } = useProjectTable();
  const { workflow, activeItem } = useBoard();
  if (workflow === null || activeItem === null || activeItem.state === state.key) {
    return { isTarget: false, reason: null };
  }
  const availability = moveAvailability(workflow, activeItem.state, state.key, detail.project.myRole);
  return { isTarget: true, reason: blockedReason(availability, detail.blueprint) };
}

/** 拖曳的落點（整欄，含標題列）與它的樣子：走得到、走不到、指標在上面。 */
function useColumnDrop(state: WorkflowState) {
  const { activeItem } = useBoard();
  const block = useDropBlock(state);
  const data: ColumnData = { kind: 'column', state, blockedReason: block.reason };
  const { setNodeRef, isOver } = useDroppable({ id: state.key, data });
  const className = cx(
    styles.column,
    activeItem !== null && block.isTarget && (block.reason === null ? styles.canDrop : styles.cannotDrop),
    isOver && block.isTarget && styles.over,
  );
  return { setNodeRef, className, reason: block.reason };
}

interface BoardColumnProps {
  readonly state: WorkflowState;
  /** 新項目一律從流程的第一個狀態開始：快速新增放在第一欄。 */
  readonly isFirst: boolean;
}

/**
 * 看板的一欄：一個狀態。凹下去的軌道，剛好是看板的高度、自己直向捲動，標題列一直看得到。
 * 各自讀、各自「載入更多」；也是拖曳的落點。
 */
export function BoardColumn({ state, isFirst }: BoardColumnProps) {
  const { detail, can, todayDate } = useProjectTable();
  const { types, filters, pending } = useBoard();
  const { cycles, isCyclesPending } = usePlanning(detail.project.key);
  const weekStart = useWeekStart();
  const query = boardColumnQuery(filters, types.map((itemType) => itemType.key), state.key, todayDate, weekStart, cycles);
  // 篩「目前的週期」要等週期清單：換成編號之前先送，會多打一次沒有結果的請求。
  const result = useBoardColumnItems(detail.project.key, query, !(filters.cycle === CURRENT_CYCLE && isCyclesPending));
  const base = useMemo(() => flattenItemPages(result.data), [result.data]);
  const items = columnItems(base, state, pending, usePendingFilter());
  const { setNodeRef, className, reason } = useColumnDrop(state);
  const headingId = useId();
  const quickAdd = useBoardQuickAdd();
  const canAdd = isFirst && can.create;
  return (
    <section ref={setNodeRef} className={className} aria-labelledby={headingId} aria-busy={result.isPlaceholderData || undefined}>
      <div className={styles.laneScroll}>
        <div className={styles.laneBody}>
          <ColumnHeader
            state={state}
            headingId={headingId}
            result={result}
            count={items.length}
            blockedReason={reason}
            quickAdd={canAdd ? quickAdd : null}
          />
          {canAdd && quickAdd.isOpen && <QuickAddForm listKey={queryKeys.boardColumn(detail.project.key, query)} quickAdd={quickAdd} />}
          <ColumnBody result={result} items={items} state={state} />
        </div>
      </div>
    </section>
  );
}
