import { useEffect, useState, type RefObject } from 'react';
import { useLocation } from 'react-router';
import { focusPageHeading } from './pageTop';

/** 做完一件事換到另一頁時帶的提示（navigate 的 state），例如套用變更集、刪除項目、把自己移出專案之後。 */
export interface ArrivalState {
  readonly notice: string;
  /**
   * 這件事可以復原（刪除項目）：提示旁邊有「復原」。放在瀏覽紀錄的 state 裡，只放資料（專案代號與項目編號）；
   * 按了做什麼是 ArrivalNotice 的事（features/bulk-edit/UndoableArrival.tsx）。
   */
  readonly undo?: ArrivalUndo;
}

export interface ArrivalUndo {
  readonly projectKey: string;
  readonly keys: readonly string[];
}

function isStringArray(value: unknown): value is readonly string[] {
  return Array.isArray(value) && value.every((entry) => typeof entry === 'string');
}

/** 瀏覽紀錄的 state 裡帶的「復原」資料；沒有或形狀不對（舊的紀錄、別處導過來的）是 null。 */
export function undoOf(state: unknown): ArrivalUndo | null {
  if (typeof state !== 'object' || state === null || !('undo' in state)) return null;
  const { undo } = state;
  if (typeof undo !== 'object' || undo === null || !('projectKey' in undo) || !('keys' in undo)) return null;
  return typeof undo.projectKey === 'string' && isStringArray(undo.keys) ? { projectKey: undo.projectKey, keys: undo.keys } : null;
}

function noticeOf(state: unknown): string | null {
  if (typeof state !== 'object' || state === null || !('notice' in state)) return null;
  return typeof state.notice === 'string' ? state.notice : null;
}

/**
 * 已經顯示過提示的那幾筆瀏覽紀錄（location.key）。按上一頁回到那一筆時，紀錄的 state 還帶著提示，
 * 但那件事早就說過了：不再顯示、也不再把焦點拉到標題。頁面卸下再掛上（從別頁回來）也要記得，所以放在模組裡。
 */
const shownEntries = new Set<string>();

interface Shown {
  readonly key: string;
  readonly text: string;
}

/**
 * 換到這一頁時：按的按鈕已經不在了，焦點移到頁面的標題、畫面捲回最上面；提示等一下才填進一直掛著的即時區域
 * （和區域一起出現的文字，螢幕報讀器常常不唸）。回傳要顯示的提示，還沒填之前是空字串。
 * 換到別的網址（例如切換檢視、沒有提示）就清掉；同一筆紀錄的提示只顯示一次。
 */
export function useArrivalNotice(headingRef: RefObject<HTMLElement | null>): string {
  const location = useLocation();
  const notice = noticeOf(location.state);
  const [shown, setShown] = useState<Shown | null>(null);
  if (shown !== null && shown.key !== location.key) {
    setShown(null);
  }
  useEffect(() => {
    if (notice === null || shownEntries.has(location.key)) return undefined;
    focusPageHeading(headingRef.current);
    const timer = window.setTimeout(() => {
      // 真的填進去了才算說過：開發模式下 effect 會先跑一次再清掉，那一次不算。
      shownEntries.add(location.key);
      setShown({ key: location.key, text: notice });
    }, 0);
    return () => {
      window.clearTimeout(timer);
    };
  }, [notice, location.key, headingRef]);
  return shown !== null && shown.key === location.key ? shown.text : '';
}
