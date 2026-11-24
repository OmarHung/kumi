import type { Location } from 'react-router';

export const LOGIN_PATH = '/login';
export const HOME_PATH = '/';
const NEXT_PARAM = 'next';

/**
 * 登入後要回去的網址來自查詢字串，是外部輸入。只接受站內的路徑。
 * 用瀏覽器同一套規則解析後再比對來源，不自己比字串：`/\evil`、夾了 Tab 的 `/<Tab>/evil`
 * 解析後都會變成 `//evil`，也就是別的網站。
 */
export function safeNextPath(raw: string | null): string {
  if (raw === null || !raw.startsWith('/')) {
    return HOME_PATH;
  }
  const { origin } = window.location;
  const url = new URL(raw, origin);
  if (url.origin !== origin || url.pathname === LOGIN_PATH) {
    return HOME_PATH;
  }
  return `${url.pathname}${url.search}${url.hash}`;
}

export function nextPathFrom(search: URLSearchParams): string {
  return safeNextPath(search.get(NEXT_PARAM));
}

/** 導到登入頁，並記下現在的頁面，登入完回來。首頁就不必記。 */
export function loginPathFor(location: Location): string {
  const current = `${location.pathname}${location.search}`;
  if (current === HOME_PATH) {
    return LOGIN_PATH;
  }
  return `${LOGIN_PATH}?${new URLSearchParams({ [NEXT_PARAM]: current }).toString()}`;
}

/** 導到登入頁時帶著的 navigate state：已經登入、之後才收到 401（工作階段被作廢、過期、帳號被停用）。 */
export interface LoginRedirectState {
  readonly sessionExpired: true;
}

export const SESSION_EXPIRED_STATE: LoginRedirectState = { sessionExpired: true };

/** 瀏覽器歷史的 state 是外部輸入（可能是別的頁面或舊版本留的）：只認得固定的形狀。 */
export function isSessionExpiredState(state: unknown): boolean {
  return typeof state === 'object' && state !== null && (state as { sessionExpired?: unknown }).sessionExpired === true;
}
