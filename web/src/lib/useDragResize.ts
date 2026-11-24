import { useEffect, useLayoutEffect, useRef, useState, type PointerEvent, type RefObject } from 'react';
import { capturePointer, listenWhileDragging } from './dragListeners';
import { isRightToLeft } from './rovingGrid';

/*
 * 拖曳調整寬度的把手（docs/DESIGN.md 7.1「欄寬」、第 7 節 SidePanel）：按下時記住起點、寬度與原本拉過的寬度，
 * 移動時換畫面上的寬度，放開（或指標被瀏覽器收回：lostpointercapture）時才記住；只按一下沒有移動就什麼都不記。
 * 拖曳中按 Esc、被瀏覽器取消（pointercancel）或卸下，都回到開始拖之前的樣子、什麼都不記——拖到一半的寬度不是使用者決定的。
 * 表格的欄寬（components/Table/ColumnResizer.tsx）與項目面板的寬度（components/SidePanel）共用。
 */

/** 拖曳要調整的那一塊的操作（欄寬：ColumnResizer.tsx 的 useColumnResize；面板：SidePanelResizer.tsx）。 */
export interface DragTarget {
  readonly min: number;
  readonly max: number;
  /** 現在畫出來的寬度。 */
  readonly current: () => number;
  /** 現在拉過的寬度（沒拉過是 undefined）。 */
  readonly userWidth: () => number | undefined;
  /**
   * 換寬度。拖曳時多給開始的那一刻（start）：呼叫端可以照它決定怎麼記（欄寬：被夾住的欄往寬拉時不把記住的改小）。
   */
  readonly apply: (width: number, isFinal: boolean, start?: DragStart) => void;
  /** 放回原本拉過的寬度（undefined 是回到沒拉過），不記。 */
  readonly restore: (width: number | undefined) => void;
  /**
   * 把手在這一塊的哪一邊：end（預設，欄寬）在文字方向的結尾，往結尾拉是變寬；start（面板）在開頭那一邊，
   * 往開頭拉是變寬（從左到右時是往左）。
   */
  readonly edge?: 'start' | 'end';
  /**
   * 置中的那一塊（項目的浮動視窗）：兩邊一起動，指標移 1px 寬度變 2px，拖的那一邊才跟得上指標。
   */
  readonly isCentered?: boolean;
  /**
   * 量哪個方向的指標位移：x（預設，寬度）或 y（高度）。y 時 start 是上緣（往上拉是變高）、end 是下緣；
   * 不看文字方向。浮動的 AI 助理面板的上緣與左上角用（左上角是兩個方向各一份，同一個指標一起餵）。
   */
  readonly axis?: 'x' | 'y';
}

/** 拖曳開始的那一刻：畫出來的寬度與原本拉過的寬度。 */
export interface DragStart {
  readonly width: number;
  readonly userWidth: number | undefined;
}

interface DragState {
  readonly pointerId: number;
  /** 按下時指標在拖曳方向上的座標（x 或 y）。 */
  readonly startPointer: number;
  readonly startWidth: number;
  readonly startUserWidth: number | undefined;
  /**
   * 這一次的上限：一般是 target.max；開始時已經比它寬（我的工作沒拉過的標題欄吃掉剩下的寬度）就是開始的寬度——
   * 不然一碰就跳回上限、把手離開指標。
   */
  readonly max: number;
  /** 指標往右移一個像素時寬度的變化：把手在結尾那一邊、從左到右是 1；文字方向或把手的邊相反時是 -1。 */
  readonly direction: 1 | -1;
  /** 拖到的寬度；沒移動過（單純按一下、點兩下）就沒有，放開時不記。 */
  readonly width?: number;
}

function pointerOf(event: PointerEvent<HTMLElement>, target: DragTarget): number {
  return target.axis === 'y' ? event.clientY : event.clientX;
}

function startOf(event: PointerEvent<HTMLElement>, target: DragTarget): DragState {
  const isVertical = target.axis === 'y';
  const isReversed = isVertical ? target.edge === 'start' : isRightToLeft(event.currentTarget) !== (target.edge === 'start');
  const startWidth = target.current();
  return {
    pointerId: event.pointerId,
    startPointer: pointerOf(event, target),
    startWidth,
    startUserWidth: target.userWidth(),
    max: Math.max(target.max, startWidth),
    direction: isReversed ? -1 : 1,
  };
}

function widthAt(state: DragState, pointer: number, { min, isCentered = false }: DragTarget): number {
  const width = state.startWidth + (pointer - state.startPointer) * state.direction * (isCentered ? 2 : 1);
  return Math.round(Math.min(Math.max(width, min), state.max));
}

function startInfo(state: DragState): DragStart {
  return { width: state.startWidth, userWidth: state.startUserWidth };
}

/**
 * 拖曳中卸下（換頁、換檢視、關上面板）：回到開始拖之前的寬度，不留著聽 Esc、不留著擋選取。
 * 用最新的 target 放回（effect 的清除只看得到第一次畫的那一個）。
 */
function useRestoreOnUnmount(
  target: DragTarget,
  drag: RefObject<DragState | null>,
  stopListening: RefObject<(() => void) | null>,
): void {
  const latestTarget = useRef(target);
  useLayoutEffect(() => {
    latestTarget.current = target;
  });
  useEffect(
    () => () => {
      const state = drag.current;
      drag.current = null;
      stopListening.current?.();
      if (state !== null) latestTarget.current.restore(state.startUserWidth);
    },
    [drag, stopListening],
  );
}

export function useDragResize(target: DragTarget) {
  const drag = useRef<DragState | null>(null);
  const stopListening = useRef<(() => void) | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  useRestoreOnUnmount(target, drag, stopListening);
  const finish = (): DragState | null => {
    const state = drag.current;
    drag.current = null;
    stopListening.current?.();
    stopListening.current = null;
    setIsDragging(false);
    return state;
  };
  const end = (event: PointerEvent<HTMLElement>) => {
    if (drag.current?.pointerId !== event.pointerId) return;
    const state = finish();
    if (state?.width !== undefined) target.apply(state.width, true, startInfo(state));
  };
  const cancel = () => {
    const state = finish();
    if (state !== null) target.restore(state.startUserWidth);
  };
  // 瀏覽器取消（pointercancel：例如觸控被捲動或縮放接手）和 Esc 一樣。
  const interrupt = (event: PointerEvent<HTMLElement>) => {
    if (drag.current?.pointerId === event.pointerId) cancel();
  };
  return {
    isDragging,
    onPointerDown: (event: PointerEvent<HTMLElement>) => {
      if (event.button !== 0 || drag.current !== null) return;
      // 不移動焦點、不開始選取文字：拖曳中掃過的字不會反白，表格的停駐點也不換。
      event.preventDefault();
      capturePointer(event.currentTarget, event.pointerId);
      drag.current = startOf(event, target);
      stopListening.current = listenWhileDragging(cancel);
      setIsDragging(true);
    },
    onPointerMove: (event: PointerEvent<HTMLElement>) => {
      const state = drag.current;
      if (state?.pointerId !== event.pointerId) return;
      const width = widthAt(state, pointerOf(event, target), target);
      drag.current = { ...state, width };
      target.apply(width, false, startInfo(state));
    },
    onPointerUp: end,
    onPointerCancel: interrupt,
    // 正常放開之後也會收到（那時已經沒有拖曳，不做事）；沒放開就被收回時照放開處理。
    onLostPointerCapture: end,
  };
}
