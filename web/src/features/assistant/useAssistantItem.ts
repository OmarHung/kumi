import { useState } from 'react';
import { projectKeyOfItemKey } from '../work-items/itemLinks';

interface ItemState {
  readonly chosen: string | null;
  /** 最近一次「跟過」的路由項目（不管有沒有採用）：手動取消之後，路由沒換就不再帶回來。 */
  readonly followedRoute: string | null;
  readonly projectKey: string | null;
}

/** 路由上的項目只有屬於助理目前的專案時才採用（其他專案的項目助理當作不存在）。 */
function routeItemFor(routeItemKey: string | null, projectKey: string | null): string | null {
  return routeItemKey !== null && projectKey !== null && projectKeyOfItemKey(routeItemKey) === projectKey.toUpperCase() ? routeItemKey : null;
}

/**
 * 助理現在針對哪個項目。在項目頁或開著項目的側邊面板（routeItemKey）就自動帶那個項目，路由的項目換了跟著換、離開了就放掉；
 * 使用者也可以自己換成同專案的別的項目或取消，路由的項目沒變時不會被帶回來。
 * 回覆中（isBusy）先不跟：回覆完了再跟上目前的路由。換專案時重新來過（項目屬於專案）。
 * 在渲染時調整狀態（和 useAssistantProject 同一個做法）：換項目的那一個畫面就已經是新的。
 */
export function useAssistantItem(
  routeItemKey: string | null,
  projectKey: string | null,
  isBusy: boolean,
): readonly [string | null, (itemKey: string | null) => void] {
  const [state, setState] = useState<ItemState>(() => ({ chosen: routeItemFor(routeItemKey, projectKey), followedRoute: routeItemKey, projectKey }));
  if (!isBusy && (routeItemKey !== state.followedRoute || projectKey !== state.projectKey)) {
    setState({ chosen: routeItemFor(routeItemKey, projectKey), followedRoute: routeItemKey, projectKey });
  }
  return [
    state.chosen,
    (itemKey) => {
      setState((current) => ({ ...current, chosen: itemKey }));
    },
  ];
}
