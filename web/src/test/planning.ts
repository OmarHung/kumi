import { http } from 'msw';
import type { Cycle, CycleDraft, ItemProgress, Milestone, MilestoneDraft } from '../api/planning';
import { failure, ok } from './api';
import type { RecordedRequest } from './items';
import { server } from './server';

export const NO_PROGRESS: ItemProgress = { total: 0, done: 0, canceled: 0, estimateTotal: null, estimateDone: null };

/** 一個週期。進度預設是空的。 */
export function cycleOf(name: string, startDate: string, endDate: string, overrides: Partial<Cycle> = {}): Cycle {
  return { id: `cycle-${name}`, name, startDate, endDate, progress: NO_PROGRESS, ...overrides };
}

/** 一個里程碑。進度預設是空的。 */
export function milestoneOf(name: string, dueDate: string, overrides: Partial<Milestone> = {}): Milestone {
  return { id: `milestone-${name}`, name, dueDate, description: null, progress: NO_PROGRESS, ...overrides };
}

export interface Rejection {
  readonly status: number;
  readonly code: string;
  readonly message: string;
}

interface ServeOptions {
  /** 新增、換掉、刪除時回的錯誤（例如週期重疊）；沒給就成功。 */
  readonly rejectWith?: Rejection;
  /** 寫入要等這個 promise 完成才回應：測送出中的狀態。 */
  readonly gate?: Promise<unknown>;
}

export interface PlanningServer<T> {
  readonly created: RecordedRequest[];
  readonly replaced: RecordedRequest[];
  readonly deleted: RecordedRequest[];
  /** 目前伺服器上的內容（照真的後端的順序）。 */
  readonly current: () => readonly T[];
  /** 下一次新增、換掉或刪除回這個錯誤（只一次）：測伺服器才知道的規則（重疊、重名）。 */
  readonly rejectNext: (error: Rejection) => void;
}

interface Kind<T, D> {
  readonly path: 'cycles' | 'milestones';
  readonly compare: (a: T, b: T) => number;
  readonly build: (draft: D, id: string) => T;
  readonly notFound: { readonly code: string; readonly message: string };
}

function serveKind<T extends { readonly id: string }, D>(
  projectKey: string,
  initial: readonly T[],
  options: ServeOptions,
  kind: Kind<T, D>,
): PlanningServer<T> {
  let rows = [...initial];
  const created: RecordedRequest[] = [];
  const replaced: RecordedRequest[] = [];
  const deleted: RecordedRequest[] = [];
  const base = `/v1/projects/${projectKey}/${kind.path}`;
  let nextRejection: Rejection | undefined;
  const reject = () => {
    const rejection = nextRejection ?? options.rejectWith;
    nextRejection = undefined;
    return rejection === undefined ? null : failure(rejection.status, rejection.code, rejection.message);
  };
  const notFound = () => failure(404, kind.notFound.code, kind.notFound.message);
  server.use(
    http.get(base, () => ok([...rows].sort(kind.compare) as never)),
    http.post(base, async ({ request }) => {
      const body = (await request.json()) as D;
      created.push({ url: new URL(request.url), body });
      await options.gate;
      const rejected = reject();
      if (rejected !== null) return rejected;
      const row = kind.build(body, `new-${String(created.length)}`);
      rows = [...rows, row];
      return ok(row as never, 201);
    }),
    http.put(`${base}/:id`, async ({ request, params }) => {
      const body = (await request.json()) as D;
      replaced.push({ url: new URL(request.url), body });
      await options.gate;
      const rejected = reject();
      if (rejected !== null) return rejected;
      const existing = rows.find((row) => row.id === params.id);
      if (existing === undefined) return notFound();
      const next = { ...existing, ...body };
      rows = rows.map((row) => (row.id === existing.id ? next : row));
      return ok(next as never);
    }),
    http.delete(`${base}/:id`, async ({ request, params }) => {
      deleted.push({ url: new URL(request.url), body: params.id });
      await options.gate;
      const rejected = reject();
      if (rejected !== null) return rejected;
      if (!rows.some((row) => row.id === params.id)) return notFound();
      rows = rows.filter((row) => row.id !== params.id);
      return new Response(null, { status: 204 });
    }),
  );
  return {
    created,
    replaced,
    deleted,
    current: () => [...rows].sort(kind.compare),
    rejectNext: (error) => {
      nextRejection = error;
    },
  };
}

/** 假的週期端點：記住新增、換掉、刪除之後的樣子（照開始日排序），重抓清單時看得到。 */
export function serveCycles(projectKey: string, initial: readonly Cycle[] = [], options: ServeOptions = {}): PlanningServer<Cycle> {
  return serveKind<Cycle, CycleDraft>(projectKey, initial, options, {
    path: 'cycles',
    compare: (a, b) => a.startDate.localeCompare(b.startDate),
    build: (draft, id) => ({ id, ...draft, progress: NO_PROGRESS }),
    notFound: { code: 'cycle.not_found', message: '找不到這個週期。' },
  });
}

/** 假的里程碑端點：照到期日、同一天照名稱排序。 */
export function serveMilestones(projectKey: string, initial: readonly Milestone[] = [], options: ServeOptions = {}): PlanningServer<Milestone> {
  return serveKind<Milestone, MilestoneDraft>(projectKey, initial, options, {
    path: 'milestones',
    compare: (a, b) => a.dueDate.localeCompare(b.dueDate) || a.name.localeCompare(b.name, 'zh-Hant'),
    build: (draft, id) => ({ id, ...draft, progress: NO_PROGRESS }),
    notFound: { code: 'milestone.not_found', message: '找不到這個里程碑。' },
  });
}
