import { isNavigableUrl } from './navigableUrl';

/**
 * 整頁導到別的網址（OAuth 授權完導回應用，可能是別的網站或本機的 http 網址）。獨立成一個函式，測試才換得掉。
 * 不是 http(s) 的網址不導（呼叫端也用 isNavigableUrl 先當作沒有網址，這裡是最後一道）。
 */
export function navigateTo(url: string): void {
  if (!isNavigableUrl(url)) return;
  window.location.assign(url);
}
