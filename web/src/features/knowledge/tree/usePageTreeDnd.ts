import {
  pointerWithin,
  TouchSensor,
  useSensor,
  useSensors,
  type Announcements,
  type DragEndEvent,
  type DragMoveEvent,
  type DragStartEvent,
  type ScreenReaderInstructions,
  type TouchSensorOptions,
  type PointerSensorOptions,
} from '@dnd-kit/core';
import { useState } from 'react';
import { CardPointerSensor } from '../../board/boardDnd';
import { dropZoneOf, planDrop, type DropZone, type MovePlan } from './treeDrop';
import type { TreeModel } from './treeModel';

/** 和看板同一組門檻（features/board/boardDnd.ts）：滑鼠移動 6px 才開始拖，手指按住 250ms 才開始拖（一碰就滑是捲動）。 */
const POINTER_OPTIONS: PointerSensorOptions = { activationConstraint: { distance: 6 } };
const TOUCH_OPTIONS: TouchSensorOptions = { activationConstraint: { delay: 250, tolerance: 8 } };

export interface DropHover {
  readonly targetNumber: number;
  readonly zone: DropZone;
  /** null：這裡不能放（不顯示落點）。 */
  readonly plan: MovePlan | null;
}

export const TREE_INSTRUCTIONS: ScreenReaderInstructions = {
  draggable: '用滑鼠拖曳頁面，或用手指按住頁面一下再拖，放在另一頁的上面、下面或中間就會搬過去。用鍵盤時，請用頁面的「更多」選單裡的「搬移…」。',
};

function pointerY(event: Event | null): number | null {
  if (event === null) return null;
  if ('clientY' in event && typeof event.clientY === 'number') return event.clientY;
  if ('touches' in event) {
    const touches = event.touches as ArrayLike<{ readonly clientY: number }>;
    return touches[0]?.clientY ?? null;
  }
  return null;
}

const ZONE_TEXT: Readonly<Record<DropZone, string>> = { before: '前面', inside: '裡面', after: '後面' };

/** 搬完的結果，給樹上的即時區域唸（拖曳放下、對話框以外沒有別的地方說）。 */
export function moveSummary(model: TreeModel, movedNumber: number, plan: MovePlan): string {
  const title = model.nodeOf(movedNumber)?.title ?? '頁面';
  const parent = plan.parentNumber === null ? undefined : model.nodeOf(plan.parentNumber);
  return `已把「${title}」搬到${parent === undefined ? '最上層' : `「${parent.title}」底下`}。`;
}

export function treeAnnouncements(model: TreeModel, hoverOf: () => DropHover | null): Announcements {
  const titleOf = (id: string | number) => `「${model.nodeOf(Number(id))?.title ?? '頁面'}」`;
  return {
    onDragStart: ({ active }) => `開始拖曳 ${titleOf(active.id)}。`,
    onDragOver: ({ active, over }) => {
      if (over === null) return `${titleOf(active.id)} 不在任何頁面上。`;
      const hover = hoverOf();
      if (hover === null || hover.plan === null) return `${titleOf(active.id)} 在 ${titleOf(over.id)} 上，這裡不能放。`;
      return `${titleOf(active.id)} 會放在 ${titleOf(over.id)} 的${ZONE_TEXT[hover.zone]}。`;
    },
    // 真的要搬時回空字串：結果由樹上的即時區域唸（moveSummary），不重複；放在不能放的位置要自己說，不然什麼都沒聽到。
    onDragEnd: ({ active, over }) => {
      if (over === null) return `${titleOf(active.id)} 放回原處，沒有搬移。`;
      return hoverOf()?.plan == null ? '這裡不能放，沒有搬移。' : '';
    },
    onDragCancel: ({ active }) => `取消拖曳，${titleOf(active.id)} 放回原處。`,
  };
}

interface PageTreeDnd {
  readonly sensors: ReturnType<typeof useSensors>;
  readonly announcements: Announcements;
  readonly activeNumber: number | null;
  readonly hover: DropHover | null;
  readonly handlers: {
    readonly onDragStart: (event: DragStartEvent) => void;
    readonly onDragMove: (event: DragMoveEvent) => void;
    readonly onDragEnd: (event: DragEndEvent) => void;
    readonly onDragCancel: () => void;
  };
  readonly collisionDetection: typeof pointerWithin;
}

/** 指標每動一下都會重算：值一樣就不更新，不然每次移動都重畫整棵樹。 */
function isSameHover(a: DropHover | null, b: DropHover | null): boolean {
  if (a === null || b === null) return a === b;
  const isSamePlan = a.plan?.parentNumber === b.plan?.parentNumber && a.plan?.beforeNumber === b.plan?.beforeNumber && (a.plan === null) === (b.plan === null);
  return a.targetNumber === b.targetNumber && a.zone === b.zone && isSamePlan;
}

/** 指標現在在哪一列的哪一區、放下去會搬到哪裡。 */
function hoverFrom(event: DragMoveEvent | DragEndEvent, model: TreeModel, expanded: ReadonlySet<number>): DropHover | null {
  const { over, active, delta } = event;
  const startY = pointerY(event.activatorEvent);
  if (over === null || startY === null) return null;
  const zone = dropZoneOf(startY + delta.y - over.rect.top, over.rect.height);
  const targetNumber = Number(over.id);
  return { targetNumber, zone, plan: planDrop(model, Number(active.id), targetNumber, zone, expanded) };
}

/**
 * 頁面樹的拖曳（只用 @dnd-kit/core）：每一列既是被拖的、也是放的地方；指標在一列的上、中、下四分之一各是前、子頁面、後。
 * 放下時重算一次落點（不靠 state，最後一次移動與放下之間不會漏掉）。
 */
export function usePageTreeDnd(model: TreeModel, expanded: ReadonlySet<number>, onMove: (pageNumber: number, plan: MovePlan) => void, announce: (text: string) => void): PageTreeDnd {
  const sensors = useSensors(useSensor(CardPointerSensor, POINTER_OPTIONS), useSensor(TouchSensor, TOUCH_OPTIONS));
  const [activeNumber, setActiveNumber] = useState<number | null>(null);
  const [hover, setHover] = useState<DropHover | null>(null);

  const reset = () => {
    setActiveNumber(null);
    setHover(null);
  };

  return {
    sensors,
    announcements: treeAnnouncements(model, () => hover),
    activeNumber,
    hover,
    collisionDetection: pointerWithin,
    handlers: {
      onDragStart: ({ active }) => {
        setActiveNumber(Number(active.id));
      },
      onDragMove: (event) => {
        const next = hoverFrom(event, model, expanded);
        if (!isSameHover(next, hover)) setHover(next);
      },
      onDragEnd: (event) => {
        const plan = hoverFrom(event, model, expanded)?.plan ?? null;
        reset();
        if (plan === null) return;
        onMove(Number(event.active.id), plan);
        announce(moveSummary(model, Number(event.active.id), plan));
      },
      onDragCancel: reset,
    },
  };
}
