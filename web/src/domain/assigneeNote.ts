import type { Item } from '../api/types';

/**
 * 給名稱與提示用的負責人一句（「負責人 範例夥伴」「沒有負責人」）。
 * 只有「我建立的」會用：看「我負責的」時每一個都是自己，不必寫。
 */
export function assigneeNote(assignee: Item['assignee']): string {
  return assignee === null ? '沒有負責人' : `負責人 ${assignee.displayName}`;
}
