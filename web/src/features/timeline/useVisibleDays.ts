import { useEffect, useRef, useState, type RefObject } from 'react';

export interface GridRefs {
  readonly scroller: RefObject<HTMLDivElement | null>;
  readonly labels: RefObject<HTMLDivElement | null>;
  readonly track: RefObject<HTMLDivElement | null>;
}

/** 看得到幾天：（捲動區的寬度 － 黏住的左欄）÷ 一天的寬度。任何一個量不到（測試環境都是 0）是 null。 */
function measure(refs: GridRefs, days: number): number | null {
  const scroller = refs.scroller.current;
  const labels = refs.labels.current;
  const track = refs.track.current;
  if (scroller === null || labels === null || track === null || days <= 0) return null;
  const dayWidth = track.offsetWidth / days;
  const visibleWidth = scroller.clientWidth - labels.offsetWidth;
  return dayWidth > 0 && visibleWidth > 0 ? visibleWidth / dayWidth : null;
}

/**
 * 畫面看得到幾天（量捲動區的寬度）：視窗放大縮小、項目面板開關都會變，所以跟著捲動區的大小重量。
 * 「往前」「往後」的步進靠它（stepDaysFor），才不會跳過沒看過的日子。
 */
export function useVisibleDays(days: number): GridRefs & { readonly visibleDays: number | null } {
  const scroller = useRef<HTMLDivElement | null>(null);
  const labels = useRef<HTMLDivElement | null>(null);
  const track = useRef<HTMLDivElement | null>(null);
  const [visibleDays, setVisibleDays] = useState<number | null>(null);
  useEffect(() => {
    const update = () => {
      setVisibleDays(measure({ scroller, labels, track }, days));
    };
    update();
    const element = scroller.current;
    if (element === null || typeof ResizeObserver === 'undefined') return undefined;
    const observer = new ResizeObserver(update);
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, [days]);
  return { scroller, labels, track, visibleDays };
}
