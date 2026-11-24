import { useEffect, useRef, useState, type Dispatch, type MouseEvent, type PointerEvent, type RefObject, type SetStateAction } from 'react';
import { capturePointer, listenWhileDragging } from '../../lib/dragListeners';
import { isRightToLeft } from '../../lib/rovingGrid';

/*
 * 拖表頭換欄的順序（docs/DESIGN.md 7.1「欄的順序」）：按住表頭的欄名（排序按鈕）往左右拖，放開就換。
 * - 按下就抓住指標（setPointerCapture）：欄名的按鈕很窄，快速一甩時第一個移動事件已經在按鈕外面，等移動夠了才抓會收不到；
 *   放開在按鈕外面也收得到，不會留下一個沒結束的按下。抓住之後瀏覽器照樣把點擊送給這顆按鈕（排序）。
 * - 移動超過 DRAG_THRESHOLD 才算拖曳；沒有移動就放開是一般的點擊（排序）。拖過之後放開的那一下點擊不算（不排序）。
 * - 拖曳中按 Esc、被瀏覽器取消（pointercancel）都是沒換；之後放開的那一下點擊同樣不算。
 * - 觸控不拖：橫向滑表頭是捲動表格。觸控、鍵盤與不方便拖曳的人用表格外面的另一條路（我的工作的「欄的順序」）。
 * 只管指標與交界；放到哪裡可以、放了之後的順序由呼叫端決定（resolve、onDrop）。
 */

/** 移動多少才算開始拖（px）：手抖一下的點擊照樣是排序。 */
const DRAG_THRESHOLD = 5;

export interface ColumnDragState<Id extends string = string> {
  /** 拖著的欄。 */
  readonly id: Id;
  /** 放開會放到看得到的欄的第幾個交界（0 是最前面）；放了不會變是 null。 */
  readonly slot: number | null;
}

export interface ColumnDragOptions<Id extends string> {
  /** 拖到第 slot 個交界時實際會放的交界（放不下的地方換成最近放得下的）；沒有變化是 null。 */
  readonly resolve: (id: Id, slot: number) => number | null;
  /** 放開：slot 是 resolve 給的。 */
  readonly onDrop: (id: Id, slot: number) => void;
}

export interface ColumnDragHandlers {
  readonly onPointerDown: (event: PointerEvent<HTMLElement>) => void;
  readonly onPointerMove: (event: PointerEvent<HTMLElement>) => void;
  readonly onPointerUp: (event: PointerEvent<HTMLElement>) => void;
  readonly onPointerCancel: (event: PointerEvent<HTMLElement>) => void;
  readonly onLostPointerCapture: (event: PointerEvent<HTMLElement>) => void;
  readonly onClickCapture: (event: MouseEvent<HTMLElement>) => void;
}

interface PointerState<Id extends string> {
  readonly id: Id;
  readonly pointerId: number;
  readonly startX: number;
  /** 量交界用的表頭那一列。 */
  readonly row: HTMLTableRowElement | null;
  /** 按下還沒移動夠（pending）、拖曳中、拖曳中按了 Esc（canceled，等放開）。 */
  readonly phase: 'pending' | 'dragging' | 'canceled';
  readonly slot: number | null;
}

/** 指標在表頭那一列的第幾個交界：數有幾格的中線在指標的開頭那一邊（從左到右是左邊）。 */
function slotAt(row: HTMLTableRowElement, clientX: number): number {
  const isRtl = isRightToLeft(row);
  return [...row.cells].filter((cell) => {
    const { left, width } = cell.getBoundingClientRect();
    const middle = left + width / 2;
    return isRtl ? middle > clientX : middle < clientX;
  }).length;
}

/** 一次拖曳要用到的：指標的狀態、畫面的狀態，與開始、結束的動作（useColumnDrag 裡建立）。 */
interface DragSession<Id extends string> {
  readonly pointer: RefObject<PointerState<Id> | null>;
  readonly isClickSuppressed: RefObject<boolean>;
  readonly setState: Dispatch<SetStateAction<ColumnDragState<Id> | null>>;
  /** 開始拖：聽 Esc（取消）、擋下文字的選取。 */
  readonly listen: () => void;
  /** 結束：不再聽、不再畫。 */
  readonly stop: () => void;
  /** 拖過（或拖曳中取消）之後放開的那一下點擊不是排序：擋下一次；沒有點擊跟著來時，這一輪事件結束就不擋了。 */
  readonly suppressClick: () => void;
}

function moveHandler<Id extends string>(session: DragSession<Id>, resolve: ColumnDragOptions<Id>['resolve']) {
  return (event: PointerEvent<HTMLElement>) => {
    const current = session.pointer.current;
    if (current?.pointerId !== event.pointerId || current.phase === 'canceled') return;
    if (current.phase === 'pending') {
      if (Math.abs(event.clientX - current.startX) < DRAG_THRESHOLD) return;
      session.listen();
    }
    const slot = current.row === null ? null : resolve(current.id, slotAt(current.row, event.clientX));
    session.pointer.current = { ...current, phase: 'dragging', slot };
    // 交界沒換就不重畫：拖曳中每移一下都換的話，表頭跟著重畫太多次。
    session.setState((previous) => (previous?.id === current.id && previous.slot === slot ? previous : { id: current.id, slot }));
  };
}

/**
 * 放開在按鈕外面：一般的按鈕這樣是不算點擊（按下之後滑開就是反悔）；抓住指標之後瀏覽器照樣送點擊，所以自己擋。
 * 沒有版面（寬度 0，測試環境）時不判斷。
 */
function isReleasedOutside(event: PointerEvent<HTMLElement>): boolean {
  const { left, right, top, bottom, width } = event.currentTarget.getBoundingClientRect();
  if (width === 0) return false;
  return event.clientX < left || event.clientX > right || event.clientY < top || event.clientY > bottom;
}

/** 指標放開、被取消或被收回：這一次拖曳結束。 */
function endHandlers<Id extends string>(session: DragSession<Id>, onDrop: ColumnDragOptions<Id>['onDrop']) {
  const { pointer, stop } = session;
  return {
    onPointerUp: (event: PointerEvent<HTMLElement>) => {
      const current = pointer.current;
      if (current?.pointerId !== event.pointerId) return;
      pointer.current = null;
      if (current.phase !== 'pending' || isReleasedOutside(event)) session.suppressClick();
      if (current.phase === 'dragging' && current.slot !== null) onDrop(current.id, current.slot);
      stop();
    },
    onPointerCancel: (event: PointerEvent<HTMLElement>) => {
      if (pointer.current?.pointerId !== event.pointerId) return;
      pointer.current = null;
      stop();
    },
    // 正常放開之後也會收到（那時已經結束）；沒放開就被收回是沒換（還沒開始拖的按下也一起結束）。
    onLostPointerCapture: (event: PointerEvent<HTMLElement>) => {
      if (pointer.current?.pointerId !== event.pointerId) return;
      pointer.current = null;
      stop();
    },
  };
}

function handlersOf<Id extends string>(id: Id, session: DragSession<Id>, { resolve, onDrop }: ColumnDragOptions<Id>): ColumnDragHandlers {
  return {
    onPointerDown: (event) => {
      // 拖曳中的另一個指標不理；還沒開始拖的按下（放開的事件沒收到）由新的按下取代，不會卡住之後的拖曳。
      if (event.button !== 0 || event.pointerType === 'touch' || session.pointer.current?.phase === 'dragging') return;
      capturePointer(event.currentTarget, event.pointerId);
      const row = event.currentTarget.closest('tr');
      session.pointer.current = { id, pointerId: event.pointerId, startX: event.clientX, row, phase: 'pending', slot: null };
    },
    onPointerMove: moveHandler(session, resolve),
    ...endHandlers(session, onDrop),
    onClickCapture: (event) => {
      if (!session.isClickSuppressed.current) return;
      session.isClickSuppressed.current = false;
      event.preventDefault();
      event.stopPropagation();
    },
  };
}

/** 拖表頭換欄的順序：state 是拖曳中的欄與放下去的交界（畫出來用），handlersFor 掛在每一欄的欄名（排序按鈕）上。 */
export function useColumnDrag<Id extends string>(options: ColumnDragOptions<Id>) {
  const pointer = useRef<PointerState<Id> | null>(null);
  const stopListening = useRef<(() => void) | null>(null);
  const isClickSuppressed = useRef(false);
  const [state, setState] = useState<ColumnDragState<Id> | null>(null);

  // 拖曳中卸下（換版面、換頁）：不留著聽 Esc、不留著擋選取。
  useEffect(
    () => () => {
      stopListening.current?.();
    },
    [],
  );

  const stop = () => {
    stopListening.current?.();
    stopListening.current = null;
    setState(null);
  };
  const session: DragSession<Id> = {
    pointer,
    isClickSuppressed,
    setState,
    listen: () => {
      // Esc：取消，等放開（放開的那一下點擊照樣不算）。
      stopListening.current = listenWhileDragging(() => {
        const current = pointer.current;
        if (current?.phase === 'dragging') pointer.current = { ...current, phase: 'canceled', slot: null };
        stop();
      });
    },
    stop,
    suppressClick: () => {
      isClickSuppressed.current = true;
      setTimeout(() => {
        isClickSuppressed.current = false;
      }, 0);
    },
  };

  return { state, handlersFor: (id: Id) => handlersOf(id, session, options) };
}
