import { useRef, type PointerEvent } from 'react';
import { capturePointer } from '../../lib/dragListeners';

/** 指標在元素裡的位置，左上是 (0, 0)、右下是 (1, 1)，拖到外面時夾在邊上。 */
export interface PointerRatio {
  readonly x: number;
  readonly y: number;
}

function along(offset: number, size: number): number {
  return size > 0 ? Math.min(1, Math.max(0, offset / size)) : 0;
}

function ratioIn(event: PointerEvent<HTMLElement>): PointerRatio {
  const rect = event.currentTarget.getBoundingClientRect();
  return { x: along(event.clientX - rect.left, rect.width), y: along(event.clientY - rect.top, rect.height) };
}

/**
 * 取色器的方塊與色相的軌道共用：按下就選到那一點，按著拖曳一路跟著，放開或被取消就停。
 * 按下時就抓住指標（拖出範圍照樣收得到，不必慢慢拖）；不擋 pointerdown 的預設行為，按下時焦點照樣移到滑桿上，接著用方向鍵微調。
 */
export function usePointerRatio(onRatio: (ratio: PointerRatio) => void) {
  const isDragging = useRef(false);
  const stop = () => {
    isDragging.current = false;
  };
  return {
    onPointerDown: (event: PointerEvent<HTMLElement>) => {
      if (event.button !== 0) return;
      isDragging.current = true;
      capturePointer(event.currentTarget, event.pointerId);
      onRatio(ratioIn(event));
    },
    onPointerMove: (event: PointerEvent<HTMLElement>) => {
      if (isDragging.current) onRatio(ratioIn(event));
    },
    onPointerUp: stop,
    onPointerCancel: stop,
    onLostPointerCapture: stop,
  };
}
