import type { OverviewScope, ProjectOverview } from '../../api/overview';

/** 排序：風險（已過期加停滯，由多到少）、專案代號、最近更新。網址的 `sort=`，預設的風險不寫。 */
export type OverviewSort = 'risk' | 'key' | 'updated';

export const OVERVIEW_SORTS: readonly OverviewSort[] = ['risk', 'key', 'updated'];

export const OVERVIEW_SORT_LABELS: Readonly<Record<OverviewSort, string>> = {
  risk: '已過期與停滯多的在前',
  key: '專案代號',
  updated: '最近更新',
};

export const DEFAULT_OVERVIEW_SORT: OverviewSort = 'risk';
export const DEFAULT_OVERVIEW_SCOPE: OverviewScope = 'managed';

const SORT_PARAM = 'sort';
const SCOPE_PARAM = 'scope';

/** 網址可以手改：不認得的值當成預設，不要讓整頁壞掉。 */
export function overviewSortOf(params: URLSearchParams): OverviewSort {
  return OVERVIEW_SORTS.find((sort) => sort === params.get(SORT_PARAM)) ?? DEFAULT_OVERVIEW_SORT;
}

export function overviewScopeOf(params: URLSearchParams): OverviewScope {
  return params.get(SCOPE_PARAM) === 'visible' ? 'visible' : DEFAULT_OVERVIEW_SCOPE;
}

/** 換範圍或排序後的查詢字串：預設值不寫，網址短一點；另一個參數照舊帶著。 */
export function withOverviewView(params: URLSearchParams, view: { readonly scope: OverviewScope; readonly sort: OverviewSort }): URLSearchParams {
  const next = new URLSearchParams(params);
  next.delete(SCOPE_PARAM);
  next.delete(SORT_PARAM);
  if (view.scope !== DEFAULT_OVERVIEW_SCOPE) next.set(SCOPE_PARAM, view.scope);
  if (view.sort !== DEFAULT_OVERVIEW_SORT) next.set(SORT_PARAM, view.sort);
  return next;
}

function riskOf(project: ProjectOverview): number {
  return project.overdue + project.stale;
}

function updatedAtOf(project: ProjectOverview): number {
  return project.lastUpdatedAt === null ? Number.NEGATIVE_INFINITY : Date.parse(project.lastUpdatedAt);
}

function byKey(a: ProjectOverview, b: ProjectOverview): number {
  return a.projectKey.localeCompare(b.projectKey);
}

/** 排好的新陣列（不動原本的）。同分一律再依代號，順序才穩定；沒有項目的專案（沒有更新時間）排在「最近更新」的最後。 */
export function sortOverviews(projects: readonly ProjectOverview[], sort: OverviewSort): readonly ProjectOverview[] {
  const compare = {
    risk: (a: ProjectOverview, b: ProjectOverview) => riskOf(b) - riskOf(a) || byKey(a, b),
    key: byKey,
    updated: (a: ProjectOverview, b: ProjectOverview) => {
      const delta = updatedAtOf(b) - updatedAtOf(a);
      return Number.isNaN(delta) || delta === 0 ? byKey(a, b) : delta;
    },
  }[sort];
  return [...projects].sort(compare);
}
