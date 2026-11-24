import { useSyncExternalStore } from 'react';
import type { ActivityView } from '../../api/activityPages';

/**
 * 項目時間軸看哪一份：只有留言與回覆，或全部（也有系統記下的變更）。個人的顯示偏好，記在這個瀏覽器，
 * 項目頁、面板、每個項目共用。預設留言：變更比留言多，討論被夾在中間，常看的是留言。
 * 型別定義在 api/activityPages.ts（查詢要用它，api 不往 features 引用），這裡再匯出。
 */
export type { ActivityView };

export const ACTIVITY_VIEWS: readonly ActivityView[] = ['comments', 'all'];

export const DEFAULT_ACTIVITY_VIEW: ActivityView = 'comments';

export const ACTIVITY_VIEW_STORAGE_KEY = 'item.activityView';

function isActivityView(value: unknown): value is ActivityView {
  return ACTIVITY_VIEWS.some((choice) => choice === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成預設。 */
export function readActivityView(): ActivityView {
  try {
    const stored = localStorage.getItem(ACTIVITY_VIEW_STORAGE_KEY);
    return isActivityView(stored) ? stored : DEFAULT_ACTIVITY_VIEW;
  } catch {
    return DEFAULT_ACTIVITY_VIEW;
  }
}

// 只有寫不進去時（私密瀏覽）才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: ActivityView | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

function currentActivityView(): ActivityView {
  if (unsavedValue !== null) return unsavedValue;
  return readActivityView();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setActivityView(value: ActivityView): void {
  try {
    localStorage.setItem(ACTIVITY_VIEW_STORAGE_KEY, value);
    unsavedValue = null;
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
    unsavedValue = value;
  }
  notify();
}

/** 只給測試用：清掉寫不進 localStorage 時的記憶體暫存，不讓它漏到下一個測試。 */
export function resetActivityViewForTests(): void {
  unsavedValue = null;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key === ACTIVITY_VIEW_STORAGE_KEY || event.key === null) onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useActivityView(): ActivityView {
  return useSyncExternalStore(subscribe, currentActivityView, () => DEFAULT_ACTIVITY_VIEW);
}
