import { useProjectSearchQuery } from '../../api/queries';
import type { ProjectSearchHit } from '../../api/types';
import type { ItemSearchStatus } from './useItemSearch';
import { useSettledText } from './useSettledText';

/** 伺服器的 limit：說明比對到的只是補在即時比對的後面，組內總共才 PROJECT_LIMIT 個。 */
export const PROJECT_SEARCH_LIMIT = 8;

export interface ProjectSearch {
  readonly status: ItemSearchStatus;
  /** 只有 done 時有東西；換字之後舊的結果不留著（和輸入的字對不上）。 */
  readonly hits: readonly ProjectSearchHit[];
  /** 打字停下來了（和項目搜尋同一個時間點）。 */
  readonly isSettled: boolean;
}

/**
 * 用專案搜尋（GET /v1/search/projects）找說明裡的字：主機名稱、IP、網址。
 * 和項目搜尋一樣停下來 300ms 才送；代號與名稱的比對不靠它（前端即時比對），所以失敗時專案組照樣有結果。
 */
export function useProjectSearch(query: string): ProjectSearch {
  const text = query.trim();
  const settled = useSettledText(text);
  const result = useProjectSearchQuery(settled, PROJECT_SEARCH_LIMIT);

  const isSettled = settled === text;
  if (text === '') return { status: 'off', hits: [], isSettled };
  if (!isSettled || result.isPending) return { status: 'pending', hits: [], isSettled };
  if (result.isError) return { status: 'error', hits: [], isSettled };
  return { status: 'done', hits: result.data, isSettled };
}
