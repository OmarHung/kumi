import { LIST_WIDTH_STORAGE_KEY } from '../features/assistant/listWidth';
import { LAUNCHER_HIDDEN_STORAGE_KEY } from '../features/assistant-panel/launcherPreference';
import { LAUNCHER_POSITION_STORAGE_KEY } from '../features/assistant-panel/launcherPosition';
import { TAB_BADGE_STORAGE_KEY } from '../features/inbox/tabBadgePreference';
import { ACTIVITY_VIEW_STORAGE_KEY } from '../features/item-page/activityView';
import { MY_WORK_ORDER_KEY } from '../features/my-work/myWorkColumnOrder';
import { MY_WORK_WIDTHS_KEY } from '../features/my-work/myWorkColumns';
import { TOAST_MODE_STORAGE_KEY } from '../features/notification-toasts/toastMode';
import { SIDEBAR_STORAGE_KEY } from '../features/shell/sidebarPreference';
import { FLOATING_PANEL_WIDTH_STORAGE_KEY } from '../components/SidePanel/floatingPanelWidth';
import { SIDE_PANEL_WIDTH_STORAGE_KEY } from '../components/SidePanel/sidePanelWidth';
import { CALENDAR_DATE_MODE_STORAGE_KEY } from './calendarDateMode';
import { EDITOR_MODE_STORAGE_KEY } from './editorMode';
import { ITEM_PANEL_MODE_STORAGE_KEY } from './itemPanelMode';
import { THEME_STORAGE_KEY } from './theme';
import { WEEK_START_STORAGE_KEY } from './weekStart';

/** 上一次登入的人的編號：登入時和這次比對，不同才清（內容只是不透明的編號，沒有名稱）。 */
export const LAST_SIGNED_IN_KEY = 'session.lastPersonId';

/**
 * 登出之後還留在這個瀏覽器的鍵：只有純顯示偏好（主題、寬度、排列、開關），內容裡沒有任何名稱或代號。
 * 用保留清單而不是清除清單：之後新增的鍵預設會被清掉，不會因為漏列而把專案代號、空間代號、項目標題留給
 * 共用電腦上下一個登入的人。要保留新的鍵，先確認它的內容和鍵名都不帶名稱或代號，再加進來。
 */
export const PRESERVED_LOCAL_KEYS: ReadonlySet<string> = new Set([
  THEME_STORAGE_KEY,
  SIDEBAR_STORAGE_KEY,
  ITEM_PANEL_MODE_STORAGE_KEY,
  FLOATING_PANEL_WIDTH_STORAGE_KEY,
  SIDE_PANEL_WIDTH_STORAGE_KEY,
  TOAST_MODE_STORAGE_KEY,
  TAB_BADGE_STORAGE_KEY,
  EDITOR_MODE_STORAGE_KEY,
  WEEK_START_STORAGE_KEY,
  CALENDAR_DATE_MODE_STORAGE_KEY,
  ACTIVITY_VIEW_STORAGE_KEY,
  LAUNCHER_HIDDEN_STORAGE_KEY,
  LAUNCHER_POSITION_STORAGE_KEY,
  LIST_WIDTH_STORAGE_KEY,
  MY_WORK_ORDER_KEY,
  MY_WORK_WIDTHS_KEY,
  LAST_SIGNED_IN_KEY,
]);

/**
 * 登出（含工作階段失效）時清掉：localStorage 裡不在保留清單上的一律刪，sessionStorage 整個清。
 * 讀寫被擋下時本來也存不進去，不當成錯誤。
 */
export function clearSignedInStorage(): void {
  try {
    const keys = Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index));
    // 先收集再刪：邊刪邊數，索引會跟著移動。
    keys.forEach((key) => {
      if (key !== null && !PRESERVED_LOCAL_KEYS.has(key)) localStorage.removeItem(key);
    });
  } catch {
    // 不能用就本來也沒存進去。
  }
  try {
    sessionStorage.clear();
  } catch {
    // 同上。
  }
}

/**
 * 記下這次登入的人，回傳是不是和上一次不同的人。沒有上一次的紀錄（第一次、或儲存不能用）不算不同：
 * 沒有東西可以比，由「一開始就是沒登入」那條路負責清。
 */
export function rememberSignedInPerson(personId: string): boolean {
  try {
    const previous = localStorage.getItem(LAST_SIGNED_IN_KEY);
    localStorage.setItem(LAST_SIGNED_IN_KEY, personId);
    return previous !== null && previous !== personId;
  } catch {
    return false;
  }
}
