import type { InfiniteData } from '@tanstack/react-query';
import { getItemActivity } from './activity';
import type { ActivityPage, ActivityQuery } from './types';

/** 和後端的預設一樣；一頁 50 筆，往後（較新的）用「載入更多」。 */
export const ACTIVITY_PAGE_SIZE = 50;

/** 快取裡的時間軸：一頁一頁，游標是 null（第一頁）或上一頁給的 nextCursor。 */
export type ActivityPages = InfiniteData<ActivityPage, string | null>;

/** 時間軸看哪一份：只有留言與回覆（`comments`），或全部。 */
export type ActivityView = 'comments' | 'all';

/** 「留言」是後端的 `kind=commented`（回覆也是 commented）；「全部」不送，由資料庫篩過再分頁。 */
function kindOf(view: ActivityView): Pick<ActivityQuery, 'kind'> {
  return view === 'comments' ? { kind: 'commented' } : {};
}

function fetchPage(itemKey: string, view: ActivityView, cursor: string | null, signal: AbortSignal): Promise<ActivityPage> {
  const base = { limit: ACTIVITY_PAGE_SIZE, ...kindOf(view) };
  return getItemActivity(itemKey, cursor === null ? base : { ...base, cursor }, signal);
}

/**
 * 正在讀的是不是快取裡原本的最後一頁（重抓時）：游標以鍵為準，活動只會往後接，
 * 重抓時前面每一頁的位置都不變，最後一頁的游標也和原本一樣。讀下一頁時游標是新的，不算。
 */
function previousLastPage(cached: ActivityPages | undefined, cursor: string | null): ActivityPage | undefined {
  if (cached === undefined || cached.pageParams.length === 0 || cached.pageParams.at(-1) !== cursor) {
    return undefined;
  }
  return cached.pages.at(-1);
}

/** 原本沒讀到最新時，至少要讀到原本那一頁的每一筆（包括剛送出、先接上去的留言）。 */
function coversPrevious(page: ActivityPage, previous: ActivityPage): boolean {
  if (previous.nextCursor === null) {
    return false;
  }
  const ids = new Set(page.items.map((entry) => entry.id));
  return previous.items.every((entry) => ids.has(entry.id));
}

async function extendPage(
  itemKey: string,
  view: ActivityView,
  page: ActivityPage,
  previous: ActivityPage,
  signal: AbortSignal,
): Promise<ActivityPage> {
  if (page.nextCursor === null || coversPrevious(page, previous)) {
    return page;
  }
  const next = await fetchPage(itemKey, view, page.nextCursor, signal);
  const merged = { items: [...page.items, ...next.items], nextCursor: next.nextCursor };
  return extendPage(itemKey, view, merged, previous, signal);
}

/**
 * 讀時間軸的一頁。重抓最後一頁時往後接著讀，讀到原本的範圍為止：
 * 原本已經讀到最新的，就讀到最新（剛做的修改、跨過頁界的新活動都看得到，不會藏在「載入更多」後面）；
 * 原本沒讀到最新，但最後一頁有伺服器那一頁沒有的（剛送出的留言），就讀到它出現。
 * 重抓只重抓已經載入的頁數，不這樣做的話，畫面上原本有的會因為重抓而不見。
 */
export async function fetchActivityPage(
  itemKey: string,
  view: ActivityView,
  cursor: string | null,
  signal: AbortSignal,
  cached: ActivityPages | undefined,
): Promise<ActivityPage> {
  const page = await fetchPage(itemKey, view, cursor, signal);
  const previous = previousLastPage(cached, cursor);
  return previous === undefined ? page : extendPage(itemKey, view, page, previous, signal);
}
