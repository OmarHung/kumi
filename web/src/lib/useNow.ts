import { useEffect, useState } from 'react';

const MINUTE_MS = 60_000;

/**
 * 現在的時間（毫秒），每分鐘更新一次。「3 分鐘前」這種相對時間用它：停在頁面上不動，說法也要跟著時間走；
 * 精確到分鐘就夠，不必每秒重畫。
 */
export function useNow(intervalMs: number = MINUTE_MS): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => {
      setNow(Date.now());
    }, intervalMs);
    return () => {
      clearInterval(timer);
    };
  }, [intervalMs]);
  return now;
}
