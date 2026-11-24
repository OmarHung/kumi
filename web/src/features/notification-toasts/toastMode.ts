import { useSyncExternalStore } from 'react';

/**
 * 新通知的提示怎麼顯示：完整（含留言與提及的內容）、精簡（只有誰做了什麼、哪個項目）、不顯示（只有側欄的數字）。
 * 個人的顯示偏好，記在這個瀏覽器（要隱藏內容的通常是某一台電腦：會議室、投影，不是整個帳號）。
 * 寫法和 lib/itemPanelMode.ts 一樣。
 */
export type ToastMode = 'full' | 'compact' | 'off';

export const TOAST_MODES: readonly ToastMode[] = ['full', 'compact', 'off'];

export const DEFAULT_TOAST_MODE: ToastMode = 'full';

export const TOAST_MODE_STORAGE_KEY = 'notifications.toastMode';

function isToastMode(value: unknown): value is ToastMode {
  return TOAST_MODES.some((mode) => mode === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成預設。 */
export function readToastMode(): ToastMode {
  try {
    const stored = localStorage.getItem(TOAST_MODE_STORAGE_KEY);
    return isToastMode(stored) ? stored : DEFAULT_TOAST_MODE;
  } catch {
    return DEFAULT_TOAST_MODE;
  }
}

// 只有寫不進去時（私密瀏覽）才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: ToastMode | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

export function currentToastMode(): ToastMode {
  return unsavedValue ?? readToastMode();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setToastMode(value: ToastMode): void {
  try {
    localStorage.setItem(TOAST_MODE_STORAGE_KEY, value);
    unsavedValue = null;
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
    unsavedValue = value;
  }
  notify();
}

/** 只給測試用：清掉寫不進 localStorage 時的記憶體暫存，不讓它漏到下一個測試。 */
export function resetToastModeForTests(): void {
  unsavedValue = null;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key === TOAST_MODE_STORAGE_KEY || event.key === null) onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useToastMode(): ToastMode {
  return useSyncExternalStore(subscribe, currentToastMode, () => DEFAULT_TOAST_MODE);
}
