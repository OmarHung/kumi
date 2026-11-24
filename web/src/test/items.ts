import { http, type JsonBodyType } from 'msw';
import type { Item, ItemDetail, ItemPage } from '../api/types';
import { failure, ok } from './api';
import { server } from './server';

/**
 * 假的項目清單。回傳收到的每一個請求的網址，測試可以檢查查詢參數。
 * pages 有多頁時依序回傳；最後一頁之後一直回最後一頁。
 */
export function serveItems(projectKey: string, ...pages: readonly (readonly Item[])[]): URL[] {
  const requests: URL[] = [];
  server.use(
    http.get(`/v1/projects/${projectKey}/items`, ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const cursor = url.searchParams.get('cursor');
      const index = cursor === null ? 0 : Number(cursor.replace('page-', ''));
      const items = pages[index] ?? pages.at(-1) ?? [];
      const nextCursor = index + 1 < pages.length ? `page-${String(index + 1)}` : null;
      const page: ItemPage = { items, nextCursor };
      return ok(page);
    }),
  );
  return requests;
}

/**
 * 假的 GET /v1/search/items（跨專案搜尋）：標題或編號包含 q 的才回，順序照給的（伺服器排好的順序）。
 * 回傳收到的每一個請求的網址。
 */
export function serveSearch(items: readonly Item[]): URL[] {
  const requests: URL[] = [];
  server.use(
    http.get('/v1/search/items', ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const q = (url.searchParams.get('q') ?? '').toLowerCase();
      const found = items.filter((item) => item.title.toLowerCase().includes(q) || item.key.toLowerCase() === q);
      return ok({ items: found });
    }),
  );
  return requests;
}

export interface RecordedRequest {
  readonly url: URL;
  readonly body: unknown;
}

export const DESCRIPTION_CONFLICT_MESSAGE = '這段說明在你編輯時被別人改過了。';

/**
 * 假的 PATCH /v1/items/{key}：把收到的內文合併到項目上回傳（fields 的 null 是清掉），
 * 和後端一樣會記住上一次改好的樣子。說明與說明的版本照後端：同時給了 description 與 expectedDescriptionRevision，
 * 版本和現在的不同就整個不改、回 409 item.description_conflict；改了說明就換新的版本。
 * 傳進來的是帶說明的項目（detailOf）時才有說明與版本；只給清單的項目時說明是 null。
 */
export function servePatch(item: Item | ItemDetail): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  let current: ItemDetail = 'descriptionRevision' in item ? item : detailOf(item);
  server.use(
    http.patch(`/v1/items/${item.key}`, async ({ request }) => {
      const body = (await request.json()) as PatchBody;
      requests.push({ url: new URL(request.url), body });
      if (isDescriptionConflict(current, body)) {
        return failure(409, 'item.description_conflict', DESCRIPTION_CONFLICT_MESSAGE);
      }
      current = applyPatch(current, body);
      return ok(current);
    }),
  );
  return requests;
}

type PatchBody = Partial<Omit<Item, 'fields'>> & { description?: string | null; fields?: Record<string, unknown>; expectedDescriptionRevision?: string };

function isDescriptionConflict(item: ItemDetail, body: PatchBody): boolean {
  return 'description' in body && body.expectedDescriptionRevision !== undefined && body.expectedDescriptionRevision !== item.descriptionRevision;
}

function applyPatch(item: ItemDetail, body: PatchBody): ItemDetail {
  const changes = Object.fromEntries(Object.entries(body).filter(([name]) => name !== 'expectedDescriptionRevision')) as PatchBody;
  const fields = Object.fromEntries(
    Object.entries({ ...item.fields, ...body.fields }).filter(([, value]) => value !== null),
  ) as Item['fields'];
  const description = 'description' in changes ? (changes.description ?? null) : item.description;
  const next = { ...item, ...changes, description, fields, updatedAt: '2026-11-13T09:00:00Z' };
  return 'description' in changes ? { ...next, descriptionRevision: descriptionRevisionOf(description) } : next;
}

/** 假的 POST /v1/items/{key}/transitions：成功時回傳改好狀態的項目。 */
export function serveTransition(item: Item, result: Item): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  server.use(
    http.post(`/v1/items/${item.key}/transitions`, async ({ request }) => {
      requests.push({ url: new URL(request.url), body: await request.json() });
      return ok(result);
    }),
  );
  return requests;
}

export function failTransition(item: Item, status: number, code: string, message: string, details: JsonBodyType): void {
  server.use(http.post(`/v1/items/${item.key}/transitions`, () => failure(status, code, message, details)));
}

/** 假的 GET /v1/items/{key}：單一項目，帶說明。 */
export function serveItem(item: ItemDetail): void {
  server.use(http.get(`/v1/items/${item.key}`, () => ok(item)));
}

/** 假的說明版本：內容一樣版本就一樣（後端是內容的雜湊），沒有說明是固定的值。 */
export function descriptionRevisionOf(description: string | null): string {
  return `rev:${description ?? ''}`;
}

/** 單一項目的回應：清單的項目加上說明與說明的版本。 */
export function detailOf(item: Item, description: string | null = null): ItemDetail {
  return { ...item, description, descriptionRevision: descriptionRevisionOf(description) };
}

/** 刪除時回的時刻。 */
export const DELETED_AT = '2026-11-16T09:00:00Z';

/**
 * 假的 DELETE /v1/items/{key}，照合約：沒有下層時回 200 與 { key, deletedAt }；
 * 給了下層（還沒刪除的）就像後端一樣回 409 item.has_children，details 每個下層一筆（path 是 children/下層的編號）。
 * 回傳收到的請求。清單與項目本身的假資料由測試自己換掉（刪除後重抓就讀不到它）。
 */
export function serveDeleteItem(item: Item, children: readonly Item[] = []): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  server.use(
    http.delete(`/v1/items/${item.key}`, ({ request }) => {
      requests.push({ url: new URL(request.url), body: undefined });
      if (children.length === 0) return ok({ key: item.key, deletedAt: DELETED_AT });
      const details = children.map((child) => ({
        code: 'item.has_children',
        path: `children/${child.key}`,
        message: `${child.key} 還掛在這個項目底下。`,
      }));
      return failure(409, 'item.has_children', '還有下層項目，不能刪除。', details);
    }),
  );
  return requests;
}

/** 假的 POST /v1/items/{key}/type：記下內文（{ type }），回 respond 給的項目（和 GET 同一個形狀）。 */
export function serveChangeType(item: Item, respond: (type: string) => ItemDetail): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  server.use(
    http.post(`/v1/items/${item.key}/type`, async ({ request }) => {
      const body = (await request.json()) as { readonly type: string };
      requests.push({ url: new URL(request.url), body });
      return ok(respond(body.type));
    }),
  );
  return requests;
}

export function failChangeType(item: Item, status: number, code: string, message: string, details: JsonBodyType = []): void {
  server.use(http.post(`/v1/items/${item.key}/type`, () => failure(status, code, message, details)));
}
