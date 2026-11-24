import { useItemSearchQuery } from '../../api/queries';
import type { Item } from '../../api/types';
import { useSettledText } from './useSettledText';

/** 一次最多列出這麼多個搜尋結果（伺服器的 limit）：快速跳轉是找一個，不是瀏覽清單。 */
export const ITEM_SEARCH_LIMIT = 8;

/** off：沒有輸入；pending：還在打字或等伺服器；done、error：有結果了。 */
export type ItemSearchStatus = 'off' | 'pending' | 'done' | 'error';

export interface ItemSearch {
  readonly status: ItemSearchStatus;
  /** 只有 done 時有東西；換字之後舊的結果不留著（和輸入的字對不上）。 */
  readonly items: readonly Item[];
  /** 打字停下來了（和送出搜尋同一個時間點）：說明結果的 status 這時才換，不和字元的回音搶著唸。 */
  readonly isSettled: boolean;
}

/**
 * 用跨專案搜尋（GET /v1/search/items）找標題或編號：不在專案裡也搜，在專案裡也搜全部。
 * 順序照伺服器的，不在這裡把目前專案的提前：伺服器的 limit 先砍過一次，前端只重排拿到的 8 筆，
 * 救不回被砍掉的目前專案項目，卻會打亂編號完全符合、標題開頭這些伺服器排好的相關度；
 * 項目編號本身帶專案代號，jumpGroups 再對別的專案加專案名稱，認得出來就夠。
 */
export function useItemSearch(query: string): ItemSearch {
  const text = query.trim();
  const settled = useSettledText(text);
  const result = useItemSearchQuery(settled, ITEM_SEARCH_LIMIT);

  const isSettled = settled === text;
  if (text === '') return { status: 'off', items: [], isSettled };
  if (!isSettled || result.isPending) return { status: 'pending', items: [], isSettled };
  if (result.isError) return { status: 'error', items: [], isSettled };
  return { status: 'done', items: result.data, isSettled };
}
