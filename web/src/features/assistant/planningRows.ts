import type { PlanningChange, PlanningValues } from '../../api/aiTypes';
import { formatDate } from '../../domain/dates';

const TARGET_LABELS = { cycle: '週期', milestone: '里程碑' } as const;
const ACTION_LABELS = { create: '新增', update: '修改', delete: '刪除' } as const;

/** 一筆的第一行：「新增 週期」「修改 里程碑」。 */
export function planningHeading(change: PlanningChange): string {
  return `${ACTION_LABELS[change.action]}${TARGET_LABELS[change.target]}`;
}

/** 週期是「名稱（開始 – 結束）」，里程碑是「名稱（到期 日期）」；沒有日期的只寫名稱。 */
function valuesText(target: PlanningChange['target'], values: PlanningValues): string {
  if (target === 'cycle') {
    if (values.startDate === null || values.endDate === null) return values.name;
    return `${values.name}（${formatDate(values.startDate)} – ${formatDate(values.endDate)}）`;
  }
  return values.dueDate === null ? values.name : `${values.name}（到期 ${formatDate(values.dueDate)}）`;
}

export interface PlanningLine {
  /** 修改時的「之前」；新增、刪除沒有。 */
  readonly before: string | null;
  readonly after: string;
  /** 刪除時補的一行：裡面的項目會變成沒有週期、里程碑。 */
  readonly note: string | null;
}

/** 一筆變更要給人看的內容；資料不合規格（該有的前後值是 null）時寫「（讀不到內容）」。 */
export function planningLine(change: PlanningChange): PlanningLine {
  const unknown = '（讀不到內容）';
  const { target, action, before, after } = change;
  if (action === 'create') return { before: null, after: after === null ? unknown : valuesText(target, after), note: null };
  if (action === 'update') {
    const descriptionChanged = target === 'milestone' && before !== null && after !== null && before.description !== after.description;
    return {
      before: before === null ? null : valuesText(target, before),
      after: `${after === null ? unknown : valuesText(target, after)}${descriptionChanged ? '，說明也會改' : ''}`,
      note: null,
    };
  }
  const count = change.itemCount ?? 0;
  const note = count === 0 ? null : `裡面的 ${String(count)} 個項目會變成沒有${TARGET_LABELS[target]}`;
  return { before: null, after: before === null ? unknown : valuesText(target, before), note };
}
