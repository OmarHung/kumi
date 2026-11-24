import { useEffect, useRef, useState, type PointerEvent, type RefObject } from 'react';
import { capturePointer, listenWhileDragging } from '../../lib/dragListeners';
import {
  clampLauncherPosition,
  DEFAULT_LAUNCHER_POSITION,
  LAUNCHER_FALLBACK_SIZE,
  setLauncherPosition,
  type LauncherPosition,
} from './launcherPosition';

/** 指標移動超過這麼多像素才算拖曳；沒超過就是一般的點一下。 */
export const DRAG_THRESHOLD = 4;

interface DragState {
  readonly pointerId: number;
  readonly startX: number;
  readonly startY: number;
  readonly origin: LauncherPosition;
  readonly isDragging: boolean;
  /** Esc 或被瀏覽器取消之後：回到原位，放開之前不再跟著動，放開時也不當作點一下。 */
  readonly isCancelled: boolean;
}

type PointerHandler = (event: PointerEvent<HTMLElement>) => void;

function boundsNow(frame: HTMLElement | null) {
  const width = frame?.getBoundingClientRect().width ?? 0;
  return {
    viewport: { width: window.innerWidth, height: window.innerHeight },
    size: width > 0 ? width : LAUNCHER_FALLBACK_SIZE,
    // 測試環境（jsdom）沒有 matchMedia：當成滑鼠。
    isCoarse: typeof window.matchMedia === 'function' && window.matchMedia('(pointer: coarse)').matches,
  };
}

/** 按下那一刻按鈕離右邊、下邊多遠：量得到版面就量，量不到（測試）用記住的、再沒有用預設。 */
function originOf(frame: HTMLElement | null, stored: LauncherPosition | null): LauncherPosition {
  const rect = frame?.getBoundingClientRect();
  if (rect === undefined || rect.width <= 0) return stored ?? DEFAULT_LAUNCHER_POSITION;
  return { right: Math.round(window.innerWidth - rect.right), bottom: Math.round(window.innerHeight - rect.bottom) };
}

/** 指標在 (x, y) 時按鈕該在哪裡（夾在畫面裡）。 */
function positionAt(state: DragState, x: number, y: number, frame: HTMLElement | null): LauncherPosition {
  return clampLauncherPosition(
    { right: state.origin.right - (x - state.startX), bottom: state.origin.bottom - (y - state.startY) },
    boundsNow(frame),
  );
}

export interface LauncherDrag {
  /** 拖曳中的位置（還沒放開、還沒記住）；沒在拖曳是 null。 */
  readonly live: LauncherPosition | null;
  readonly isDragging: boolean;
  readonly handlers: {
    readonly onPointerDown: PointerHandler;
    readonly onPointerMove: PointerHandler;
    readonly onPointerUp: PointerHandler;
    readonly onPointerCancel: PointerHandler;
    readonly onLostPointerCapture: PointerHandler;
  };
  /** 拖曳之後放開會再送一個 click：要吞掉，不然一拖就打開面板。回傳這一下 click 該不該照常處理。 */
  readonly consumeClick: () => boolean;
}

interface DragControl {
  readonly stateRef: RefObject<DragState | null>;
  readonly stopListeningRef: RefObject<(() => void) | null>;
  readonly clickSuppressedRef: RefObject<boolean>;
  readonly live: LauncherPosition | null;
  readonly setLive: (position: LauncherPosition | null) => void;
  readonly finish: () => void;
  readonly cancel: () => void;
  readonly consumeClick: () => boolean;
  readonly expireSuppression: () => void;
}

/** 拖曳的狀態與收尾：結束（放開、被取消）、Esc 取消（回到原位但指標還按著）。 */
function useDragControl(): DragControl {
  const stateRef = useRef<DragState | null>(null);
  const stopListeningRef = useRef<(() => void) | null>(null);
  const clickSuppressedRef = useRef(false);
  const [live, setLive] = useState<LauncherPosition | null>(null);
  const stop = () => {
    stopListeningRef.current?.();
    stopListeningRef.current = null;
  };
  useEffect(() => stop, []);
  return {
    stateRef,
    stopListeningRef,
    clickSuppressedRef,
    live,
    setLive,
    finish: () => {
      stop();
      stateRef.current = null;
      setLive(null);
    },
    consumeClick: () => {
      const wasSuppressed = clickSuppressedRef.current;
      clickSuppressedRef.current = false;
      return !wasSuppressed;
    },
    // click 通常緊接著送到；萬一沒有（指標在別處放開），不要讓旗標留到下一次真正的點擊。
    expireSuppression: () => {
      if (!clickSuppressedRef.current) return;
      setTimeout(() => {
        clickSuppressedRef.current = false;
      }, 0);
    },
    // Esc：回到開始的位置；指標還按著，放開時那個 click 一樣吞掉。
    cancel: () => {
      const current = stateRef.current;
      if (current === null) return;
      stop();
      if (current.isDragging) clickSuppressedRef.current = true;
      stateRef.current = { ...current, isCancelled: true };
      setLive(null);
    },
  };
}

/**
 * 拖動 AI 助理按鈕換位置。用 pointer capture（滑出按鈕也跟著）；移動超過門檻才算拖曳；
 * 放開才記住，Esc 或 pointercancel 回到開始的位置、不記。frame 是整組（按鈕加「×」）。
 */
export function useLauncherDrag(frameRef: RefObject<HTMLElement | null>, stored: LauncherPosition | null): LauncherDrag {
  const { stateRef, stopListeningRef, clickSuppressedRef, live, setLive, finish, cancel, consumeClick, expireSuppression } =
    useDragControl();

  const end: PointerHandler = (event) => {
    const current = stateRef.current;
    if (current?.pointerId !== event.pointerId) return;
    if (current.isDragging && !current.isCancelled) {
      clickSuppressedRef.current = true;
      setLauncherPosition(positionAt(current, event.clientX, event.clientY, frameRef.current));
    }
    finish();
    expireSuppression();
  };
  const down: PointerHandler = (event) => {
    if (event.button !== 0 || stateRef.current !== null) return;
    capturePointer(event.currentTarget, event.pointerId);
    clickSuppressedRef.current = false;
    stopListeningRef.current = listenWhileDragging(cancel);
    stateRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      origin: originOf(frameRef.current, stored),
      isDragging: false,
      isCancelled: false,
    };
  };
  const move: PointerHandler = (event) => {
    const current = stateRef.current;
    if (current?.pointerId !== event.pointerId || current.isCancelled) return;
    const isPastThreshold = Math.hypot(event.clientX - current.startX, event.clientY - current.startY) > DRAG_THRESHOLD;
    if (!current.isDragging && !isPastThreshold) return;
    stateRef.current = { ...current, isDragging: true };
    setLive(positionAt(current, event.clientX, event.clientY, frameRef.current));
  };
  // 被瀏覽器取消（例如觸控被捲動接手）：不會再有 pointerup，直接收尾。
  const interrupt: PointerHandler = (event) => {
    const current = stateRef.current;
    if (current?.pointerId !== event.pointerId) return;
    if (current.isDragging) clickSuppressedRef.current = true;
    finish();
  };

  return {
    live,
    isDragging: live !== null,
    handlers: { onPointerDown: down, onPointerMove: move, onPointerUp: end, onPointerCancel: interrupt, onLostPointerCapture: end },
    consumeClick,
  };
}
