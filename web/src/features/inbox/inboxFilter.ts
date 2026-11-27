import { NOTIFICATION_SEARCH_MAX_LENGTH } from '../../api/notificationTypes';
import type { NotificationCategory } from '../../api/types';

/**
 * 收件匣的條件都記在網址（重新整理、分享、上一頁都對），預設的不寫進網址：
 * type（mentions、assigned；沒有是全部分類）、q（搜尋字）、show（all 是不只看未讀；沒有是只看未讀）。
 */
const CATEGORY_PARAM = 'type';
const SEARCH_PARAM = 'q';
const SHOW_PARAM = 'show';
const ALL_VALUE = 'all';

export type InboxCategory = NotificationCategory | 'all';

export interface InboxFilter {
  readonly category: InboxCategory;
  /** 搜尋字，已去掉頭尾空白；空字串是不搜尋。 */
  readonly q: string;
  readonly unreadOnly: boolean;
}

/** 不認得的值當成預設：分享來的網址打錯也照樣看得到清單。 */
export function inboxFilterOf(params: URLSearchParams): InboxFilter {
  const type = params.get(CATEGORY_PARAM);
  return {
    category: type === 'mentions' || type === 'assigned' ? type : 'all',
    q: searchTextOf(params.get(SEARCH_PARAM)),
    unreadOnly: params.get(SHOW_PARAM) !== ALL_VALUE,
  };
}

/**
 * 網址的搜尋字照後端的規則先整理：去掉控制字元、截到上限。輸入框擋得住，但分享來或手打的網址擋不住，
 * 不整理的話後端回 400，清單只會一直「載入失敗」、重試也沒用。
 */
function searchTextOf(raw: string | null): string {
  // eslint-disable-next-line no-control-regex -- 就是要拿掉控制字元
  const cleaned = (raw ?? '').replace(/[\u0000-\u001f\u007f-\u009f]/g, '').trim();
  return Array.from(cleaned).slice(0, NOTIFICATION_SEARCH_MAX_LENGTH).join('').trim();
}

/** 換掉其中幾個條件之後的查詢字串：其他參數（開著的面板 item）留著；預設的條件不寫進網址。 */
export function inboxFilterSearch(params: URLSearchParams, change: Partial<InboxFilter>): string {
  const next = new URLSearchParams(params);
  const filter = { ...inboxFilterOf(params), ...change };
  setOrDelete(next, CATEGORY_PARAM, filter.category === 'all' ? null : filter.category);
  setOrDelete(next, SEARCH_PARAM, filter.q === '' ? null : filter.q);
  setOrDelete(next, SHOW_PARAM, filter.unreadOnly ? null : ALL_VALUE);
  const query = next.toString();
  return query === '' ? '' : `?${query}`;
}

function setOrDelete(params: URLSearchParams, name: string, value: string | null): void {
  if (value === null) params.delete(name);
  else params.set(name, value);
}
