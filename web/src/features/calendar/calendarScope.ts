import { createContext, useContext } from 'react';
import type { CalendarDate } from '../../api/types';

/**
 * 行事曆畫面底下共用的兩件事：今天（過期的標示用）與里程碑連去哪個專案。
 * 專案頁有單一專案；「我的工作」跨專案，沒有週期與里程碑的標示（projectKey 是 null）。
 */
export interface CalendarScope {
  readonly todayDate: CalendarDate;
  readonly projectKey: string | null;
  /** 項目的名稱與提示多一句負責人（「我的工作」看「我建立的」時；負責人不一定是自己）。 */
  readonly isAssigneeShown: boolean;
}

export const CalendarScopeContext = createContext<CalendarScope | null>(null);

export function useCalendarScope(): CalendarScope {
  const scope = useContext(CalendarScopeContext);
  if (scope === null) throw new Error('行事曆的項目要放在 CalendarFrame 裡面。');
  return scope;
}
