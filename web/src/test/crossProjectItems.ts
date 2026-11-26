import { http } from 'msw';
import type { StateCategory } from '../api/blueprint';
import type { Item, ItemPage, ItemTypeSummary, Priority, ProjectDetail } from '../api/types';
import { ok } from './api';
import { matchesCreatedBy } from './createdBy';
import { server } from './server';

/**
 * 假的跨專案清單（GET /v1/items）：照後端的規則篩選、排序與分頁（internal/HANDOFF-my-work-views.md 第 4.1 節），
 * 「我的工作」的測試用。只做畫面會送的條件；游標是 `offset-N`。
 */

const CATEGORY_ORDER: readonly StateCategory[] = ['backlog', 'not_started', 'in_progress', 'done', 'canceled'];
const PRIORITY_ORDER: readonly Priority[] = ['urgent', 'high', 'medium', 'low'];
const DEFAULT_LIMIT = 50;

function inList(values: readonly string[], value: string): boolean {
  return values.length === 0 || values.includes(value);
}

function matchesAssignee(item: Item, assignee: string | null, me: string): boolean {
  if (assignee === null) return true;
  if (assignee === 'unassigned') return item.assignee === null;
  return item.assignee?.personId === (assignee === 'me' ? me : assignee);
}

function matchesDates(item: Item, params: URLSearchParams): boolean {
  const dueFrom = params.get('dueFrom');
  const dueTo = params.get('dueTo');
  const datesFrom = params.get('datesFrom');
  const datesTo = params.get('datesTo');
  if (params.get('noDue') === 'true' && item.dueDate !== null) return false;
  if (dueFrom !== null && (item.dueDate === null || item.dueDate < dueFrom)) return false;
  if (dueTo !== null && (item.dueDate === null || item.dueDate > dueTo)) return false;
  const start = item.startDate ?? item.dueDate;
  const end = item.dueDate ?? item.startDate;
  if (datesTo !== null && (start === null || start > datesTo)) return false;
  return datesFrom === null || (end !== null && end >= datesFrom);
}

function matchesSearch(item: Item, q: string | null): boolean {
  const text = q?.trim().toLowerCase() ?? '';
  return text === '' || item.title.toLowerCase().includes(text) || item.key.toLowerCase() === text;
}

function matches(item: Item, params: URLSearchParams, me: string): boolean {
  return (
    inList(params.getAll('type'), item.type) &&
    inList(params.getAll('state'), item.state) &&
    inList(params.getAll('stateCategory'), item.stateCategory) &&
    inList(params.getAll('priority'), item.priority) &&
    inList(params.getAll('project').map((key) => key.toUpperCase()), item.projectKey) &&
    matchesAssignee(item, params.get('assignee'), me) &&
    matchesCreatedBy(item, params.get('createdBy'), me) &&
    matchesDates(item, params) &&
    matchesSearch(item, params.get('q'))
  );
}

/** 空值一律排最後（兩個方向都是），同值再依專案代號、編號：和後端一樣。 */
function compareBy(field: string): (a: Item, b: Item) => number | null {
  const rank = (value: number) => (value < 0 ? null : value);
  switch (field) {
    case 'due_date':
      return (a, b) => (a.dueDate === null || b.dueDate === null ? null : a.dueDate.localeCompare(b.dueDate));
    case 'start_date':
      return (a, b) => (a.startDate === null || b.startDate === null ? null : a.startDate.localeCompare(b.startDate));
    case 'priority':
      return (a, b) => {
        const left = rank(PRIORITY_ORDER.indexOf(a.priority));
        const right = rank(PRIORITY_ORDER.indexOf(b.priority));
        return left === null || right === null ? null : left - right;
      };
    case 'state':
      return (a, b) => CATEGORY_ORDER.indexOf(a.stateCategory) - CATEGORY_ORDER.indexOf(b.stateCategory) || a.state.localeCompare(b.state);
    case 'title':
      return (a, b) => a.title.localeCompare(b.title);
    case 'created_at':
      return (a, b) => a.createdAt.localeCompare(b.createdAt);
    case 'updated_at':
      return (a, b) => a.updatedAt.localeCompare(b.updatedAt);
    default:
      return () => 0;
  }
}

function isEmpty(item: Item, field: string): boolean {
  if (field === 'due_date') return item.dueDate === null;
  if (field === 'start_date') return item.startDate === null;
  return field === 'priority' && item.priority === 'none';
}

function sortItems(items: readonly Item[], sort: string | null): readonly Item[] {
  const [field = 'number', direction = 'asc'] = (sort ?? 'number:asc').split(':');
  const compare = compareBy(field);
  const sign = direction === 'desc' ? -1 : 1;
  const tieBreak = (a: Item, b: Item) => a.projectKey.localeCompare(b.projectKey) || a.number - b.number;
  return items.toSorted((a, b) => {
    const emptyA = isEmpty(a, field);
    const emptyB = isEmpty(b, field);
    if (emptyA !== emptyB) return emptyA ? 1 : -1;
    const result = emptyA ? 0 : (compare(a, b) ?? 0) * sign;
    return result || tieBreak(a, b);
  });
}

/** 一頁的結果：篩選、排序、照 limit 切，cursor 是 `offset-N`。 */
export function filterAcrossProjects(items: readonly Item[], url: URL, me: string): ItemPage {
  const params = url.searchParams;
  const limit = Number(params.get('limit') ?? DEFAULT_LIMIT);
  const offset = Number((params.get('cursor') ?? 'offset-0').replace('offset-', ''));
  const sorted = sortItems(
    items.filter((item) => matches(item, params, me)),
    params.get('sort'),
  );
  const page = sorted.slice(offset, offset + limit);
  const nextCursor = offset + limit < sorted.length ? `offset-${String(offset + limit)}` : null;
  return { items: page, nextCursor };
}

/** 假的 GET /v1/items：回傳收到的每一個請求的網址，測試可以檢查查詢參數。me 是登入的人（assignee=me）。 */
export function serveCrossProjectItems(items: readonly Item[], me: string): URL[] {
  const requests: URL[] = [];
  server.use(
    http.get('/v1/items', ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      return ok(filterAcrossProjects(items, url, me));
    }),
  );
  return requests;
}

/** 專案內容裡的類型依 key 合併：和後端的 GET /v1/item-types 同一個形狀。 */
export function itemTypesOf(details: readonly ProjectDetail[]): readonly ItemTypeSummary[] {
  const byKey = new Map<string, { names: string[]; projectKeys: string[] }>();
  for (const detail of details.toSorted((a, b) => a.project.key.localeCompare(b.project.key))) {
    for (const itemType of detail.blueprint.itemTypes) {
      const entry = byKey.get(itemType.key) ?? { names: [], projectKeys: [] };
      if (!entry.names.includes(itemType.name)) entry.names.push(itemType.name);
      entry.projectKeys.push(detail.project.key);
      byKey.set(itemType.key, entry);
    }
  }
  return [...byKey.entries()].toSorted(([a], [b]) => a.localeCompare(b)).map(([key, entry]) => ({ key, ...entry }));
}

/** 假的 GET /v1/item-types。 */
export function serveItemTypes(types: readonly ItemTypeSummary[]): void {
  server.use(http.get('/v1/item-types', () => ok(types)));
}
