import { createContext, use, useEffect } from 'react';
import type { ItemPanelValue } from './ItemPanelContext';

/**
 * 目前這一頁的項目面板（ItemPanelHost 登記）：登入後的外框裡的東西（新通知的提示）不在宿主底下、拿不到 ItemPanelContext，
 * 點了要像清單一樣在旁邊的面板開項目時，從這裡找。沒有宿主的頁面是 null。
 */
export interface ItemPanelRegistry {
  readonly current: () => ItemPanelValue | null;
  /** 登記；回傳取消的函式（只取消自己登記的，換頁時新頁面先登記、舊頁面後取消也不會被清掉）。 */
  readonly register: (panel: ItemPanelValue) => () => void;
}

export function createItemPanelRegistry(): ItemPanelRegistry {
  let registered: ItemPanelValue | null = null;
  return {
    current: () => registered,
    register: (panel) => {
      registered = panel;
      return () => {
        if (registered === panel) registered = null;
      };
    },
  };
}

export const ItemPanelRegistryContext = createContext<ItemPanelRegistry | null>(null);

/** 宿主呼叫：每次渲染都登記最新的面板（開、關、換項目的函式與目前開著的項目）。 */
export function useRegisterItemPanel(panel: ItemPanelValue): void {
  const registry = use(ItemPanelRegistryContext);
  useEffect(() => registry?.register(panel), [registry, panel]);
}
