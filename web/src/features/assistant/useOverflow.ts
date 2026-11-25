import { useLayoutEffect, useState, type RefObject } from 'react';

/**
 * 元素的內容是不是比它自己高（被 max-height 切掉、要捲動才看得完）。尺寸變了重量：Markdown 從純文字換成排好的版面時
 * 高度會變，所以裡面那一層也觀察。沒有 ResizeObserver（jsdom）時只量一次。
 */
export function useOverflow(ref: RefObject<HTMLElement | null>, content: string): boolean {
  const [isOverflowing, setIsOverflowing] = useState(false);
  useLayoutEffect(() => {
    const element = ref.current;
    if (element === null) return undefined;
    const update = () => {
      setIsOverflowing(element.scrollHeight > element.clientHeight + 1);
    };
    update();
    if (typeof window.ResizeObserver !== 'function') return undefined;
    const observer = new ResizeObserver(update);
    observer.observe(element);
    for (const child of element.children) observer.observe(child);
    return () => {
      observer.disconnect();
    };
  }, [ref, content]);
  return isOverflowing;
}
