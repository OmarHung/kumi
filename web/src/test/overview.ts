import { http } from 'msw';
import type { OverviewScope, ProjectOverview } from '../api/overview';
import { ok } from './api';
import { NO_PROGRESS } from './planning';
import { server } from './server';

/** 一個專案的總覽，數字都是 0、沒有週期與里程碑、沒有項目（和新開的專案一樣）；要什麼蓋什麼。 */
export function overviewOf(projectKey: string, name: string, overrides: Partial<ProjectOverview> = {}): ProjectOverview {
  return {
    projectKey,
    name,
    ownItemsOnly: false,
    states: [],
    open: 0,
    overdue: 0,
    dueSoon: 0,
    unassigned: 0,
    stale: 0,
    doneRecently: 0,
    currentCycle: null,
    nextMilestone: null,
    lastUpdatedAt: null,
    ...overrides,
  };
}

/** 有週期、有里程碑、各種數字的專案。 */
export const webOverview: ProjectOverview = overviewOf('WEB', '官網改版', {
  states: [
    { key: 'todo', name: '待辦', category: 'not_started', count: 3 },
    { key: 'doing', name: '進行中', category: 'in_progress', count: 4 },
    { key: 'done', name: '完成', category: 'done', count: 6 },
  ],
  open: 7,
  overdue: 2,
  dueSoon: 1,
  unassigned: 2,
  stale: 1,
  doneRecently: 3,
  currentCycle: {
    id: 'cycle-1',
    name: '第 12 週',
    startDate: '2026-11-23',
    endDate: '2026-12-06',
    progress: { total: 8, done: 3, canceled: 0, estimateTotal: null, estimateDone: null },
  },
  nextMilestone: {
    id: 'milestone-1',
    name: '正式上線',
    dueDate: '2026-12-15',
    progress: { total: 10, done: 6, canceled: 1, estimateTotal: null, estimateDone: null },
  },
  lastUpdatedAt: '2026-11-26T08:00:00Z',
});

/** 只看得到自己項目的專案：數字只算自己的，沒有週期。 */
export const appOverview: ProjectOverview = overviewOf('APP', '行動應用', {
  ownItemsOnly: true,
  states: [{ key: 'doing', name: '進行中', category: 'in_progress', count: 2 }],
  open: 2,
  overdue: 0,
  stale: 3,
  lastUpdatedAt: '2026-10-01T08:00:00Z',
});

/** 剛開的專案：什麼都是 0、沒有週期與里程碑、沒有更新時間。 */
export const opsOverview: ProjectOverview = overviewOf('OPS', '客戶維運', { nextMilestone: { id: 'm-ops', name: '年度檢查', dueDate: '2027-01-10', progress: NO_PROGRESS } });

/** 沒有任何東西的專案：數字都是 0、沒有狀態。 */
export const emptyOverview: ProjectOverview = overviewOf('NEW', '新專案');

/**
 * 假的 GET /v1/overview/projects：managed 與 visible 各給一份（visible 沒給就同 managed）。
 * 回傳收到的每一個請求的網址，測試可以檢查 scope。
 */
export function serveOverview(managed: readonly ProjectOverview[], visible: readonly ProjectOverview[] = managed): URL[] {
  const requests: URL[] = [];
  server.use(
    http.get('/v1/overview/projects', ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const scope: OverviewScope = url.searchParams.get('scope') === 'visible' ? 'visible' : 'managed';
      return ok((scope === 'visible' ? visible : managed) as never);
    }),
  );
  return requests;
}
