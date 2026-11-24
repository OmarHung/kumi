import { hashKey, useQueryClient, type QueryCacheNotifyEvent } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { queryKeys } from '../../api/queryKeys';
import type { Item, ItemDetail } from '../../api/types';
import { isTerminal } from '../../domain/dates';

const NO_ITEMS: readonly Item[] = [];

/**
 * 分組與排序看的欄位（myWorkGroups.ts、myWorkSort.ts）：「放在哪裡」。這一頁看過或改過的項目用第一次記下時清單裡的樣子，
 * 停在原組原位——補填到期日之後列不會換到別組的表格（換組會連同格子與補填的對話框一起卸下），改了優先度也不會在組裡跳走。
 */
export type RowPlacement = Pick<Item, 'key' | 'projectKey' | 'number' | 'dueDate' | 'title' | 'state' | 'stateCategory' | 'priority'>;

/** 畫面上的一列：放在哪裡（RowPlacement），加上要顯示的最新那一份（item）。 */
export interface MyWorkRow extends RowPlacement {
  readonly item: Item;
}

/** 這次看的期間記下的：每個項目最新的一份，以及它第一次被記下時在清單裡的樣子（放在哪裡）。 */
export interface RecordedCopies {
  readonly copies: ReadonlyMap<string, Item>;
  readonly placements: ReadonlyMap<string, Item>;
}

const NOTHING_RECORDED: RecordedCopies = { copies: new Map(), placements: new Map() };

/**
 * 單一項目快取（queryKeys.item）裡的那一份不比清單的舊：在這一列的狀態格、項目面板或別的畫面改過。
 * 一樣新的也用它（內容相同；時間戳記精度不夠時才不會漏掉剛改的）。
 */
function isNotOlder(copy: Item, listed: Item): boolean {
  return Date.parse(copy.updatedAt) >= Date.parse(listed.updatedAt);
}

/** 要顯示的那一份放在 placement 記下的位置。 */
export function rowOf(item: Item, placement: Item): MyWorkRow {
  const { key, projectKey, number, dueDate, title, state, stateCategory, priority } = placement;
  return { key, projectKey, number, dueDate, title, state, stateCategory, priority, item };
}

/** 清單裡的項目換上不比它舊的那一份（側欄的數量也用：只算伺服器給的，加上剛改的狀態）。 */
export function latestItems(listed: readonly Item[], copies: ReadonlyMap<string, Item>): readonly Item[] {
  return listed.map((item) => {
    const copy = copies.get(item.key);
    return copy !== undefined && isNotOlder(copy, item) ? copy : item;
  });
}

/**
 * 畫面上的列：伺服器的清單換上比較新的那一份，放在第一次記下時的位置；加上這次看的期間改成完成或取消、
 * 伺服器重抓之後已經不給的那幾個（留在原位，docs/DESIGN.md 8.2）。伺服器的清單只有還沒結束的項目，
 * 所以畫面上結束了的列一定是剛改的。
 */
export function mergeRows(listed: readonly Item[], recorded: RecordedCopies): readonly MyWorkRow[] {
  const { copies, placements } = recorded;
  const listedKeys = new Set(listed.map((item) => item.key));
  const current = latestItems(listed, copies).map((item, index) => rowOf(item, placements.get(item.key) ?? listed[index] ?? item));
  const settled = [...copies.values()]
    .filter((copy) => !listedKeys.has(copy.key) && isTerminal(copy.stateCategory))
    .map((copy) => rowOf(copy, placements.get(copy.key) ?? copy));
  return [...current, ...settled];
}

/**
 * 快取的事件是不是看著的項目寫進了新的一份（讀回來或改好之後 setQueryData，都是 success）。
 * 用查詢鍵的雜湊比對：單一項目是 ['item', 編號]，活動（['item', 編號, 'activity']）的雜湊不同，不算。
 */
function updatedItemKey(event: QueryCacheNotifyEvent, watched: ReadonlyMap<string, string>): string | null {
  if (event.type !== 'updated' || event.action.type !== 'success') return null;
  return watched.get(event.query.queryHash) ?? null;
}

/** 記下新的一份；第一次記下時把清單裡的樣子當成它的位置（之後重抓、改了到期日都不搬）。 */
function record(previous: RecordedCopies, copy: Item, listed: Item | undefined): RecordedCopies {
  const placement = previous.placements.get(copy.key) ?? listed ?? copy;
  return {
    copies: new Map(previous.copies).set(copy.key, copy),
    placements: previous.placements.has(copy.key) ? previous.placements : new Map(previous.placements).set(copy.key, placement),
  };
}

/**
 * 這次看的期間，這些項目在單一項目快取裡寫進的每一份（以編號為鍵）與第一次記下時的位置。改好的項目只寫進單一項目與
 * 專案的清單（features/item-table/itemCache.ts），我的工作的快取只標成過期、不改內容：清單要自己接上新的那一份，
 * 不必重抓。記在元件裡：重新整理或離開再回來就重新開始。
 * 看著的編號包含已經記下的：重抓之後伺服器不再給的（剛完成的），改回還沒結束的狀態時照樣接得到。
 */
export function useRecordedCopies(listed: readonly Item[], resetKey = ''): RecordedCopies {
  const queryClient = useQueryClient();
  const [state, setState] = useState({ key: resetKey, recorded: NOTHING_RECORDED });
  // 條件換了（resetKey 不同）就重新開始：原本的列是在原本的條件下記的，留著會出現不符合新條件的列。
  const recorded = state.key === resetKey ? state.recorded : NOTHING_RECORDED;
  // 用字串比對內容：每次渲染給的都是新的陣列，編號沒變就不必重新訂閱。
  const signature = [...new Set([...listed.map((item) => item.key), ...recorded.copies.keys()])].join('\n');
  useEffect(() => {
    const watched = new Map(signature.split('\n').map((key) => [hashKey(queryKeys.item(key)), key] as const));
    const listedByKey = new Map(listed.map((item) => [item.key, item]));
    return queryClient.getQueryCache().subscribe((event) => {
      const key = updatedItemKey(event, watched);
      const copy = key === null ? undefined : queryClient.getQueryData<ItemDetail>(queryKeys.item(key));
      if (copy === undefined) return;
      setState((previous) => ({
        key: resetKey,
        recorded: record(previous.key === resetKey ? previous.recorded : NOTHING_RECORDED, copy, listedByKey.get(copy.key)),
      }));
    });
  }, [queryClient, signature, listed, resetKey]);
  return recorded;
}

/**
 * 我的工作畫面上的列（還沒排序、還沒分組）：伺服器的清單接上剛改好的那一份；看過、改過的停在原位，
 * 剛完成、取消的留著直到離開這一頁，或條件換了（resetKey 不同：重新開始記）。工具列上那一句在 myWorkStatus.ts。
 */
export function useMyWorkRows(listed: readonly Item[] | undefined, resetKey = ''): readonly MyWorkRow[] {
  const items = listed ?? NO_ITEMS;
  const recorded = useRecordedCopies(items, resetKey);
  return mergeRows(items, recorded);
}
