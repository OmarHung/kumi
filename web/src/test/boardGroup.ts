import { http } from 'msw';
import type { Item, ItemPage, ItemPatch } from '../api/types';
import { failure, ok } from './api';
import { exampleOwner } from './fixtures';
import { server } from './server';

interface GroupedServerOptions {
  /** 一頁幾個；沒給時照請求的 limit。 */
  readonly pageSize?: number;
  /** 修改這幾個項目時回這個錯（項目的 key 對到回應）。 */
  readonly failPatch?: Readonly<Record<string, () => Response>>;
}

export interface GroupedServer {
  /** 收到的每一個清單請求的網址。 */
  readonly requests: URL[];
  /** 收到的每一個修改（項目的 key 與內文）。 */
  readonly patches: { readonly key: string; readonly body: ItemPatch }[];
  /** 伺服器上現在的項目。 */
  readonly items: () => readonly Item[];
}

function matches(item: Item, params: URLSearchParams): boolean {
  const categories = params.getAll('stateCategory');
  const assignee = params.get('assignee');
  const fieldFilters = [...params].filter(([name]) => name.startsWith('field.'));
  return (
    (categories.length === 0 || categories.includes(item.stateCategory)) &&
    (assignee === null || item.assignee?.personId === (assignee === 'me' ? exampleOwner.personId : assignee)) &&
    fieldFilters.every(([name, value]) => item.fields[name.slice('field.'.length)] === value)
  );
}

function applyPatch(item: Item, body: ItemPatch, memberName: (personId: string) => string): Item {
  const fields = Object.fromEntries(
    Object.entries({ ...item.fields, ...body.fields }).filter(([, value]) => value !== null),
  ) as Item['fields'];
  const assignee =
    body.assigneeId === undefined
      ? item.assignee
      : body.assigneeId === null
        ? null
        : { personId: body.assigneeId, displayName: memberName(body.assigneeId) };
  return { ...item, assignee, fields, updatedAt: '2026-11-19T09:00:00Z' };
}

/**
 * 假的項目清單與修改，照條件真的過濾（stateCategory、assignee、field.*，照編號遞增，游標是位移量）；
 * 修改成功後伺服器上的項目跟著變，重抓會看到新的樣子。依負責人、欄位分欄的看板是一次讀一份清單，不分狀態。
 */
export function serveGroupedItems(
  projectKey: string,
  initial: readonly Item[],
  memberName: (personId: string) => string,
  options: GroupedServerOptions = {},
): GroupedServer {
  const requests: URL[] = [];
  const patches: { key: string; body: ItemPatch }[] = [];
  let items = initial;
  server.use(
    http.get(`/v1/projects/${projectKey}/items`, ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const limit = options.pageSize ?? Number(url.searchParams.get('limit') ?? '50');
      const offset = Number((url.searchParams.get('cursor') ?? 'page-0').replace('page-', ''));
      const found = items.filter((item) => matches(item, url.searchParams)).toSorted((a, b) => a.number - b.number);
      const nextOffset = offset + limit;
      const page: ItemPage = { items: found.slice(offset, nextOffset), nextCursor: nextOffset < found.length ? `page-${String(nextOffset)}` : null };
      return ok(page);
    }),
    http.patch('/v1/items/:key', async ({ request, params }) => {
      const key = String(params.key);
      const body = (await request.json()) as ItemPatch;
      patches.push({ key, body });
      const fail = options.failPatch?.[key];
      if (fail !== undefined) return fail();
      const current = items.find((item) => item.key === key);
      if (current === undefined) return failure(404, 'item.not_found', '找不到項目');
      const updated = applyPatch(current, body, memberName);
      items = items.map((item) => (item.key === key ? updated : item));
      return ok({ ...updated, description: null });
    }),
  );
  return { requests, patches, items: () => items };
}
