import { descriptionRevisionOf } from './items';
import { http, type JsonBodyType } from 'msw';
import type { DeletedItemSummary } from '../api/itemRecovery';
import type { ItemDetail } from '../api/types';
import { failure, ok } from './api';
import { exampleOwner } from './fixtures';
import { ownerRef } from './activity';
import { server } from './server';

/** 已刪除的項目（清單的形狀）。預設是範例使用者在 2026-11-16 刪除的 AGY 需求。 */
export function deletedItem(number: number, overrides: Partial<DeletedItemSummary> = {}): DeletedItemSummary {
  return {
    key: `AGY-${String(number)}`,
    projectKey: 'AGY',
    number,
    type: 'requirement',
    title: `舊的需求 ${String(number)}`,
    state: 'confirming',
    stateCategory: 'not_started',
    parentKey: null,
    deletedBy: ownerRef,
    deletedAt: '2026-11-16T09:00:00Z',
    ...overrides,
  };
}

/** 復原失敗時回的錯誤。 */
export interface RestoreRejection {
  readonly status: number;
  readonly code: string;
  readonly message: string;
  readonly details?: JsonBodyType;
}

export interface ServedDeletedItems {
  /** GET 清單收到的每一個請求。 */
  readonly requests: URL[];
  /** 收到的復原請求（項目編號，依序）。 */
  readonly restored: string[];
  /** 目前伺服器上還刪除著的項目。 */
  readonly current: () => readonly DeletedItemSummary[];
  /** 這個項目的復原會被擋下，回這個錯誤。 */
  readonly rejectRestore: (itemKey: string, rejection: RestoreRejection) => void;
  /** 讓清單讀不到（之後的請求都回 500）。 */
  readonly failList: () => void;
}

interface ServeOptions {
  /** 復原要等這個 promise 完成才回應：測送出中的狀態。 */
  readonly gate?: Promise<unknown>;
  /** 每頁幾筆；沒給就照請求的 limit（預設 50）。 */
  readonly pageSize?: number;
}

const DEFAULT_LIMIT = 50;

function restoredDetail(item: DeletedItemSummary): ItemDetail {
  return {
    key: item.key,
    projectKey: item.projectKey,
    number: item.number,
    type: item.type,
    title: item.title,
    state: item.state,
    stateCategory: item.stateCategory,
    assignee: { personId: exampleOwner.personId, displayName: exampleOwner.displayName },
    priority: 'none',
    startDate: null,
    dueDate: null,
    estimate: null,
    parentKey: item.parentKey,
    cycleId: null,
    milestoneId: null,
    fields: {},
    createdAt: '2026-11-03T02:00:00Z',
    updatedAt: '2026-11-17T02:00:00Z',
    description: null,
    descriptionRevision: descriptionRevisionOf(null),
  };
}

/**
 * 假的已刪除項目：GET /v1/projects/{projectKey}/items/deleted（游標是已讀過幾筆，照真的後端分頁）與
 * POST /v1/items/{key}/restore（成功就從清單拿掉，回復原後的項目）。
 */
export function serveDeletedItems(
  projectKey: string,
  initial: readonly DeletedItemSummary[],
  options: ServeOptions = {},
): ServedDeletedItems {
  let rows = [...initial];
  let isListFailing = false;
  const requests: URL[] = [];
  const restored: string[] = [];
  const rejections = new Map<string, RestoreRejection>();
  server.use(
    http.get(`/v1/projects/${projectKey}/items/deleted`, ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      if (isListFailing) return failure(500, 'server.error', '伺服器暫時無法處理。');
      const limit = options.pageSize ?? Number(url.searchParams.get('limit') ?? DEFAULT_LIMIT);
      const start = Number(url.searchParams.get('cursor')?.replace('offset-', '') ?? 0);
      const items = rows.slice(start, start + limit);
      const nextCursor = start + limit < rows.length ? `offset-${String(start + limit)}` : null;
      return ok({ items, nextCursor });
    }),
    http.post('/v1/items/:itemKey/restore', async ({ params }) => {
      const itemKey = String(params.itemKey);
      restored.push(itemKey);
      await options.gate;
      const rejection = rejections.get(itemKey);
      if (rejection !== undefined) return failure(rejection.status, rejection.code, rejection.message, rejection.details);
      const item = rows.find((row) => row.key === itemKey);
      if (item === undefined) return failure(404, 'item.not_found', '找不到這個項目。');
      rows = rows.filter((row) => row.key !== itemKey);
      return ok(restoredDetail(item));
    }),
  );
  return {
    requests,
    restored,
    current: () => rows,
    rejectRestore: (itemKey, rejection) => {
      rejections.set(itemKey, rejection);
    },
    failList: () => {
      isListFailing = true;
    },
  };
}
