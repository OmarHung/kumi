import { useState } from 'react';
import { readStoredProject } from './panelMemory';

interface ProjectState {
  readonly chosen: string | null;
  /** 最近一次「跟過」的路由專案（沒有在專案頁是 undefined）。 */
  readonly followed: string | undefined;
}

/**
 * 面板現在對著哪個專案。在專案頁就跟著路由；不在專案頁維持上一次的（記在瀏覽器），使用者也可以自己選。
 * 這裡不記：確認讀得到那個專案才記（PanelContent 的 rememberProject），路由上不存在的代號不會被存下來。
 * 回覆中（isBusy）先不換：回覆是這個專案的，換掉會把進行中的對話晾在一邊；回覆完了再跟上目前的路由。
 * 在渲染時調整狀態（不必 effect 再跑一輪）：換專案的那一個畫面就已經是新的專案。
 */
export function useAssistantProject(
  routeProjectKey: string | undefined,
  isBusy: boolean,
): readonly [string | null, (projectKey: string) => void] {
  const [state, setState] = useState<ProjectState>(() => ({ chosen: routeProjectKey ?? readStoredProject(), followed: routeProjectKey }));
  if (!isBusy && routeProjectKey !== state.followed) {
    setState({ chosen: routeProjectKey ?? state.chosen, followed: routeProjectKey });
  }
  return [state.chosen, (projectKey) => {
    setState((current) => ({ ...current, chosen: projectKey }));
  }];
}
