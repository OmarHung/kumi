import { http } from 'msw';
import type { Item, ItemPage } from '../api/types';
import { ok } from './api';
import { server } from './server';

export interface DatedItemsServer {
  /** 收到的每一個清單請求的網址。 */
  readonly requests: URL[];
}

/** 期間重疊：開始日（沒有就用到期日）到到期日（沒有就用開始日）和 datesFrom～datesTo 有交集；兩個日期都沒有的不算。 */
function matchesDates(item: Item, params: URLSearchParams): boolean {
  const from = params.get('datesFrom');
  const to = params.get('datesTo');
  if (from === null && to === null) return true;
  const start = item.startDate ?? item.dueDate;
  const end = item.dueDate ?? item.startDate;
  if (start === null || end === null) return false;
  return (to === null || start <= to) && (from === null || end >= from);
}

function matchesDue(item: Item, params: URLSearchParams): boolean {
  const from = params.get('dueFrom');
  const to = params.get('dueTo');
  if (params.get('noDue') === 'true') return item.dueDate === null;
  if (from === null && to === null) return true;
  if (item.dueDate === null) return false;
  return (from === null || item.dueDate >= from) && (to === null || item.dueDate <= to);
}

/**
 * 假的項目清單，照 dueFrom、dueTo、noDue、datesFrom、datesTo 篩，照到期日（再照編號）排，游標是位移量（page-200 從第 201 個開始）。
 * pageSize 沒給時照請求的 limit：行事曆一頁讀 200 筆，要測讀很多頁時可以給小一點的。
 */
export function serveDatedItems(projectKey: string, items: readonly Item[], pageSize?: number): DatedItemsServer {
  const requests: URL[] = [];
  server.use(
    http.get(`/v1/projects/${projectKey}/items`, ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const limit = pageSize ?? Number(url.searchParams.get('limit') ?? '50');
      const offset = Number((url.searchParams.get('cursor') ?? 'page-0').replace('page-', ''));
      const found = items
        .filter((item) => matchesDue(item, url.searchParams) && matchesDates(item, url.searchParams))
        .toSorted((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? '') || a.number - b.number);
      const next = offset + limit;
      const page: ItemPage = { items: found.slice(offset, next), nextCursor: next < found.length ? `page-${String(next)}` : null };
      return ok(page);
    }),
  );
  return { requests };
}
