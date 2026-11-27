import { useSyncExternalStore } from 'react';

/**
 * 底部分頁列（features/shell/TabBar.tsx）的「AI 助理」要開關浮動面板。面板的狀態在 AssistantPanel 裡，
 * 分頁列在外框的另一處、沒有共用的父層：用瀏覽器的事件請它開關（和 panelRequests.ts 同一個做法），
 * 面板開著沒有再用同一個小小的外部 store 公佈出來，分頁列才標得出「目前所在」。
 */
export const TOGGLE_ASSISTANT_PANEL_EVENT = 'kumi:toggle-assistant-panel';
export const CLOSE_ASSISTANT_PANEL_EVENT = 'kumi:close-assistant-panel';

/** 分頁列上「AI 助理」按鈕的 id：窄螢幕沒有右下角的按鈕，面板關上時焦點回到這裡。 */
export const ASSISTANT_TAB_ID = 'assistant-tab-button';

export interface CloseAssistantPanelDetail {
  /** 關上之後要不要把焦點交還給開面板的按鈕；點別的分頁換頁時不要（焦點跟著被點的分頁）。 */
  readonly restoreFocus: boolean;
}

export function toggleAssistantPanel(): void {
  window.dispatchEvent(new Event(TOGGLE_ASSISTANT_PANEL_EVENT));
}

export function closeAssistantPanel(options: CloseAssistantPanelDetail = { restoreFocus: true }): void {
  window.dispatchEvent(new CustomEvent<CloseAssistantPanelDetail>(CLOSE_ASSISTANT_PANEL_EVENT, { detail: options }));
}

let isShown = false;
const listeners = new Set<() => void>();

/** AssistantPanel 在面板開關時呼叫；卸下時要公佈 false，不留下上一個外框的狀態。 */
export function publishAssistantPanelShown(next: boolean): void {
  if (isShown === next) return;
  isShown = next;
  listeners.forEach((listener) => {
    listener();
  });
}

export function useIsAssistantPanelShown(): boolean {
  return useSyncExternalStore(
    (onChange) => {
      listeners.add(onChange);
      return () => {
        listeners.delete(onChange);
      };
    },
    () => isShown,
    () => false,
  );
}
