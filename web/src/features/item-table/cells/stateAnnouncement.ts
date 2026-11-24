import { createContext, use } from 'react';
import type { Item } from '../../../api/types';

/** 狀態格補填完成時要唸的結果：說法，以及移好之後的項目（頁面拿它判斷要不要和自己的那一句合成）。 */
export interface StateAnnouncement {
  readonly item: Item;
  readonly message: string;
}

export type AnnounceState = (announcement: StateAnnouncement) => void;

/**
 * 頁面自己有一直掛著的 status、會為同一件事說話時提供（我的工作：「AGY-3 已經完成，重新整理後會離開清單。」）：
 * 狀態格把補填完成的那一句交給頁面合成一句唸，格子不自己掛 status——兩個 status 接連更新，報讀軟體會唸兩句。
 * 沒有提供時（專案的表格、項目頁）格子照舊自己唸。
 */
export const StateAnnouncementContext = createContext<AnnounceState | null>(null);

export function useStateAnnouncement(): AnnounceState | null {
  return use(StateAnnouncementContext);
}
