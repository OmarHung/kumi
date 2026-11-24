import { panelItemOf } from '../work-items/itemLinks';

const ITEM_PAGE_PATH = /^\/projects\/[^/]+\/items\/([^/]+)\/?$/;

function decode(segment: string): string {
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}

/**
 * 畫面上正開著的項目（大寫）：項目頁，與清單旁邊的面板、浮動視窗（網址的 ?item=）。
 * 這些項目的新通知不跳提示：那一則留言就在眼前。項目編號不分大小寫（網址是人打的）。
 */
export function openItemKeysOf(pathname: string, search: string): readonly string[] {
  const onPage = ITEM_PAGE_PATH.exec(pathname)?.[1];
  const inPanel = panelItemOf(new URLSearchParams(search));
  return [onPage === undefined ? null : decode(onPage), inPanel].flatMap((key) => (key === null ? [] : [key.toUpperCase()]));
}

/** 收件匣頁：清單自己會即時更新，不跳提示。 */
export function isInboxPath(pathname: string): boolean {
  return pathname === '/inbox' || pathname.startsWith('/inbox/');
}
