import { PANEL_SIZE_STORAGE_KEY } from './panelSize';

/** 面板上一次對著的專案：記在瀏覽器，不在專案頁時用。 */
export const PANEL_PROJECT_STORAGE_KEY = 'assistant.panelProject';

export function readStoredProject(): string | null {
  try {
    const value = localStorage.getItem(PANEL_PROJECT_STORAGE_KEY);
    return value === null || value === '' ? null : value;
  } catch {
    return null;
  }
}

/** 寫不進去時這次照樣用，下次載入回到沒有選：只是個人偏好。 */
export function storeProject(projectKey: string): void {
  try {
    localStorage.setItem(PANEL_PROJECT_STORAGE_KEY, projectKey);
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
  }
}

/**
 * 登出時清掉面板記住的專案與大小（auth/session.ts 的 markSignedOut）：共用電腦上，下一個登入的人
 * 不該從面板的專案看出上一位在做什麼。
 */
export function clearPanelMemory(): void {
  try {
    localStorage.removeItem(PANEL_PROJECT_STORAGE_KEY);
    localStorage.removeItem(PANEL_SIZE_STORAGE_KEY);
  } catch {
    // 讀寫被擋下時本來也存不進去。
  }
}
