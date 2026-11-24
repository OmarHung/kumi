import { useSyncExternalStore } from 'react';

/**
 * 項目面板的樣子：在清單旁邊的側邊面板（side），或置中的浮動視窗（modal，背後的清單不能操作）。
 * 個人的顯示偏好，記在這個瀏覽器，專案的四種版面、我的工作、收件匣…所有開項目面板的地方共用（docs/design/08-interaction.md「項目面板」）。
 * 預設側邊面板（不讓原本的畫面突然改變）。寫法和 lib/weekStart.ts 一樣。
 */
export type ItemPanelMode = 'side' | 'modal';

export const ITEM_PANEL_MODES: readonly ItemPanelMode[] = ['side', 'modal'];

export const DEFAULT_ITEM_PANEL_MODE: ItemPanelMode = 'side';

export const ITEM_PANEL_MODE_STORAGE_KEY = 'shell.itemPanelMode';

function isItemPanelMode(value: unknown): value is ItemPanelMode {
  return ITEM_PANEL_MODES.some((mode) => mode === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成預設。 */
export function readItemPanelMode(): ItemPanelMode {
  try {
    const stored = localStorage.getItem(ITEM_PANEL_MODE_STORAGE_KEY);
    return isItemPanelMode(stored) ? stored : DEFAULT_ITEM_PANEL_MODE;
  } catch {
    return DEFAULT_ITEM_PANEL_MODE;
  }
}

// 只有寫不進去時（私密瀏覽）才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: ItemPanelMode | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

function currentItemPanelMode(): ItemPanelMode {
  return unsavedValue ?? readItemPanelMode();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setItemPanelMode(value: ItemPanelMode): void {
  try {
    localStorage.setItem(ITEM_PANEL_MODE_STORAGE_KEY, value);
    unsavedValue = null;
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
    unsavedValue = value;
  }
  notify();
}

/** 只給測試用：清掉寫不進 localStorage 時的記憶體暫存，不讓它漏到下一個測試。 */
export function resetItemPanelModeForTests(): void {
  unsavedValue = null;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key === ITEM_PANEL_MODE_STORAGE_KEY || event.key === null) onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useItemPanelMode(): ItemPanelMode {
  return useSyncExternalStore(subscribe, currentItemPanelMode, () => DEFAULT_ITEM_PANEL_MODE);
}
