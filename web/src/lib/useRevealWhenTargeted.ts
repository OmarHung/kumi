import { useEffect, useRef } from 'react';
import { useLocation } from 'react-router';

/**
 * 網址帶著這一塊的錨點（/account/integrations#cli、/help/quick-start#notifications）時捲過來、焦點放到標題：
 * 站內連結換頁時瀏覽器不會自己捲到錨點（React Router 換的是畫面，不是整頁載入），按的連結跟著舊的頁面卸下，焦點也沒有去處。
 * 每次導到這個錨點（location.key 換了）都做一次：同一頁裡再點一次同一個錨點也會捲回來；之後使用者自己捲不再被拉回來。
 * 標題要 tabIndex={-1}（程式放得進焦點、Tab 不停在上面）。設定中心的區塊與使用說明的節共用。
 */
export function useRevealWhenTargeted(id: string | undefined) {
  const sectionRef = useRef<HTMLElement>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const { hash, key } = useLocation();
  const isTargeted = id !== undefined && hash === `#${id}`;
  useEffect(() => {
    const section = sectionRef.current;
    if (!isTargeted || section === null) return;
    // jsdom 沒有 scrollIntoView。
    if (typeof section.scrollIntoView === 'function') section.scrollIntoView({ block: 'start' });
    headingRef.current?.focus({ preventScroll: true });
  }, [isTargeted, key]);
  return { sectionRef, headingRef };
}
