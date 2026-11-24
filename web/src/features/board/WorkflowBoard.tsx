import { DndContext, DragOverlay, type CollisionDetection } from '@dnd-kit/core';
import { useCallback, useMemo, useState } from 'react';
import type { Workflow } from '../../api/blueprint';
import type { Item } from '../../api/types';
import { stateLabel } from '../../domain/blueprint';
import { STATE_GROUP } from '../../domain/boardGroup';
import type { BoardFilters } from '../../domain/projectView';
import { workflowTypes } from '../../domain/workflow';
import { cx } from '../../lib/cx';
import { useIsCompact } from '../../lib/useMediaQuery';
import type { FocusTarget } from '../../lib/updateThenFocus';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { CardPreview } from './BoardCard';
import { BoardColumn } from './BoardColumn';
import { BoardProvider, type BoardContextValue } from './BoardContext';
import { useBoardDrag } from './useBoardDrag';
import {
  BOARD_ANNOUNCEMENTS,
  BOARD_INSTRUCTIONS,
  boardAutoScroll,
  boardDropAnimation,
  dropTarget,
  pointerWithinVisibleLanes,
  useBoardSensors,
} from './boardDnd';
import { MoveFill } from './MoveFill';
import { MoveProblems } from './MoveProblems';
import type { BoardMove, BoardMoves } from './useBoardMoves';
import styles from './Board.module.css';

const ACCESSIBILITY = { announcements: BOARD_ANNOUNCEMENTS, screenReaderInstructions: BOARD_INSTRUCTIONS };

/** 給螢幕閱讀器（與看得到的一行小字）：正在送出、或剛移好的是哪一張。 */
function useMoveStatus(pending: readonly BoardMove[], landed: Item | null): string {
  const { detail } = useProjectTable();
  const name = (item: Item, state: string) => stateLabel(detail.blueprint, item.type, state, item.stateCategory);
  const latest = pending.at(-1);
  if (latest !== undefined) {
    return `正在把 ${latest.item.key} 移到「${name(latest.item, latest.to)}」…`;
  }
  return landed === null ? '' : `已把 ${landed.key} 移到「${name(landed, landed.state)}」。`;
}

interface WorkflowBoardProps {
  readonly workflow: Workflow;
  readonly filters: BoardFilters;
  /** 改狀態的狀態放在 BoardView：這個元件換流程時會重建。 */
  readonly moves: BoardMoves;
  /** 關掉被擋下來的說明時，卡片與別的說明都不在畫面上，焦點的去處。 */
  readonly focusFallback: readonly FocusTarget[];
}

/** 一個流程的看板：一欄一個狀態，拖曳與「移到…」都經過 useBoardMoves。 */
export function WorkflowBoard({ workflow, filters, moves, focusFallback }: WorkflowBoardProps) {
  const { detail } = useProjectTable();
  const sensors = useBoardSensors();
  // 看板（欄的橫向捲動容器）：放下的判斷與自動捲動都要用，放在 state（掛上之後才有），不必在渲染時讀 ref。
  const [lanes, setLanes] = useState<HTMLDivElement | null>(null);
  const collisionDetection = useCallback<CollisionDetection>((args) => pointerWithinVisibleLanes(lanes, args), [lanes]);
  const { activeItem, ...dragHandlers } = useBoardDrag(moves.move, dropTarget);
  const [dropAnimation] = useState(boardDropAnimation);
  const autoScroll = boardAutoScroll(useIsCompact(), lanes);
  const types = useMemo(() => workflowTypes(detail.blueprint, workflow.key), [detail.blueprint, workflow.key]);
  const failedKeys = useMemo(() => new Set(moves.problems.map((problem) => problem.item.key)), [moves.problems]);
  const status = useMoveStatus(moves.pending, moves.landed);
  const value: BoardContextValue = {
    workflow,
    group: STATE_GROUP,
    types,
    filters,
    pending: moves.pending,
    failedKeys,
    landedKey: moves.landed?.key ?? null,
    activeItem,
    focusAfterMoveRef: moves.focusAfterMoveRef,
    move: moves.move,
  };
  return (
    <BoardProvider value={value}>
      <MoveProblems problems={moves.problems} onDismiss={moves.dismiss} focusFallback={focusFallback} />
      {/* 一次一個，換下一個時重新掛上（對話框的內容從新的那一張開始）。 */}
      {moves.fill !== null && <MoveFill key={moves.fill.item.key} request={moves.fill} moves={moves} focusFallback={focusFallback} />}
      <p role="status" className={styles.moveStatus}>
        {status}
      </p>
      <DndContext
        sensors={sensors}
        collisionDetection={collisionDetection}
        accessibility={ACCESSIBILITY}
        autoScroll={autoScroll}
        {...dragHandlers}
      >
        <div ref={setLanes} className={cx(styles.lanes, activeItem !== null && styles.lanesDragging)}>
          {workflow.states.map((state, index) => (
            <BoardColumn key={state.key} state={state} isFirst={index === 0} />
          ))}
        </div>
        <DragOverlay dropAnimation={dropAnimation}>{activeItem !== null && <CardPreview item={activeItem} />}</DragOverlay>
      </DndContext>
    </BoardProvider>
  );
}
