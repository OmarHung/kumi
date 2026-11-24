import { useRef, useState, type KeyboardEvent, type PointerEvent, type RefObject } from 'react';
import { resizeKeyTarget } from '../../lib/resizeKeys';
import { useDragResize, type DragTarget } from '../../lib/useDragResize';
import { clampPanelSize, DEFAULT_PANEL_SIZE, MIN_PANEL_SIZE, readStoredPanelSize, storePanelSize, type PanelSize } from './panelSize';

/*
 * 浮動面板釘在右下角，三個把手往左上拉都是變大：左緣調寬、上緣調高、左上角兩個一起。
 * 指標拖曳用 lib/useDragResize.ts（抓住指標、按下不搬焦點、Esc 或被取消回到開始之前、放開才記住、只按一下不記）：
 * 寬與高各用一份，左上角把同一個指標餵給兩份；鍵盤用 lib/resizeKeys.ts。
 */

type Dimension = 'width' | 'height';

interface PointerHandlers {
  readonly onPointerDown: (event: PointerEvent<HTMLElement>) => void;
  readonly onPointerMove: (event: PointerEvent<HTMLElement>) => void;
  readonly onPointerUp: (event: PointerEvent<HTMLElement>) => void;
  readonly onPointerCancel: (event: PointerEvent<HTMLElement>) => void;
  readonly onLostPointerCapture: (event: PointerEvent<HTMLElement>) => void;
}

/** 單一方向（寬或高）的把手：指標、鍵盤、點兩下，與給 ARIA 用的數值。 */
export interface EdgeResize {
  readonly isDragging: boolean;
  readonly value: number;
  readonly min: number;
  readonly max: number;
  /** 只有指標的那一份：左上角把兩個方向的合起來用。 */
  readonly pointer: PointerHandlers;
  readonly handlers: PointerHandlers & {
    readonly onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
    readonly onDoubleClick: () => void;
  };
}

/** 左上角：只給指標（兩個方向一起），沒有鍵盤。 */
export interface CornerResize {
  readonly isDragging: boolean;
  readonly handlers: PointerHandlers & { readonly onDoubleClick: () => void };
}

export interface PanelResize {
  /** 記住的（或預設的）大小；畫面上實際的大小可能被 CSS 夾在畫面之內。 */
  readonly size: PanelSize;
  readonly width: EdgeResize;
  readonly height: EdgeResize;
  readonly corner: CornerResize;
}

function viewportNow() {
  return { width: window.innerWidth, height: window.innerHeight };
}

function storedOrDefault(): PanelSize {
  return readStoredPanelSize() ?? DEFAULT_PANEL_SIZE;
}

/** 範圍：最小到「畫面減邊距」。 */
function boundsOf(dimension: Dimension): { readonly min: number; readonly max: number } {
  const max = clampPanelSize({ width: Number.MAX_SAFE_INTEGER, height: Number.MAX_SAFE_INTEGER }, viewportNow());
  return { min: MIN_PANEL_SIZE[dimension], max: max[dimension] };
}

/** 面板現在畫出來的大小（CSS 把面板夾在畫面之內時，記住的值不等於看到的）；量不到（沒有版面）用記住的。 */
function renderedOf(panel: HTMLElement | null, dimension: Dimension, fallback: number): number {
  const rect = panel?.getBoundingClientRect();
  if (rect === undefined || rect.width <= 0) return fallback;
  return Math.round(dimension === 'width' ? rect.width : rect.height);
}

interface SizeStore {
  readonly size: PanelSize;
  readonly latest: RefObject<PanelSize>;
  /** 換這個方向的值（夾回範圍內）；isFinal 時記住（和預設一樣就拿掉記錄）。左上角兩個方向同一刻放開，所以從 ref 讀最新的。 */
  readonly set: (dimension: Dimension, value: number, isFinal: boolean) => void;
  readonly reset: (dimensions: readonly Dimension[]) => void;
}

function useSizeStore(): SizeStore {
  const [size, setSize] = useState<PanelSize>(storedOrDefault);
  const latest = useRef(size);
  const commit = (next: PanelSize, isFinal: boolean) => {
    latest.current = next;
    setSize(next);
    if (!isFinal) return;
    const isDefault = next.width === DEFAULT_PANEL_SIZE.width && next.height === DEFAULT_PANEL_SIZE.height;
    storePanelSize(isDefault ? null : next);
  };
  return {
    size,
    latest,
    set: (dimension, value, isFinal) => {
      const clamped = clampPanelSize({ ...latest.current, [dimension]: value }, viewportNow());
      commit({ ...latest.current, [dimension]: clamped[dimension] }, isFinal);
    },
    reset: (dimensions) => {
      const next = dimensions.reduce<PanelSize>((size_, key) => ({ ...size_, [key]: DEFAULT_PANEL_SIZE[key] }), latest.current);
      commit(next, true);
    },
  };
}

function useEdge(dimension: Dimension, panelRef: RefObject<HTMLElement | null>, store: SizeStore): EdgeResize {
  const { min, max } = boundsOf(dimension);
  const current = () => renderedOf(panelRef.current, dimension, store.latest.current[dimension]);
  const target: DragTarget = {
    min,
    max,
    // 把手在面板的開頭那一邊（左緣、上緣）：往外拉是變大。
    edge: 'start',
    axis: dimension === 'width' ? 'x' : 'y',
    current,
    userWidth: () => store.latest.current[dimension],
    apply: (value, isFinal) => {
      store.set(dimension, value, isFinal);
    },
    // 回到開始之前記住的（沒有就是預設）；不記。
    restore: (value) => {
      store.set(dimension, value ?? DEFAULT_PANEL_SIZE[dimension], false);
    },
  };
  const { isDragging, ...pointer } = useDragResize(target);
  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    const next = resizeKeyTarget(event, { width: current(), min, max, edge: 'start', axis: target.axis });
    if (next === null) return;
    event.preventDefault();
    if (next === 'reset') store.reset([dimension]);
    else store.set(dimension, next, true);
  };
  return {
    isDragging,
    value: store.size[dimension],
    min,
    max,
    pointer,
    handlers: {
      ...pointer,
      onKeyDown,
      onDoubleClick: () => {
        store.reset([dimension]);
      },
    },
  };
}

/** 把同一個指標事件交給兩個方向的拖曳。 */
function bothPointers(a: PointerHandlers, b: PointerHandlers): PointerHandlers {
  return {
    onPointerDown: (event) => {
      a.onPointerDown(event);
      b.onPointerDown(event);
    },
    onPointerMove: (event) => {
      a.onPointerMove(event);
      b.onPointerMove(event);
    },
    onPointerUp: (event) => {
      a.onPointerUp(event);
      b.onPointerUp(event);
    },
    onPointerCancel: (event) => {
      a.onPointerCancel(event);
      b.onPointerCancel(event);
    },
    onLostPointerCapture: (event) => {
      a.onLostPointerCapture(event);
      b.onLostPointerCapture(event);
    },
  };
}

/** 面板的大小與三個把手。panelRef 是面板本身：按下把手時量畫面上實際的大小當起點。 */
export function usePanelResize(panelRef: RefObject<HTMLElement | null>): PanelResize {
  const store = useSizeStore();
  const width = useEdge('width', panelRef, store);
  const height = useEdge('height', panelRef, store);
  return {
    size: store.size,
    width,
    height,
    corner: {
      isDragging: width.isDragging || height.isDragging,
      handlers: {
        ...bothPointers(width.pointer, height.pointer),
        onDoubleClick: () => {
          store.reset(['width', 'height']);
        },
      },
    },
  };
}
