import type { Cycle, CycleDraft, Milestone, MilestoneDraft } from '../../api/planning';
import type { CalendarDate } from '../../api/types';
import { addDays } from '../../domain/dates';

export const CYCLE_NAME_MAX = 50;
export const MILESTONE_NAME_MAX = 100;
export const MILESTONE_DESCRIPTION_MAX = 2000;
/** 最長 365 天，頭尾都算（和後端一致）：結束日減開始日最多 364。 */
const CYCLE_MAX_DAYS = 365;
/** 預設的週期長度：兩週，結束日是開始日加 13 天（含頭尾 14 天）。 */
const DEFAULT_CYCLE_SPAN_DAYS = 13;
const MS_PER_DAY = 86_400_000;
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

export type CycleStatus = 'active' | 'upcoming' | 'ended';

/** 「今天」由呼叫端給（使用者所在地的日期）：伺服器不知道使用者的時區。字串順序就是日期順序。 */
export function cycleStatusOf(cycle: Pick<Cycle, 'startDate' | 'endDate'>, today: CalendarDate): CycleStatus {
  if (today < cycle.startDate) return 'upcoming';
  return today > cycle.endDate ? 'ended' : 'active';
}

/** 名稱從「衝刺 (數量+1)」開始往上找第一個沒被用過的：刪過中間的一個之後，數量加一可能已經有了。比對不分大小寫（唯一索引也不分）。 */
function unusedCycleName(cycles: readonly Cycle[]): string {
  const taken = new Set(cycles.map((cycle) => cycle.name.trim().toLowerCase()));
  const nameOf = (number: number) => `衝刺 ${String(number)}`;
  let number = cycles.length + 1;
  while (taken.has(nameOf(number).toLowerCase())) number += 1;
  return nameOf(number);
}

/** 新增週期的預設值：接在最後一個週期結束的隔天（沒有就從今天），名稱是「衝刺 N」。 */
export function defaultCycleDraft(cycles: readonly Cycle[], today: CalendarDate): CycleDraft {
  // 不靠清單的順序：伺服器照開始日排，但週期不重疊，結束日最晚的就是最後一個，這裡自己取才不依賴順序。
  const lastEnd = cycles.reduce<CalendarDate | null>((latest, cycle) => (latest === null || cycle.endDate > latest ? cycle.endDate : latest), null);
  const startDate = lastEnd === null ? today : addDays(lastEnd, 1);
  return { name: unusedCycleName(cycles), startDate, endDate: addDays(startDate, DEFAULT_CYCLE_SPAN_DAYS) };
}

/** 過了到期日、而且還有項目沒完成也沒取消。沒有項目時沒有東西過期。 */
export function isMilestoneOverdue(milestone: Pick<Milestone, 'dueDate' | 'progress'>, today: CalendarDate): boolean {
  const { total, done, canceled } = milestone.progress;
  return milestone.dueDate < today && done + canceled < total;
}

export type CycleErrors = Partial<Record<'name' | 'startDate' | 'endDate', string>>;
export type MilestoneErrors = Partial<Record<'name' | 'dueDate' | 'description', string>>;

function isDate(value: string): boolean {
  return DATE_PATTERN.test(value);
}

function daysBetween(start: CalendarDate, end: CalendarDate): number {
  return Math.round((Date.parse(`${end}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`)) / MS_PER_DAY);
}

/** 送出前先擋掉伺服器一定會退回的（名稱、日期、長度）；重疊與重名要問伺服器才知道。 */
export function validateCycle(draft: CycleDraft): CycleErrors {
  const name = draft.name.trim();
  const errors: Record<string, string> = {};
  if (name === '') errors.name = '請輸入週期的名稱。';
  else if (Array.from(name).length > CYCLE_NAME_MAX) errors.name = `名稱最多 ${String(CYCLE_NAME_MAX)} 個字。`;
  if (!isDate(draft.startDate)) errors.startDate = '請選開始日。';
  if (!isDate(draft.endDate)) errors.endDate = '請選結束日。';
  if (errors.startDate === undefined && errors.endDate === undefined) {
    const days = daysBetween(draft.startDate, draft.endDate);
    if (days < 0) errors.endDate = '結束日不能早於開始日。';
    else if (days + 1 > CYCLE_MAX_DAYS) errors.endDate = `一個週期最長 ${String(CYCLE_MAX_DAYS)} 天。`;
  }
  return errors;
}

export function validateMilestone(draft: MilestoneDraft): MilestoneErrors {
  const name = draft.name.trim();
  const errors: Record<string, string> = {};
  if (name === '') errors.name = '請輸入里程碑的名稱。';
  else if (Array.from(name).length > MILESTONE_NAME_MAX) errors.name = `名稱最多 ${String(MILESTONE_NAME_MAX)} 個字。`;
  if (!isDate(draft.dueDate)) errors.dueDate = '請選到期日。';
  if (draft.description !== null && Array.from(draft.description).length > MILESTONE_DESCRIPTION_MAX) {
    errors.description = '說明最多 2,000 個字。';
  }
  return errors;
}
