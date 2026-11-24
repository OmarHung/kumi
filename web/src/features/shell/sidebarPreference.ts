import { useState } from 'react';

/** 寬螢幕的側欄收起來了沒有：個人的顯示偏好，記在這個瀏覽器（docs/DESIGN.md 8.3）。 */
export const SIDEBAR_STORAGE_KEY = 'shell.sidebar';

const COLLAPSED = 'collapsed';

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成展開。 */
export function readSidebarCollapsed(): boolean {
  try {
    return localStorage.getItem(SIDEBAR_STORAGE_KEY) === COLLAPSED;
  } catch {
    return false;
  }
}

/** 展開就拿掉設定。寫不進去時這次照樣收起來，下次載入回到展開：只是個人偏好，不值得打斷使用者。 */
export function saveSidebarCollapsed(isCollapsed: boolean): void {
  try {
    if (isCollapsed) {
      localStorage.setItem(SIDEBAR_STORAGE_KEY, COLLAPSED);
    } else {
      localStorage.removeItem(SIDEBAR_STORAGE_KEY);
    }
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
  }
}

/** 目前收起來了沒有，與切換的函式：切了就記住。 */
export function useSidebarCollapsed(): readonly [boolean, () => void] {
  const [isCollapsed, setIsCollapsed] = useState(readSidebarCollapsed);
  const toggle = () => {
    const next = !isCollapsed;
    saveSidebarCollapsed(next);
    setIsCollapsed(next);
  };
  return [isCollapsed, toggle];
}
