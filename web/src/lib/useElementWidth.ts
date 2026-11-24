import { useLayoutEffect, useState, type RefObject } from 'react';

/**
 * 元素畫出來的寬度（px），大小變了就跟著變。量不到（沒有 ResizeObserver 的 jsdom、元素還沒排版時寬度是 0）是 null：
 * 呼叫端把 null 當成「放得下」，不要因為量不到就把東西收起來。
 */
export function useElementWidth(ref: RefObject<HTMLElement | null>): number | null {
  const [width, setWidth] = useState<number | null>(null);
  useLayoutEffect(() => {
    const element = ref.current;
    if (element === null || typeof window.ResizeObserver !== 'function') {
      return undefined;
    }
    const initial = element.getBoundingClientRect().width;
    setWidth(initial > 0 ? initial : null);
    const observer = new ResizeObserver((entries) => {
      const last = entries.at(-1);
      if (last !== undefined) setWidth(last.contentRect.width);
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, [ref]);
  return width;
}

/** 1rem 是幾 px（使用者放大文字時會變）。 */
export function remInPixels(): number {
  return Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}
