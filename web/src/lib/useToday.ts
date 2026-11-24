import { useSyncExternalStore } from 'react';
import type { CalendarDate } from '../api/types';
import { today } from '../domain/dates';

/** 使用者回到這個分頁（視窗取得焦點、分頁回到前景）時重新看一次今天是幾號。 */
function subscribeToReturn(onChange: () => void): () => void {
  window.addEventListener('focus', onChange);
  document.addEventListener('visibilitychange', onChange);
  return () => {
    window.removeEventListener('focus', onChange);
    document.removeEventListener('visibilitychange', onChange);
  };
}

/**
 * 「今天」（使用者所在地的日期）。一直掛著的東西（側欄的過期數）用它：分頁開過夜，回到分頁時就換成新的一天，
 * 和重新進入我的工作時的分組一致。日期是字串，同一天的值相同，不會多渲染。
 */
export function useToday(): CalendarDate {
  return useSyncExternalStore(subscribeToReturn, today, today);
}
