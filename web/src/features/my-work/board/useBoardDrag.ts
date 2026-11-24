import type { DragEndEvent, DragStartEvent } from '@dnd-kit/core';
import { useState } from 'react';
import type { Item } from '../../../api/types';
import { useReportHandBusy } from '../../live/handActivity';
import { cardOf } from '../../board/boardDnd';
import { categoryDrop, type CategoryDrop } from './myWorkBoardDnd';

/** 拖曳中的卡片，以及放下之後要處理的（放在別的類別的欄上）。 */
export function useBoardDrag(onDrop: (drop: CategoryDrop) => void, onStart: () => void) {
  const [activeItem, setActiveItem] = useState<Item | null>(null);
  // 即時更新的失效等拖曳結束（features/live）。
  useReportHandBusy(activeItem !== null);
  return {
    activeItem,
    onDragStart: ({ active }: DragStartEvent) => {
      onStart();
      setActiveItem(cardOf(active) ?? null);
    },
    onDragEnd: ({ active, over }: DragEndEvent) => {
      setActiveItem(null);
      const drop = categoryDrop(active, over);
      if (drop !== null) onDrop(drop);
    },
    onDragCancel: () => {
      setActiveItem(null);
    },
  };
}
