import { useEffect, useRef } from 'react';

/**
 * 點在這個標籤上等於點它指到的控制項（<label for>）：控制項在元件裡面的話，標籤也不算外面。
 * 否則清單開著時點欄位的標籤會先「點在外面」關上，接著標籤的點擊按到按鈕又打開。
 */
function isLabelOfInside(target: Node, container: HTMLElement): boolean {
  const label = target instanceof Element ? target.closest('label') : null;
  const control = label?.control ?? null;
  return control !== null && container.contains(control);
}

/**
 * 點在元件外面就關上，和一般的下拉選單一樣。close 要是固定的函式（useCallback），
 * effect 才不會每次渲染都重新掛監聽。
 */
export function useCloseOnOutsidePointer<T extends HTMLElement>(isOpen: boolean, close: () => void) {
  const containerRef = useRef<T>(null);
  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }
    function handlePointerDown(event: PointerEvent) {
      const container = containerRef.current;
      if (!(event.target instanceof Node) || container === null) return;
      if (!container.contains(event.target) && !isLabelOfInside(event.target, container)) {
        close();
      }
    }
    document.addEventListener('pointerdown', handlePointerDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
    };
  }, [isOpen, close]);
  return containerRef;
}
