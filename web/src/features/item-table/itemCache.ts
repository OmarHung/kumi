import type { InfiniteData, QueryClient, QueryKey } from '@tanstack/react-query';
import { noteOwnWrites } from '../live/recentWrites';
import { BOARD_COLUMN, DATE_VIEW, queryKeys } from '../../api/queryKeys';
import type { Item, ItemDetail, ItemPage, MyWork } from '../../api/types';
import { acceptsStateAndType, conditionsOf, dependsOnServer, touchesConditions, type ListConditions } from './listConditions';

type ItemPages = InfiniteData<ItemPage, string | null>;

const NO_ITEMS: readonly Item[] = [];

/**
 * 清單的每一頁接成一份。快速新增先把新的項目放進已經載入的頁，之後載入的頁可能又帶到它；
 * 以編號去重，留最後出現的那一個：後面那頁是伺服器照條件與排序排好的位置。
 */
export function flattenItemPages(data: { readonly pages: readonly ItemPage[] } | undefined): readonly Item[] {
  if (data === undefined) {
    return NO_ITEMS;
  }
  const all = data.pages.flatMap((page) => page.items);
  const lastIndex = new Map(all.map((item, index) => [item.key, index]));
  return all.filter((item, index) => lastIndex.get(item.key) === index);
}

function mapPages(data: ItemPages, update: (items: readonly Item[]) => readonly Item[]): ItemPages {
  return { ...data, pages: data.pages.map((page) => ({ ...page, items: update(page.items) })) };
}

function findIn(data: ItemPages | undefined, itemKey: string): Item | undefined {
  return data?.pages.flatMap((page) => page.items).find((candidate) => candidate.key === itemKey);
}

function replaceIn(data: ItemPages, item: Item): ItemPages {
  return mapPages(data, (items) => items.map((candidate) => (candidate.key === item.key ? item : candidate)));
}

function withoutItem(data: ItemPages, itemKey: string): ItemPages {
  return mapPages(data, (items) => items.filter((candidate) => candidate.key !== itemKey));
}

function hasNextPage(data: ItemPages): boolean {
  return (data.pages.at(-1)?.nextCursor ?? null) !== null;
}

/**
 * 照編號放進清單：放在最後一個編號比它小的後面。看板照編號遞增排；
 * 快速新增的項目（編號最大）先放在最上面，也不會把別的項目擠到它前面。
 */
export function insertByNumber(items: readonly Item[], item: Item): readonly Item[] {
  const index = items.findLastIndex((candidate) => candidate.number < item.number);
  return [...items.slice(0, index + 1), item, ...items.slice(index + 1)];
}

/** 已經載入的頁裡，該放在哪一頁：最後一個有比它小的編號的那一頁；都沒有就是第一頁。 */
function insertIntoPages(data: ItemPages, item: Item): ItemPages {
  if (data.pages.length === 0) {
    return data;
  }
  const found = data.pages.findLastIndex((page) => page.items.some((candidate) => candidate.number < item.number));
  const target = Math.max(found, 0);
  return {
    ...data,
    pages: data.pages.map((page, index) => (index === target ? { ...page, items: insertByNumber(page.items, item) } : page)),
  };
}

/** 照編號排、還有下一頁的欄：已載入的到最後一頁的最後一個為止，編號更大的還在伺服器上。 */
function isWithinLoaded(data: ItemPages, item: Item): boolean {
  const last = data.pages.at(-1)?.items.at(-1);
  return last !== undefined && item.number < last.number;
}

/** 一份清單要怎麼跟著改：新的頁，以及要不要重抓。 */
interface ListUpdate {
  readonly data: ItemPages;
  readonly shouldRefetch: boolean;
}

function keep(data: ItemPages): ListUpdate {
  return { data, shouldRefetch: false };
}

/**
 * 看板的一欄。後端的游標是位移量：還有下一頁的欄多一張或少一張，「載入更多」就會重複或漏掉，所以要重抓；
 * 先改快取再重抓，成功時卡片不會閃回原欄。條件有負責人或搜尋時不自己判斷要不要放進來，交給重抓。
 * previous 是修改前快取裡的樣子：狀態、類型與條件看的欄位都沒變的修改，只在原位換掉。
 */
function updateColumn(data: ItemPages, conditions: ListConditions, item: Item, previous: Item | undefined): ListUpdate {
  const current = findIn(data, item.key);
  const hasMore = hasNextPage(data);
  const fits = acceptsStateAndType(conditions, item);
  if (current !== undefined) {
    if (!fits) return { data: withoutItem(data, item.key), shouldRefetch: hasMore };
    return { data: replaceIn(data, item), shouldRefetch: dependsOnServer(conditions) && touchesConditions(conditions, current, item) };
  }
  const isUnchanged = previous !== undefined && !touchesConditions(conditions, previous, item);
  if (!fits || isUnchanged) return keep(data);
  if (dependsOnServer(conditions)) return { data, shouldRefetch: true };
  // 編號超過已載入的範圍：還沒載入的較小編號該排在它前面，放進來就排錯了。
  if (hasMore && !isWithinLoaded(data, item)) return { data, shouldRefetch: true };
  return { data: insertIntoPages(data, item), shouldRefetch: hasMore };
}

/**
 * 表格與下層項目的清單：在原位換掉（使用者正在看的那一列不會突然不見）；下層項目的清單裡上層換掉的就拿掉。
 * 還有下一頁、而且條件或排序看的欄位變了，已載入的範圍可能少一個或換位置，重抓才不會在「載入更多」時漏掉。
 */
function updateList(data: ItemPages, conditions: ListConditions, item: Item): ListUpdate {
  const current = findIn(data, item.key);
  if (current === undefined) return keep(data);
  const shouldRefetch = hasNextPage(data) && touchesConditions(conditions, current, item);
  const belongs = conditions.parent === undefined || conditions.parent === item.parentKey;
  return { data: belongs ? replaceIn(data, item) : withoutItem(data, item.key), shouldRefetch };
}

/**
 * 行事曆與時間軸的清單：項目換進已經載入的位置（標題、優先度這類畫面上看得到的），
 * 但只要動到清單的條件（日期、狀態、負責人⋯）就整份重抓，不在原位保留：這兩種版面不需要，
 * 項目也可能原本不在這段時間裡（補上日期、改了開始日），原位換掉看不到它。
 * previous 是修改前快取裡的樣子；找不到時不知道有沒有動到條件，一律重抓。
 */
function updateDateView(data: ItemPages, conditions: ListConditions, item: Item, previous: Item | undefined): ListUpdate {
  const shouldRefetch = previous === undefined || touchesConditions(conditions, previous, item);
  return { data: findIn(data, item.key) === undefined ? data : replaceIn(data, item), shouldRefetch };
}

/** 修改前快取裡最新的那一份（各份清單讀回來的時間不同，看 updatedAt）。 */
function latestCopy(queryClient: QueryClient, lists: readonly [QueryKey, ItemPages | undefined][], itemKey: string): Item | undefined {
  const single = queryClient.getQueryData<ItemDetail>(queryKeys.item(itemKey));
  const copies = [...lists.flatMap(([, data]) => findIn(data, itemKey) ?? []), ...(single === undefined ? [] : [single])];
  return copies.reduce<Item | undefined>(
    (latest, copy) => (latest === undefined || Date.parse(copy.updatedAt) > Date.parse(latest.updatedAt) ? copy : latest),
    undefined,
  );
}

/** 項目以外跟著它變的資料：我的工作（負責人、狀態）、上層項目的選項（標題）。只標成過期，用到時才重抓。 */
function markRelatedStale(queryClient: QueryClient, projectKey: string): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork, refetchType: 'none' });
  void queryClient.invalidateQueries({ queryKey: queryKeys.parentCandidates(projectKey) });
}

function updateOf(queryKey: QueryKey, data: ItemPages, conditions: ListConditions, item: Item, previous: Item | undefined): ListUpdate {
  if (queryKey[4] === BOARD_COLUMN) return updateColumn(data, conditions, item, previous);
  if (queryKey[4] === DATE_VIEW) return updateDateView(data, conditions, item, previous);
  return updateList(data, conditions, item);
}

/**
 * 改好的項目換進這個專案每一份已經載入的清單；回傳要重抓的那幾份（位移量游標的頁界被打亂時，見 updateColumn、updateList）。
 * 批次修改（isBulk）時，清單的條件依賴伺服器（負責人、日期、搜尋⋯）而且這一筆動到了條件，也整份重抓：
 * 一次改很多筆，各筆該不該留在這份清單裡不逐筆猜。
 */
function applyToLists(queryClient: QueryClient, item: Item, isBulk: boolean): QueryKey[] {
  const lists = queryClient.getQueriesData<ItemPages>({ queryKey: queryKeys.items(item.projectKey) });
  const myWorkLists = queryClient.getQueriesData<ItemPages>({ queryKey: queryKeys.myWorkLists });
  const previous = latestCopy(queryClient, [...lists, ...myWorkLists], item.key);
  const refetch: QueryKey[] = applyToMyWorkDateViews(queryClient, item, myWorkLists, previous);
  for (const [queryKey, data] of lists) {
    if (data === undefined) continue;
    const conditions = conditionsOf(queryKey);
    const update = updateOf(queryKey, data, conditions, item, previous);
    // 先寫快取再標成要重抓：setQueryData 會清掉「已失效」的標記。
    if (update.data !== data) queryClient.setQueryData<ItemPages>(queryKey, update.data);
    const isServerSide = isBulk && previous !== undefined && dependsOnServer(conditions) && touchesConditions(conditions, previous, item);
    if (update.shouldRefetch || isServerSide) refetch.push(queryKey);
  }
  if (item.parentKey !== null) {
    const siblings = queryKeys.items(item.projectKey, { parent: item.parentKey });
    void queryClient.invalidateQueries({ queryKey: siblings, refetchType: 'none' });
  }
  return refetch;
}

/**
 * 「我的工作」的清單（['my-work', 'items', 條件, 標記?]）：鍵比專案的少一段，標記在第 3 段、條件在第 2 段。
 * 只有行事曆與時間軸要跟著改：它們動到條件就整份重抓。表格與看板靠頁面記下的那一份在原位更新，
 * 重抓會讓剛完成的列跳走（8.3「我的工作」：停在原位）。
 */
function applyToMyWorkDateViews(
  queryClient: QueryClient,
  item: Item,
  lists: readonly [QueryKey, ItemPages | undefined][],
  previous: Item | undefined,
): QueryKey[] {
  const refetch: QueryKey[] = [];
  for (const [queryKey, data] of lists) {
    if (data === undefined || queryKey[3] !== DATE_VIEW) continue;
    const update = updateDateView(data, conditionsOf(queryKey, 2), item, previous);
    if (update.data !== data) queryClient.setQueryData<ItemPages>(queryKey, update.data);
    if (update.shouldRefetch) refetch.push(queryKey);
  }
  return refetch;
}

function refetchLists(queryClient: QueryClient, keys: readonly QueryKey[]): void {
  const unique = new Map(keys.map((key) => [JSON.stringify(key), key]));
  unique.forEach((queryKey) => {
    void queryClient.invalidateQueries({ queryKey, exact: true });
  });
}

/**
 * 改好的項目換進這個專案每一份已經載入的清單，不必重抓整頁；看板的欄則是從舊的欄搬到新的欄。
 * 也寫進項目頁用的單一項目，並讓它的活動重抓（每次修改都會多一筆）。下層項目的清單裡，上層換掉的就拿掉；
 * 新的上層那份清單不知道它該排在哪裡，標成過期，下次看的時候重抓。
 */
export function replaceItemInCache(queryClient: QueryClient, item: ItemDetail): void {
  noteOwnWrites(queryClient, [item.key]);
  refetchLists(queryClient, applyToLists(queryClient, item, false));
  queryClient.setQueryData(queryKeys.item(item.key), item);
  void queryClient.invalidateQueries({ queryKey: queryKeys.activity(item.key) });
  markRelatedStale(queryClient, item.projectKey);
}

/**
 * 批次改好的項目（清單的形狀，沒有說明）：和 replaceItemInCache 一樣在原位換掉，列不會因為排序跳走；
 * 單一項目的快取有的話只換掉清單帶得到的欄位，說明留著；沒有的不補（補了就少了說明）。
 */
export function replaceListedItemsInCache(queryClient: QueryClient, projectKey: string, items: readonly Item[]): void {
  noteOwnWrites(queryClient, items.map((item) => item.key));
  const refetch = items.flatMap((item) => {
    const keys = applyToLists(queryClient, item, true);
    queryClient.setQueryData<ItemDetail | undefined>(queryKeys.item(item.key), (single) =>
      single === undefined ? undefined : { ...single, ...item },
    );
    void queryClient.invalidateQueries({ queryKey: queryKeys.activity(item.key) });
    return keys;
  });
  refetchLists(queryClient, refetch);
  markRelatedStale(queryClient, projectKey);
}

export type ListPosition = 'first' | 'last';

function insertItem(data: ItemPages, item: Item, position: ListPosition): ItemPages {
  const pages = data.pages.map((page) => ({ ...page, items: page.items.filter((candidate) => candidate.key !== item.key) }));
  const index = position === 'first' ? 0 : pages.length - 1;
  return {
    ...data,
    pages: pages.map((page, pageIndex) =>
      pageIndex === index ? { ...page, items: position === 'first' ? [item, ...page.items] : [...page.items, item] } : page,
    ),
  };
}

/**
 * 新增的項目放進目前這份清單：表格放在最上面，使用者馬上看得到剛建立的那一個；
 * 下層項目照編號排，新的編號最大，放在最後面。
 * 其他條件的清單不知道它該排在哪裡，標成過期，下次看的時候重抓。
 */
export function addItemToCache(
  queryClient: QueryClient,
  item: ItemDetail,
  currentKey: readonly unknown[],
  position: ListPosition = 'first',
): void {
  noteOwnWrites(queryClient, [item.key]);
  void queryClient.invalidateQueries({ queryKey: queryKeys.items(item.projectKey), refetchType: 'none' });
  queryClient.setQueryData<ItemPages>(currentKey, (data) =>
    data === undefined || data.pages.length === 0 ? data : insertItem(data, item, position),
  );
  // 剛建立的項目點進去就有內容，不必再讀一次。
  queryClient.setQueryData(queryKeys.item(item.key), item);
  markRelatedStale(queryClient, item.projectKey);
}

/**
 * 刪除的項目從這個專案每一份已經載入的清單（表格、看板的欄、下層清單）與我的工作（側欄的數量，以及四種版面的清單）拿掉，再全部標成過期：
 * 位移量游標的頁界少了一個，下次看的時候重抓。先拿掉再重抓，回到專案頁時不會閃一下又出現。
 * 項目頁正在看的就是它，這裡不重抓（重抓只會是 404）；單一項目的快取等離開項目頁之後再丟（forgetItem）。
 */
export function removeItemFromCache(queryClient: QueryClient, projectKey: string, itemKey: string): void {
  noteOwnWrites(queryClient, [itemKey]);
  const lists = [
    ...queryClient.getQueriesData<ItemPages>({ queryKey: queryKeys.items(projectKey) }),
    ...queryClient.getQueriesData<ItemPages>({ queryKey: queryKeys.myWorkLists }),
  ];
  for (const [queryKey, data] of lists) {
    if (data !== undefined && findIn(data, itemKey) !== undefined) {
      queryClient.setQueryData<ItemPages>(queryKey, withoutItem(data, itemKey));
    }
  }
  queryClient.setQueryData<MyWork>(queryKeys.myWork, (data) =>
    data === undefined ? data : { ...data, items: data.items.filter((candidate) => candidate.key !== itemKey) },
  );
  void queryClient.invalidateQueries({ queryKey: queryKeys.items(projectKey), refetchType: 'none' });
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork, refetchType: 'none' });
  void queryClient.invalidateQueries({ queryKey: queryKeys.parentCandidates(projectKey), refetchType: 'none' });
  void queryClient.invalidateQueries({ queryKey: queryKeys.deletedItems(projectKey) });
}

/** 丟掉單一項目與它的活動（同一個前綴）。 */
export function forgetItem(queryClient: QueryClient, itemKey: string): void {
  queryClient.removeQueries({ queryKey: queryKeys.item(itemKey) });
}

/** 批次刪除成功的那幾筆：從清單與我的工作拿掉（同 removeItemFromCache），單一項目的快取也丟掉。 */
export function removeItemsFromCache(queryClient: QueryClient, projectKey: string, itemKeys: readonly string[]): void {
  itemKeys.forEach((itemKey) => {
    removeItemFromCache(queryClient, projectKey, itemKey);
    forgetItem(queryClient, itemKey);
  });
}

/**
 * 復原之後（項目回到清單、已刪除的項目頁少一筆）：專案的項目清單、已刪除的清單、我的工作與上層的選項都重抓。
 * 回來的項目該排在清單的哪裡只有伺服器知道。
 */
export function refreshAfterRestore(queryClient: QueryClient, projectKey: string, itemKeys: readonly string[]): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.items(projectKey) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.deletedItems(projectKey) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
  void queryClient.invalidateQueries({ queryKey: queryKeys.parentCandidates(projectKey) });
  itemKeys.forEach((itemKey) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.item(itemKey) });
  });
}
