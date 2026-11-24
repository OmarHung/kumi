import { useLocation } from 'react-router';

/** 項目頁、專案頁、設定頁與變更集的網址。表格、我的工作、下層項目清單都連到這裡，網址的寫法只維護一份。 */
export function projectPagePath(projectKey: string): string {
  return `/projects/${encodeURIComponent(projectKey)}`;
}

export function itemPagePath(projectKey: string, itemKey: string): string {
  return `${projectPagePath(projectKey)}/items/${encodeURIComponent(itemKey)}`;
}

export function projectPlanningPath(projectKey: string): string {
  return `${projectPagePath(projectKey)}/planning`;
}

/** 資訊頁：專案說明（網址、部署環境、主機）。 */
export function projectInfoPath(projectKey: string): string {
  return `${projectPagePath(projectKey)}/info`;
}

/** 專案的 AI 助理；沒給對話編號是「新的對話」（還沒開）。 */
export function projectAssistantPath(projectKey: string, conversationId?: string, itemKey?: string | null): string {
  const base = `${projectPagePath(projectKey)}/assistant`;
  const path = conversationId === undefined ? base : `${base}/${encodeURIComponent(conversationId)}`;
  // 從面板開成整頁：帶著選的項目過去（整頁只在進來時讀一次）。
  return itemKey === undefined || itemKey === null ? path : `${path}${searchWithPanelItem('', itemKey)}`;
}

/** 已刪除的項目頁：刪除的項目列在這裡，可以一筆一筆復原。 */
export function projectDeletedPath(projectKey: string): string {
  return `${projectPagePath(projectKey)}/deleted`;
}

/** 匯入項目的頁面（PM-4b，features/item-import/）。 */
export function projectImportPath(projectKey: string): string {
  return `/projects/${encodeURIComponent(projectKey)}/import`;
}

/** 設定頁本身：會轉到預設的區塊（流程）。連到設定頁、不指定區塊時用這個。 */
export function projectSettingsPath(projectKey: string): string {
  return `${projectPagePath(projectKey)}/settings`;
}

/** 設定頁的四個區塊，照分頁的順序。網址的最後一段就是它。 */
export const SETTINGS_SECTIONS = ['workflows', 'types', 'fields', 'roles'] as const;
export type SettingsSection = (typeof SETTINGS_SECTIONS)[number];
export const DEFAULT_SETTINGS_SECTION: SettingsSection = 'workflows';

export function settingsSectionPath(projectKey: string, section: SettingsSection): string {
  return `${projectSettingsPath(projectKey)}/${section}`;
}

/** 網址在哪個專案的設定頁、哪個區塊；section 是 null 是設定頁本身（還沒轉到區塊）。 */
export interface SettingsPlace {
  readonly projectKey: string;
  readonly section: SettingsSection | null;
}

const SETTINGS_PATH = /^\/projects\/([^/]+)\/settings(?:\/([^/]+))?\/?$/;

function isSettingsSection(value: string): value is SettingsSection {
  return (SETTINGS_SECTIONS as readonly string[]).includes(value);
}

function decodeKey(segment: string): string | null {
  try {
    return decodeURIComponent(segment);
  } catch {
    // 編碼壞掉的網址不是任何一個專案的設定頁。
    return null;
  }
}

/** 不是設定頁（包含不認得的區塊）回 null。 */
export function settingsPlaceOf(pathname: string): SettingsPlace | null {
  const match = SETTINGS_PATH.exec(pathname);
  const [, segment, section] = match ?? [];
  if (segment === undefined) return null;
  const projectKey = decodeKey(segment);
  if (projectKey === null) return null;
  if (section === undefined) return { projectKey, section: null };
  return isSettingsSection(section) ? { projectKey, section } : null;
}

/** 兩個網址都在同一個專案的設定頁底下（切換區塊、轉到預設的區塊）：草稿還在同一頁，離開提醒不必問。 */
export function isSameSettingsPage(from: string, to: string): boolean {
  const before = settingsPlaceOf(from);
  const after = settingsPlaceOf(to);
  return before !== null && after !== null && before.projectKey === after.projectKey;
}

export function changeSetsPath(projectKey: string): string {
  return `${projectPagePath(projectKey)}/changesets`;
}

export function changeSetPagePath(projectKey: string, number: number): string {
  return `${changeSetsPath(projectKey)}/${String(number)}`;
}

/** 清單旁邊開著的項目面板記在網址的這個參數（?item=AGY-3），和 view、篩選參數並存（docs/DESIGN.md 8.3）。 */
export const ITEM_PANEL_PARAM = 'item';

/** 網址上開著面板的項目編號；沒有（或是空白）時是 null。 */
export function panelItemOf(params: URLSearchParams): string | null {
  const value = params.get(ITEM_PANEL_PARAM)?.trim() ?? '';
  return value === '' ? null : value;
}

function paramName(pair: string): string {
  const name = pair.split('=')[0] ?? '';
  try {
    return decodeURIComponent(name.replaceAll('+', ' '));
  } catch {
    return name;
  }
}

/**
 * 換掉（itemKey 是 null 時拿掉）查詢字串裡的面板項目，其他參數原封不動：
 * 不經過 URLSearchParams 重新編碼，網址上的「q=首頁」開關面板之後還是原本的寫法。
 */
export function searchWithPanelItem(search: string, itemKey: string | null): string {
  const others = search
    .replace(/^\?/, '')
    .split('&')
    .filter((pair) => pair !== '' && paramName(pair) !== ITEM_PANEL_PARAM);
  const pairs = itemKey === null ? others : [...others, `${ITEM_PANEL_PARAM}=${encodeURIComponent(itemKey)}`];
  return pairs.length === 0 ? '' : `?${pairs.join('&')}`;
}

/** 換條件（篩選、排序、看板的流程）時面板留著：新的查詢參數接上現在開著的項目。 */
export function keepPanelItem(next: URLSearchParams, current: URLSearchParams): URLSearchParams {
  const itemKey = panelItemOf(current);
  return new URLSearchParams(searchWithPanelItem(`?${next.toString()}`, itemKey));
}

/** 現在的網址拿掉面板：連到整頁時記下的 returnTo、在面板裡刪除之後留下的清單。 */
export function pathWithoutPanelItem(pathname: string, search: string): string {
  return `${pathname}${searchWithPanelItem(search, null)}`;
}

/**
 * 從編號猜專案代號（AGY-3 → AGY）：我的工作是跨專案的，面板先用它讀專案；
 * 讀到項目之後以項目自己的 projectKey 為準，猜錯也不會用錯藍圖。
 */
export function projectKeyOfItemKey(itemKey: string): string {
  const dash = itemKey.lastIndexOf('-');
  return (dash > 0 ? itemKey.slice(0, dash) : itemKey).toUpperCase();
}

/**
 * 從清單連到項目頁時帶的 state。returnTo：刪除項目之後、麵包屑的專案連結回到原本的檢視與條件；
 * fromEntry：清單那一筆瀏覽紀錄（location.key），用上一頁回去時焦點放回那一筆（returnFocus.ts）。
 */
export interface ItemPageEntry {
  readonly returnTo: string;
  readonly fromEntry?: string;
}

/** 連到項目頁的連結用：記下現在的網址（專案頁的檢視與條件都在網址上；開著的面板不算）。 */
export function useItemPageEntry(): ItemPageEntry {
  const location = useLocation();
  return { returnTo: pathWithoutPanelItem(location.pathname, location.search), fromEntry: location.key };
}

/** 連進來時記下的是這個專案的專案頁（表格或看板、條件照舊）就回傳它；其他的（我的工作、下層清單、直接打網址）是 null。 */
export function entryReturnTo(projectKey: string, state: unknown): string | null {
  const base = projectPagePath(projectKey);
  const returnTo = typeof state === 'object' && state !== null && 'returnTo' in state ? state.returnTo : undefined;
  if (typeof returnTo !== 'string') return null;
  return returnTo === base || returnTo.startsWith(`${base}?`) ? returnTo : null;
}

/**
 * 刪除項目之後要回到的專案頁：連進來時記下的是這個專案的專案頁就回去那裡（表格或看板、條件照舊），
 * 其他的（從我的工作、下層清單、直接打網址進來）回到專案的表格。
 */
export function returnToProject(projectKey: string, state: unknown): string {
  return entryReturnTo(projectKey, state) ?? projectPagePath(projectKey);
}
