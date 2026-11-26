/**
 * 別處（對話裡的新專案建議卡）請浮動面板切到某個專案並打開。
 * 面板的狀態在 AppShell 層、卡片在對話深處，中間沒有共用的父層，用瀏覽器的事件傳話就夠了（沒有面板時沒人聽，什麼都不發生）。
 */
export const OPEN_ASSISTANT_PANEL_EVENT = 'kumi:open-assistant-panel';

export interface OpenAssistantPanelDetail {
  readonly projectKey: string;
}

export function requestAssistantPanel(projectKey: string): void {
  window.dispatchEvent(new CustomEvent<OpenAssistantPanelDetail>(OPEN_ASSISTANT_PANEL_EVENT, { detail: { projectKey } }));
}
