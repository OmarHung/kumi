import { PANEL_SIZE_STORAGE_KEY } from './panelSize';

/** 面板上一次對著的專案：記在瀏覽器，不在專案頁時用。 */
export const PANEL_PROJECT_STORAGE_KEY = 'assistant.panelProject';

/** 面板對著什麼：專案代號，null 是「不選專案」（組織的對話），undefined 是還沒選。 */
export type PanelScope = string | null | undefined;

/** 記在瀏覽器裡代表「不選專案」的值：專案代號只有英數字，不會撞到。 */
export const NO_PROJECT_STORED = '@organization';

export function readStoredProject(): PanelScope {
  try {
    const value = localStorage.getItem(PANEL_PROJECT_STORAGE_KEY);
    if (value === null || value === '') return undefined;
    return value === NO_PROJECT_STORED ? null : value;
  } catch {
    return undefined;
  }
}

/** 寫不進去時這次照樣用，下次載入回到沒有選：只是個人偏好。 */
export function storeProject(projectKey: string | null): void {
  try {
    localStorage.setItem(PANEL_PROJECT_STORAGE_KEY, projectKey ?? NO_PROJECT_STORED);
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
