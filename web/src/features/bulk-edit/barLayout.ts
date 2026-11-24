/*
 * 批次動作列放不下時，欄位動作依序收進「更多動作」（和工具列的「更多條件」同一個做法，item-table/inlineConditions.ts）：
 * 動作列永遠一行，不推動版面（docs/DESIGN.md 第 14 節的 contentStart 預算）。寬度是實測的估計值（rem），算得略寬一點。
 */

export type FieldActionKey = 'state' | 'assignee' | 'priority' | 'due' | 'cycle' | 'milestone';

/** 先收比較少用的；狀態最常用，最後才收。 */
export const BAR_COLLAPSE_ORDER: readonly FieldActionKey[] = ['milestone', 'cycle', 'due', 'priority', 'assignee', 'state'];

/** 每個動作在列上的寬度（rem，2026-11-18 在 1024 寬實測：狀態 72px、負責人與優先度 84、到期日 89、週期 72、里程碑 84）。 */
const ACTION_REM: Readonly<Record<FieldActionKey, number>> = {
  state: 4.5,
  assignee: 5.25,
  priority: 5.25,
  due: 5.6,
  cycle: 4.5,
  milestone: 5.25,
};
/** 「已選 N 個」（三位數時）、「刪除」（76px）、「取消選取」（78px）、「更多動作」（估計）。 */
const COUNT_REM = 4.5;
const DELETE_REM = 4.75;
const CANCEL_REM = 4.9;
const MORE_REM = 6.5;
/** 超過上限、處理中時動作列多一行字（「一次最多 200 個」）。 */
const NOTE_REM = 7;
/** 控制項之間的間距（列內 0.5rem、和「取消選取」之間 0.75rem，一律取大的）。 */
const GAP_REM = 0.75;

export interface BarOptions {
  readonly hasDelete: boolean;
  /** 動作列上有一行說明（超過上限、處理中）。 */
  readonly hasNote: boolean;
}

function neededRem(inline: ReadonlySet<FieldActionKey>, hasMore: boolean, { hasDelete, hasNote }: BarOptions): number {
  const widths = [
    COUNT_REM,
    CANCEL_REM,
    ...(hasDelete ? [DELETE_REM] : []),
    ...(hasMore ? [MORE_REM] : []),
    ...(hasNote ? [NOTE_REM] : []),
    ...[...inline].map((key) => ACTION_REM[key]),
  ];
  return widths.reduce((sum, width) => sum + width, 0) + widths.length * GAP_REM;
}

/**
 * 寬度（rem）下留在列上的欄位動作：從全部開始，放不下就依 BAR_COLLAPSE_ORDER 收一個，直到放得下或都收了。
 * 量不到寬度（null）當成放得下。
 */
export function inlineActionKeys(widthRem: number | null, present: readonly FieldActionKey[], options: BarOptions): ReadonlySet<FieldActionKey> {
  const inline = new Set(present);
  if (widthRem === null) return inline;
  for (const key of BAR_COLLAPSE_ORDER.filter((candidate) => present.includes(candidate))) {
    if (neededRem(inline, inline.size < present.length, options) <= widthRem) break;
    inline.delete(key);
  }
  return inline;
}

/** 收進「更多動作」的，照收的順序。 */
export function collapsedActions(inline: ReadonlySet<FieldActionKey>, present: readonly FieldActionKey[]): readonly FieldActionKey[] {
  return BAR_COLLAPSE_ORDER.filter((key) => present.includes(key) && !inline.has(key));
}
