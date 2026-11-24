import { http } from 'msw';
import type { Item, ItemPage } from '../api/types';
import { ok } from './api';
import type { ProjectBlueprint } from '../api/blueprint';
import { matchesFieldParams, sortByFieldParam } from './fieldQuery';
import { exampleOwner } from './fixtures';
import { server } from './server';

interface BoardServerOptions {
  /** 一頁幾個；沒給時照請求的 limit。 */
  readonly pageSize?: number;
  /** assignee=me 的「我」。 */
  readonly me?: string;
  /** 依單選欄位排序時，選項的順序用它的；沒給就依選項的 key。 */
  readonly blueprint?: ProjectBlueprint;
}

export interface BoardServer {
  /** 收到的每一個清單請求的網址。 */
  readonly requests: URL[];
  /** 某個狀態的欄收到的請求。 */
  readonly requestsFor: (state: string) => URL[];
  /** 換掉伺服器上的項目（例如改狀態成功後，重抓會看到新的狀態）。 */
  readonly set: (items: readonly Item[]) => void;
}

function matchesAssignee(item: Item, assignee: string | null, me: string): boolean {
  if (assignee === null) return true;
  if (assignee === 'unassigned') return item.assignee === null;
  return item.assignee?.personId === (assignee === 'me' ? me : assignee);
}

function matches(item: Item, params: URLSearchParams, me: string): boolean {
  const states = params.getAll('state');
  const types = params.getAll('type');
  const q = params.get('q');
  return (
    (states.length === 0 || states.includes(item.state)) &&
    (types.length === 0 || types.includes(item.type)) &&
    matchesAssignee(item, params.get('assignee'), me) &&
    matchesFieldParams(item, params, me) &&
    (q === null || item.title.includes(q) || item.key === q)
  );
}

/**
 * 假的項目清單，照查詢參數真的篩選與分頁：state、type 可以有好幾個，assignee、q、自訂欄位（test/fieldQuery.ts），照編號遞增（依自訂欄位排序時照 sort）；
 * 游標是位移量（page-50 從第 51 個開始），和後端一樣。看板每一欄各自讀，每一欄都要拿到自己的項目。
 */
export function serveItemsByQuery(projectKey: string, initial: readonly Item[], options: BoardServerOptions = {}): BoardServer {
  const requests: URL[] = [];
  let items = initial;
  const me = options.me ?? exampleOwner.personId;
  server.use(
    http.get(`/v1/projects/${projectKey}/items`, ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const limit = options.pageSize ?? Number(url.searchParams.get('limit') ?? '50');
      const offset = Number((url.searchParams.get('cursor') ?? 'page-0').replace('page-', ''));
      const found = sortByFieldParam(items.filter((item) => matches(item, url.searchParams, me)), url.searchParams.get('sort'), options.blueprint);
      const nextOffset = offset + limit;
      const page: ItemPage = {
        items: found.slice(offset, nextOffset),
        nextCursor: nextOffset < found.length ? `page-${String(nextOffset)}` : null,
      };
      return ok(page);
    }),
  );
  return {
    requests,
    requestsFor: (state) => requests.filter((url) => url.searchParams.getAll('state').includes(state)),
    set: (next) => {
      items = next;
    },
  };
}

export interface HeldTransition {
  /** 收到的內文（{ to }）。 */
  readonly bodies: unknown[];
  /** 讓後端回應。在這之前項目一直在「送出中」。 */
  readonly release: () => void;
}

/** 假的 POST /v1/items/{key}/transitions，先不回應：測試可以看送出中的樣子，再決定什麼時候回。 */
export function holdTransition(item: Item, respond: () => Response): HeldTransition {
  const bodies: unknown[] = [];
  let open: () => void = () => undefined;
  const opened = new Promise<void>((resolve) => {
    open = resolve;
  });
  server.use(
    http.post(`/v1/items/${item.key}/transitions`, async ({ request }) => {
      bodies.push(await request.json());
      await opened;
      return respond();
    }),
  );
  return {
    bodies,
    release: () => {
      open();
    },
  };
}
