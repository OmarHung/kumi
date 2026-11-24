import type { CalendarDate, Item } from '../../api/types';
import type { WeekStart } from '../../lib/weekStart';
import { compareDueThenKey, groupByDue, type DueGroup, type DueSortable } from '../../domain/dates';

/** 我的工作的分組方式：依急迫程度（預設）或依專案，記在網址的 group 參數。 */
export type MyWorkGrouping = 'urgency' | 'project';

export const GROUPING_PARAM = 'group';
const PROJECT_GROUPING = 'project';

export const DUE_GROUP_LABELS: Readonly<Record<DueGroup, string>> = {
  overdue: '已過期',
  today: '今天',
  thisWeek: '這一週',
  later: '之後',
  none: '沒有到期日',
};

/** 不認得的值當成預設（依急迫程度）：分享來的網址打錯也照樣看得到清單。 */
export function groupingOf(params: URLSearchParams): MyWorkGrouping {
  return params.get(GROUPING_PARAM) === PROJECT_GROUPING ? 'project' : 'urgency';
}

/** 換成另一種分組的查詢字串：其他參數（開著的面板 item）留著；預設的分組不寫進網址。 */
export function groupingSearch(params: URLSearchParams, grouping: MyWorkGrouping): string {
  const next = new URLSearchParams(params);
  if (grouping === 'project') {
    next.set(GROUPING_PARAM, PROJECT_GROUPING);
  } else {
    next.delete(GROUPING_PARAM);
  }
  const query = next.toString();
  return query === '' ? '' : `?${query}`;
}

/** 可以分組的東西：分組與排序要的欄位，加上編號（畫面上的鍵）。 */
export type Groupable = DueSortable & { readonly key: string };

/**
 * 畫面上的一組：標題（h2）、這一組的表格或清單的名稱都是 label。T 是項目，或帶著位置的列（useMyWorkRows 的
 * MyWorkRow：分組看的是它第一次被記下時的到期日，改過的列停在原組）。
 */
export interface MyWorkGroup<T extends Groupable = Item> {
  readonly key: string;
  readonly label: string;
  /** 依專案分組時這一組的專案（組名連到那個專案）；依急迫程度分組沒有。 */
  readonly projectKey?: string;
  readonly items: readonly T[];
}

function byProject<T extends Groupable>(items: readonly T[], projectName: (projectKey: string) => string | undefined): readonly MyWorkGroup<T>[] {
  // 照代號排：專案的名稱要等專案內容讀到才知道，照名稱排的話組會在讀到之後換位置。
  const keys = [...new Set(items.map((item) => item.projectKey))].toSorted();
  const sorted = items.toSorted(compareDueThenKey);
  return keys.map((projectKey) => ({
    key: `project:${projectKey}`,
    label: projectName(projectKey) ?? projectKey,
    projectKey,
    items: sorted.filter((item) => item.projectKey === projectKey),
  }));
}

/** 分好的組，照畫面的順序；空的組不回傳。組內一律照到期日再照編號。 */
export function groupMyWork<T extends Groupable>(
  items: readonly T[],
  grouping: MyWorkGrouping,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  projectName: (projectKey: string) => string | undefined,
): readonly MyWorkGroup<T>[] {
  if (grouping === 'project') {
    return byProject(items, projectName);
  }
  return groupByDue(items, todayDate, weekStart).map(({ group, items: grouped }) => ({
    key: group,
    label: DUE_GROUP_LABELS[group],
    items: grouped,
  }));
}
