/** 收件匣的分頁記在網址的 show 參數（重新整理、分享、上一頁都對）：沒有是「未讀」，all 是「全部」。 */
const FILTER_PARAM = 'show';
const ALL_VALUE = 'all';

export type InboxFilter = 'unread' | 'all';

/** 不認得的值當成預設（未讀）：分享來的網址打錯也照樣看得到清單。 */
export function inboxFilterOf(params: URLSearchParams): InboxFilter {
  return params.get(FILTER_PARAM) === ALL_VALUE ? 'all' : 'unread';
}

/** 換成另一個分頁的查詢字串：其他參數（開著的面板 item）留著；預設的分頁不寫進網址。 */
export function inboxFilterSearch(params: URLSearchParams, filter: InboxFilter): string {
  const next = new URLSearchParams(params);
  if (filter === 'all') next.set(FILTER_PARAM, ALL_VALUE);
  else next.delete(FILTER_PARAM);
  const query = next.toString();
  return query === '' ? '' : `?${query}`;
}
