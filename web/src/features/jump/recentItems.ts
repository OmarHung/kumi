/**
 * 最近看過的項目（快速跳轉的「最近」分組用）。存在 localStorage，讀寫失敗當成沒有。
 * 項目頁與項目面板每次顯示一個項目時呼叫 recordRecentItem；排在最前面的是最新的，最多 MAX_RECENT_ITEMS 個。
 */
export interface RecentItem {
  readonly key: string;
  readonly projectKey: string;
  readonly title: string;
}

const STORAGE_KEY = 'jump.recentItems';
export const MAX_RECENT_ITEMS = 8;

function isRecentItem(value: unknown): value is RecentItem {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const candidate = value as Record<string, unknown>;
  return (
    typeof candidate.key === 'string' &&
    typeof candidate.projectKey === 'string' &&
    typeof candidate.title === 'string'
  );
}

export function readRecentItems(): readonly RecentItem[] {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter(isRecentItem).slice(0, MAX_RECENT_ITEMS) : [];
  } catch {
    return [];
  }
}

/**
 * 登出（含工作階段失效）時清掉：記錄帶著項目的標題，共用電腦上下一個登入的人按 ⌘K 不能看到上一個人看過什麼。
 * 不照人分鍵：分了鍵，上一個人的標題照樣留在這台電腦上。
 */
export function clearRecentItems(): void {
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // 讀寫被擋下時本來也存不進去。
  }
}

export function recordRecentItem(item: RecentItem): void {
  const others = readRecentItems().filter((existing) => existing.key !== item.key);
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify([item, ...others].slice(0, MAX_RECENT_ITEMS)));
  } catch {
    // 存不了（私密瀏覽、容量滿）就當成沒有最近的項目，不影響畫面。
  }
}
