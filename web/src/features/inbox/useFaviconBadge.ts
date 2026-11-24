import { useEffect } from 'react';
import { badgedFaviconUrl } from './faviconBadge';
import { useTabBadge } from './tabBadgePreference';

/**
 * 有未讀時把分頁的圖示換成帶圓點的（照「設定 → 通知」選的位置、大小、顏色，改了馬上換），
 * 沒有了（讀完、登出時外框卸下）換回原本的。
 */
export function useFaviconBadge(hasUnread: boolean): void {
  const badge = useTabBadge();
  const url = hasUnread ? badgedFaviconUrl(badge) : null;
  useEffect(() => {
    if (url === null) return undefined;
    const links = [...document.querySelectorAll<HTMLLinkElement>('link[rel~="icon"]')];
    const originals = links.map((link) => link.getAttribute('href'));
    links.forEach((link) => { link.setAttribute('href', url); });
    return () => {
      links.forEach((link, index) => {
        const original = originals[index];
        if (original === null || original === undefined) link.removeAttribute('href');
        else link.setAttribute('href', original);
      });
    };
  }, [url]);
}
