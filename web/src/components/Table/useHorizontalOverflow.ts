import { useLayoutEffect, useState, type RefObject } from 'react';

/**
 * 表格（容器的第一個子元素）比容器裡面的寬度寬嗎？比的是表格自己的寬度（getBoundingClientRect），
 * 不是容器的 scrollWidth：容器在 overflow-x: clip 時不是捲動容器，scrollWidth 算不算進被裁掉的部分要看瀏覽器。
 */
function overflows(container: HTMLElement): boolean {
  const content = container.firstElementChild;
  return content !== null && content.getBoundingClientRect().width > container.clientWidth;
}

/**
 * 容器裡的表格是不是比容器寬。容器與表格變大小時重新量。
 * 沒有 ResizeObserver 的環境（jsdom）當成放得下。isEnabled 是 false 時不量，一律回 false。
 */
export function useHorizontalOverflow(ref: RefObject<HTMLElement | null>, isEnabled: boolean): boolean {
  const [isOverflowing, setIsOverflowing] = useState(false);
  useLayoutEffect(() => {
    const element = ref.current;
    if (!isEnabled || element === null || typeof window.ResizeObserver !== 'function') {
      return undefined;
    }
    const update = () => {
      setIsOverflowing(overflows(element));
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(element);
    if (element.firstElementChild !== null) {
      observer.observe(element.firstElementChild);
    }
    return () => {
      observer.disconnect();
    };
  }, [ref, isEnabled]);
  return isEnabled && isOverflowing;
}
