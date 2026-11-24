import { useSyncExternalStore } from 'react';
import { normalizeHex } from '../../lib/hexColor';
import {
  DEFAULT_TAB_BADGE,
  TAB_BADGE_COLOR_KEYS,
  TAB_BADGE_POSITIONS,
  TAB_BADGE_SIZES,
  type TabBadge,
  type TabBadgeColorChoice,
} from './faviconBadge';

const COLOR_CHOICES: readonly TabBadgeColorChoice[] = [...TAB_BADGE_COLOR_KEYS, 'custom'];

function parseCustomColor(value: unknown): string | null {
  return typeof value === 'string' ? normalizeHex(value) : null;
}

/**
 * 分頁圖示上圓點的位置、大小、顏色。個人的顯示偏好，記在這個瀏覽器（和「通知提示」的顯示方式同一個地方、同一個理由：
 * 只是這台電腦上看起來的樣子）。寫法和 notification-toasts/toastMode.ts 一樣，多的是值是一個物件：
 * useSyncExternalStore 要求同樣的內容回傳同一個物件，所以照存的字串記住上一次讀出來的。
 */
export const TAB_BADGE_STORAGE_KEY = 'notifications.tabBadge';

function pick<T extends string>(options: readonly T[], value: unknown, fallback: T): T {
  return options.find((option) => option === value) ?? fallback;
}

/** 存的內容不可信（別的版本、手動改過）：每個欄位各自檢查，認不得的那一個用預設。 */
function parseTabBadge(raw: string | null): TabBadge {
  if (raw === null) return DEFAULT_TAB_BADGE;
  let stored: unknown;
  try {
    stored = JSON.parse(raw);
  } catch {
    return DEFAULT_TAB_BADGE;
  }
  if (typeof stored !== 'object' || stored === null) return DEFAULT_TAB_BADGE;
  const fields = stored as Record<string, unknown>;
  const customColor = parseCustomColor(fields.customColor);
  const color = pick(COLOR_CHOICES, fields.color, DEFAULT_TAB_BADGE.color);
  return {
    position: pick(TAB_BADGE_POSITIONS, fields.position, DEFAULT_TAB_BADGE.position),
    size: pick(TAB_BADGE_SIZES, fields.size, DEFAULT_TAB_BADGE.size),
    // 選的是自訂、色碼卻讀不懂時沒有顏色可畫，退回預設的顏色。
    color: color === 'custom' && customColor === null ? DEFAULT_TAB_BADGE.color : color,
    customColor,
  };
}

function readRaw(): string | null {
  try {
    return localStorage.getItem(TAB_BADGE_STORAGE_KEY);
  } catch {
    return null;
  }
}

let cached: { readonly raw: string | null; readonly value: TabBadge } = { raw: null, value: DEFAULT_TAB_BADGE };

/** 讀不到（私密瀏覽、被擋下）時是預設。 */
export function readTabBadge(): TabBadge {
  const raw = readRaw();
  if (raw !== cached.raw) cached = { raw, value: parseTabBadge(raw) };
  return cached.value;
}

// 只有寫不進去時（私密瀏覽）才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: TabBadge | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

export function currentTabBadge(): TabBadge {
  return unsavedValue ?? readTabBadge();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setTabBadge(value: TabBadge): void {
  try {
    localStorage.setItem(TAB_BADGE_STORAGE_KEY, JSON.stringify(value));
    unsavedValue = null;
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
    unsavedValue = value;
  }
  notify();
}

/**
 * 只改給的欄位，其他以當下存的為準（不是畫面上一次 render 時的值）：拖曳取色器時一秒會改很多次，
 * 用 render 當下的值去展開，連續兩次之間如果還沒重畫，後面的會蓋掉前面的。色碼不合格式（或是 null）時這一欄不改。
 */
export function updateTabBadge(patch: Partial<TabBadge>): void {
  const current = currentTabBadge();
  const next = { ...current, ...patch };
  // null 也當成「不改」：沒有清掉自訂色碼的操作。
  const customColor = (next.customColor === null ? null : normalizeHex(next.customColor)) ?? current.customColor;
  // 選成自訂卻沒有色碼時沒有顏色可畫：和重新載入時讀到的一樣，退回預設的顏色。
  const color = next.color === 'custom' && customColor === null ? DEFAULT_TAB_BADGE.color : next.color;
  setTabBadge({ ...next, color, customColor });
}

/** 只給測試用：清掉寫不進 localStorage 時的記憶體暫存，不讓它漏到下一個測試。 */
export function resetTabBadgeForTests(): void {
  unsavedValue = null;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key !== TAB_BADGE_STORAGE_KEY && event.key !== null) return;
    // 別的分頁寫進去了：以儲存的為準，這個分頁之前寫不進去時的暫存作廢。
    unsavedValue = null;
    onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useTabBadge(): TabBadge {
  return useSyncExternalStore(subscribe, currentTabBadge, () => DEFAULT_TAB_BADGE);
}
