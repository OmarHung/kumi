import { useSyncExternalStore } from 'react';

/**
 * 行事曆項目排在哪幾天：只排到期日那一天，或從開始日畫到到期日（期間）。個人的顯示偏好，記在這個瀏覽器，
 * 專案頁與「我的工作」的行事曆共用；不進網址、不存進檢視（和一週從哪天開始同一套，lib/weekStart.ts）。
 * 預設到期日：不改變原本的畫面。
 */
export type CalendarDateMode = 'due' | 'span';

export const CALENDAR_DATE_MODES: readonly CalendarDateMode[] = ['due', 'span'];

export const CALENDAR_DATE_MODE_LABELS: Readonly<Record<CalendarDateMode, string>> = { due: '到期日', span: '期間' };

export const DEFAULT_CALENDAR_DATE_MODE: CalendarDateMode = 'due';

export const CALENDAR_DATE_MODE_STORAGE_KEY = 'calendar.dateMode';

export function isCalendarDateMode(value: unknown): value is CalendarDateMode {
  return CALENDAR_DATE_MODES.some((choice) => choice === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成預設。 */
export function readCalendarDateMode(): CalendarDateMode {
  try {
    const stored = localStorage.getItem(CALENDAR_DATE_MODE_STORAGE_KEY);
    return isCalendarDateMode(stored) ? stored : DEFAULT_CALENDAR_DATE_MODE;
  } catch {
    return DEFAULT_CALENDAR_DATE_MODE;
  }
}

// 只有寫不進去時（私密瀏覽）才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: CalendarDateMode | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

function currentCalendarDateMode(): CalendarDateMode {
  return unsavedValue ?? readCalendarDateMode();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setCalendarDateMode(value: CalendarDateMode): void {
  try {
    localStorage.setItem(CALENDAR_DATE_MODE_STORAGE_KEY, value);
    unsavedValue = null;
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
    unsavedValue = value;
  }
  notify();
}

/** 只給測試用：清掉寫不進 localStorage 時的記憶體暫存，不讓它漏到下一個測試。 */
export function resetCalendarDateModeForTests(): void {
  unsavedValue = null;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key === CALENDAR_DATE_MODE_STORAGE_KEY || event.key === null) onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useCalendarDateMode(): CalendarDateMode {
  return useSyncExternalStore(subscribe, currentCalendarDateMode, () => DEFAULT_CALENDAR_DATE_MODE);
}
