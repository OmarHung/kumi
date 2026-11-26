import { http } from 'msw';
import type { SavedView, SavedViewDraft, SavedViewQuery } from '../api/types';
import { failure, ok } from './api';
import type { RecordedRequest } from './items';
import { server } from './server';

export const NO_CONDITIONS: SavedViewQuery = {
  type: null,
  state: null,
  assignee: null,
  createdBy: null,
  mine: null,
  q: null,
  sort: null,
  due: null,
  priority: null,
  cycle: null,
  milestone: null,
  fields: null,
  category: null,
  project: null,
  group: null,
};

/** 一個檢視。條件只寫要的，其餘是 null。 */
export function savedViewOf(name: string, query: Partial<SavedViewQuery> = {}, overrides: Partial<SavedView> = {}): SavedView {
  return {
    id: `view-${name}`,
    name,
    layout: 'table',
    query: { ...NO_CONDITIONS, ...query },
    createdAt: '2026-11-17T01:00:00Z',
    updatedAt: '2026-11-17T01:00:00Z',
    ...overrides,
  };
}

export interface SavedViewsServer {
  readonly created: RecordedRequest[];
  readonly replaced: RecordedRequest[];
  readonly deleted: RecordedRequest[];
  /** 目前伺服器上的檢視（照名稱排序，和真的後端一樣）。 */
  readonly current: () => readonly SavedView[];
  /** 像別處（另一個分頁、另一個人的裝置）把這個檢視刪掉了：之後換掉或刪除會是 404。 */
  readonly drop: (id: string) => void;
}

interface ServeOptions {
  /** 新增或換掉時回的錯誤（例如名稱重複）；沒給就成功。 */
  /** 新增、換掉、刪除要等這個 promise 完成才回應：測送出中的狀態。 */
  readonly gate?: Promise<unknown>;
  readonly rejectWith?: { readonly status: number; readonly code: string; readonly message: string };
  /** 像真的後端一樣檢查：名稱不分大小寫不能重複、數量有上限（409）。沒給就不檢查。 */
  readonly enforce?: { readonly maxViews: number };
}

/** 「我的工作」的檢視一個人最多這麼多個，和後端的 SavedViewRules.MaxMyWork 一樣。 */
export const MY_WORK_VIEW_LIMIT = 50;

function byName(a: SavedView, b: SavedView): number {
  return a.name.localeCompare(b.name, 'zh-Hant');
}

/**
 * 假的檢視端點：記住新增、換掉、刪除之後的樣子，重抓清單時看得到。
 * 回傳收到的請求，測試可以檢查內文。
 */
export function serveSavedViews(projectKey: string, initial: readonly SavedView[] = [], options: ServeOptions = {}): SavedViewsServer {
  return serveViewsAt(`/v1/projects/${projectKey}/saved-views`, initial, options);
}

/** 假的「我的工作」檢視端點（/v1/my-work/saved-views）：名稱重複與數量上限照後端的規則回 409。 */
export function serveMyWorkViews(initial: readonly SavedView[] = [], options: ServeOptions = {}): SavedViewsServer {
  return serveViewsAt('/v1/my-work/saved-views', initial, { enforce: { maxViews: MY_WORK_VIEW_LIMIT }, ...options });
}

function ruleViolation(views: readonly SavedView[], name: string, exceptId: string | null, maxViews: number | null) {
  const taken = views.some((view) => view.id !== exceptId && view.name.toLowerCase() === name.toLowerCase());
  if (taken) return failure(409, 'saved_view.name_taken', '已經有同名的檢視。');
  if (exceptId === null && maxViews !== null && views.length >= maxViews) {
    return failure(409, 'saved_view.limit_reached', `檢視最多 ${String(maxViews)} 個。`);
  }
  return null;
}

function serveViewsAt(base: string, initial: readonly SavedView[], options: ServeOptions): SavedViewsServer {
  let views = [...initial];
  const created: RecordedRequest[] = [];
  const replaced: RecordedRequest[] = [];
  const deleted: RecordedRequest[] = [];
  const reject = () =>
    options.rejectWith === undefined ? null : failure(options.rejectWith.status, options.rejectWith.code, options.rejectWith.message);
  const waitForGate = async () => {
    await options.gate;
  };
  server.use(
    http.get(base, () => ok([...views].sort(byName))),
    http.post(base, async ({ request }) => {
      const body = (await request.json()) as SavedViewDraft;
      created.push({ url: new URL(request.url), body });
      await waitForGate();
      const rejected = reject() ?? (options.enforce ? ruleViolation(views, body.name, null, options.enforce.maxViews) : null);
      if (rejected !== null) return rejected;
      const view = savedViewOf(body.name, body.query, { layout: body.layout, id: `new-${String(created.length)}` });
      views = [...views, view];
      return ok(view, 201);
    }),
    http.put(`${base}/:id`, async ({ request, params }) => {
      const body = (await request.json()) as SavedViewDraft;
      replaced.push({ url: new URL(request.url), body });
      await waitForGate();
      const rejected = reject() ?? (options.enforce ? ruleViolation(views, body.name, String(params.id), null) : null);
      if (rejected !== null) return rejected;
      const existing = views.find((view) => view.id === params.id);
      if (existing === undefined) return failure(404, 'saved_view.not_found', '找不到這個檢視。');
      const next = { ...existing, ...body };
      views = views.map((view) => (view.id === existing.id ? next : view));
      return ok(next);
    }),
    http.delete(`${base}/:id`, async ({ request, params }) => {
      deleted.push({ url: new URL(request.url), body: params.id });
      await waitForGate();
      if (!views.some((view) => view.id === params.id)) return failure(404, 'saved_view.not_found', '找不到這個檢視。');
      views = views.filter((view) => view.id !== params.id);
      return new Response(null, { status: 204 });
    }),
  );
  return {
    created,
    replaced,
    deleted,
    current: () => [...views].sort(byName),
    drop: (id) => {
      views = views.filter((view) => view.id !== id);
    },
  };
}
