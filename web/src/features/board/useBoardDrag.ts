import type { Active, DragEndEvent, DragStartEvent, Over } from '@dnd-kit/core';
import { useState } from 'react';
import type { Item } from '../../api/types';
import { cardOf, type DropTarget } from './boardDnd';
import { useReportHandBusy } from '../live/handActivity';
import type { BoardContextValue } from './BoardContext';

/** 放下之後要改成哪一欄；不改（放回原欄、拖出看板）是 null。依狀態與依負責人、欄位分組各有一套。 */
export type ResolveDrop = (active: Pick<Active, 'data'>, over: Pick<Over, 'data'> | null) => DropTarget | null;

/** 拖曳中的卡片，以及放下之後要不要移動（resolve）。 */
export function useBoardDrag(move: BoardContextValue['move'], resolve: ResolveDrop) {
  const [activeItem, setActiveItem] = useState<Item | null>(null);
  // 即時更新的失效等拖曳結束（features/live）。
  useReportHandBusy(activeItem !== null);
  return {
    activeItem,
    onDragStart: ({ active }: DragStartEvent) => {
      setActiveItem(cardOf(active) ?? null);
    },
    onDragEnd: ({ active, over }: DragEndEvent) => {
      setActiveItem(null);
      const drop = resolve(active, over);
      if (drop !== null) {
        move(drop.item, drop.to, false);
      }
    },
    onDragCancel: () => {
      setActiveItem(null);
    },
  };
}
