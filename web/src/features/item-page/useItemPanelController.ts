import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { revealCell } from '../../lib/revealCell';
import { isFocusLost } from '../../lib/updateThenFocus';
import { useIsCompact } from '../../lib/useMediaQuery';
import { panelItemOf, searchWithPanelItem } from '../work-items/itemLinks';
import type { ItemPanelValue } from './ItemPanelContext';
import { createItemSequenceRegistry, type ItemSequenceRegistry } from './itemSequences';
import { forgetReturnFocus, peekReturnFocus } from './returnFocus';

/**
 * 從清單打開面板的那一筆瀏覽紀錄帶這個 state。關閉時看到它就用「上一頁」回到打開之前（關上面板、不多一筆紀錄），
 * 沒有（網址直接帶著 item 打開、切換過檢視、換過條件）就換掉目前的紀錄、拿掉 item。
 */
interface PanelEntryState {
  readonly itemPanel: true;
}

const PANEL_ENTRY: PanelEntryState = { itemPanel: true };

/** 這一筆瀏覽紀錄是不是從清單打開面板時新開的那一筆。 */
export function isPanelEntry(state: unknown): boolean {
  return typeof state === 'object' && state !== null && 'itemPanel' in state && state.itemPanel === true;
}

/** 頁面的標題（h1，三種放面板的頁面都給了 tabIndex=-1）。清單上找不到那一筆時焦點的最後去處。 */
function pageHeading(): HTMLElement | null {
  return document.querySelector<HTMLElement>('main h1[tabindex]');
}

/**
 * 面板關上之後（關閉、Esc、上一頁、刪除）焦點原本在面板裡、跟著面板不見了：放回清單上那一筆。
 * 面板裡換到不在清單上的項目（點了上層）時，回到清單上最後開的那一筆；清單上都沒有（網址直接帶著 item、
 * 重抓之後被拿掉）就到頁面的標題，不會掉回頁面本體。
 * 面板換了項目時，把清單上的那一筆捲到看得到的地方（上一個、下一個走到捲出畫面的那一列）。
 */
function useListFocus(openKey: string | null, registry: ItemSequenceRegistry) {
  const previous = useRef(openKey);
  const lastListed = useRef<string | null>(null);
  useLayoutEffect(() => {
    const before = previous.current;
    previous.current = openKey;
    if (openKey !== null && registry.sequenceOf(openKey) !== undefined) lastListed.current = openKey;
    if (before !== null && openKey === null && isFocusLost()) {
      const fallback = lastListed.current === null ? null : registry.linkOf(lastListed.current);
      (registry.linkOf(before) ?? fallback ?? pageHeading())?.focus({ preventScroll: true });
    }
    if (openKey === null) lastListed.current = null;
    const link = openKey === null ? null : registry.linkOf(openKey);
    // 表格的編號是固定欄：scrollIntoView 會把橫向捲過去的表格捲回一大段，revealCell 只修正直向（lib/revealCell.ts）。
    if (link !== null) revealCell(link);
  }, [openKey, registry]);
}

/**
 * 從整頁的項目頁用上一頁回到這一筆紀錄時（returnFocus.ts），等清單畫出那一筆再把焦點放上去。
 * 面板開著時不做（面板的標題會拿到焦點）；使用者已經把焦點放到別處也不搶。
 */
function useReturnFocus(entry: string, isPanelOpen: boolean, registry: ItemSequenceRegistry) {
  useEffect(() => {
    if (isPanelOpen) return undefined;
    const itemKey = peekReturnFocus(entry);
    if (itemKey === null) return undefined;
    const attempt = (): boolean => {
      const link = registry.linkOf(itemKey);
      if (link === null) return false;
      forgetReturnFocus(entry);
      if (isFocusLost()) link.focus();
      return true;
    };
    if (attempt()) return undefined;
    // 清單還沒畫出那一筆（資料還在讀）：等清單登記了含它的順序再試，放好一次就不再聽。
    const unsubscribe = registry.subscribe(() => {
      if (attempt()) unsubscribe();
    });
    return unsubscribe;
  }, [entry, isPanelOpen, registry]);
}

type PanelNavigation = Pick<ItemPanelValue, 'open' | 'show' | 'close' | 'closeWith'>;

/**
 * 開、換、關的瀏覽紀錄：從清單打開是新的一筆（上一頁關上面板），換項目是換掉這一筆，
 * 關閉時這一筆是自己開的就用上一頁退掉（不多一筆一樣的清單），不是（網址直接帶著 item）就換掉這一筆、拿掉 item。
 */
function usePanelNavigation(param: string | null): PanelNavigation {
  const location = useLocation();
  const navigate = useNavigate();
  const isOwnEntry = isPanelEntry(location.state);
  const keepEntry = isOwnEntry ? PANEL_ENTRY : null;
  // hash 只有 open 帶：換項目、關閉都不帶，上一個、下一個之後就不會再指著原本那一則留言。
  const target = (itemKey: string | null, hash = '') => ({
    pathname: location.pathname,
    search: searchWithPanelItem(location.search, itemKey),
    hash,
  });
  const go = (itemKey: string | null, replace: boolean, state: PanelEntryState | null, hash?: string) => {
    void navigate(target(itemKey, hash), { replace, state });
  };
  return {
    open: (itemKey, hash) => {
      if (itemKey === param && !hash) return;
      if (param === null) go(itemKey, false, PANEL_ENTRY, hash);
      else go(itemKey, true, keepEntry, hash);
    },
    show: (itemKey) => {
      if (itemKey !== param) go(itemKey, true, keepEntry);
    },
    close: () => {
      if (isOwnEntry) void navigate(-1);
      else go(null, true, null);
    },
    closeWith: async (state) => {
      const list = target(null);
      if (isOwnEntry) await navigate(-1);
      await navigate(list, { replace: true, state });
    },
  };
}

export interface ItemPanelController {
  readonly value: ItemPanelValue;
  /** 窄螢幕而網址帶著 item：不用面板，呼叫端換到整頁。 */
  readonly compactKey: string | null;
}

/** 面板的狀態就是網址（?item=AGY-3，docs/DESIGN.md 8.3）：可以分享、重新整理，上一頁關上面板。 */
export function useItemPanelController(): ItemPanelController {
  const isCompact = useIsCompact();
  const location = useLocation();
  const param = panelItemOf(new URLSearchParams(location.search));
  const openKey = isCompact ? null : param;
  const [registry] = useState(createItemSequenceRegistry);
  useListFocus(openKey, registry);
  useReturnFocus(location.key, param !== null, registry);
  const navigation = usePanelNavigation(param);
  const value: ItemPanelValue = { openKey, isEnabled: !isCompact, registry, ...navigation };
  return { value, compactKey: isCompact ? param : null };
}
