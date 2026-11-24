import { useSyncExternalStore } from 'react';

/**
 * 一週從哪天開始：個人的顯示偏好，記在這個瀏覽器。行事曆、選日期的小月曆、時間軸、「本週到期」與我的工作的「這一週」
 * 全都照它算，各處的「一週」才一致。預設週一（不讓原本的畫面突然改變）；台灣常見週日開頭的月曆，所以可以改。
 */
export type WeekStart = 'monday' | 'sunday';

export const WEEK_STARTS: readonly WeekStart[] = ['monday', 'sunday'];

/** 選項上的字（行事曆的下拉）。 */
export const WEEK_START_LABELS: Readonly<Record<WeekStart, string>> = { monday: '週一開始', sunday: '週日開始' };

export const DEFAULT_WEEK_START: WeekStart = 'monday';

export const WEEK_START_STORAGE_KEY = 'shell.weekStart';

function isWeekStart(value: unknown): value is WeekStart {
  return WEEK_STARTS.some((choice) => choice === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成預設。 */
export function readWeekStart(): WeekStart {
  try {
    const stored = localStorage.getItem(WEEK_START_STORAGE_KEY);
    return isWeekStart(stored) ? stored : DEFAULT_WEEK_START;
  } catch {
    return DEFAULT_WEEK_START;
  }
}

// 只有寫不進去時（私密瀏覽）才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: WeekStart | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

function currentWeekStart(): WeekStart {
  if (unsavedValue !== null) return unsavedValue;
  return readWeekStart();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setWeekStart(value: WeekStart): void {
  try {
    localStorage.setItem(WEEK_START_STORAGE_KEY, value);
    unsavedValue = null;
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
    unsavedValue = value;
  }
  notify();
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key === WEEK_START_STORAGE_KEY || event.key === null) onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useWeekStart(): WeekStart {
  return useSyncExternalStore(subscribe, currentWeekStart, () => DEFAULT_WEEK_START);
}
