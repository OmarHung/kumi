import { projectPagePath } from '../work-items/itemLinks';

/**
 * 總覽的數字連到專案的表格、套好篩選。條件的名稱與意思和專案清單的網址一樣（domain/itemFilters.ts），
 * 數字與點下去的清單筆數要一樣：已過期、7 天內到期、停滯都只算還沒結束的，後兩者由清單的條件自己帶上還沒結束的狀態類別。
 * 不帶 view：連結一律開表格（篩選只有表格有「只看還沒結束的」「14 天沒有更新」）。
 */
function projectLink(projectKey: string, entries: readonly (readonly [string, string])[]): string {
  const search = new URLSearchParams(entries.map(([name, value]) => [name, value])).toString();
  return search === '' ? projectPagePath(projectKey) : `${projectPagePath(projectKey)}?${search}`;
}

export const overviewLinks = {
  project: (projectKey: string) => projectLink(projectKey, []),
  state: (projectKey: string, stateKey: string) => projectLink(projectKey, [['state', stateKey]]),
  open: (projectKey: string) => projectLink(projectKey, [['open', '1']]),
  overdue: (projectKey: string) => projectLink(projectKey, [['due', 'overdue']]),
  dueSoon: (projectKey: string) => projectLink(projectKey, [['due', 'soon']]),
  unassigned: (projectKey: string) =>
    projectLink(projectKey, [
      ['assignee', 'unassigned'],
      ['open', '1'],
    ]),
  stale: (projectKey: string) =>
    projectLink(projectKey, [
      ['stale', '1'],
      ['open', '1'],
    ]),
  cycle: (projectKey: string, cycleId: string) => projectLink(projectKey, [['cycle', cycleId]]),
  milestone: (projectKey: string, milestoneId: string) => projectLink(projectKey, [['milestone', milestoneId]]),
};
