import type { Item, Project, ProjectSearchHit } from '../../api/types';
import type { IconName } from '../../components/Icon/Icon';
import { projectPageLink } from '../projects/projectViewPreference';
import { itemPagePath } from '../work-items/itemLinks';
import type { RecentItem } from './recentItems';

/**
 * 快速跳轉的結果（docs/DESIGN.md 8.3「快速跳轉」）：照輸入的字分組，純邏輯，不碰畫面與網路。
 * 分組的順序固定：編號（像 AGY-12 的輸入）、最近看過、專案、頁面、項目（跨專案的伺服器搜尋結果）。
 */

/** 選了之後做什麼：換頁，或在原地打開成員對話框（成員沒有自己的網址）。 */
export type JumpAction =
  | { readonly kind: 'navigate'; readonly to: string }
  | { readonly kind: 'members'; readonly projectKey: string };

export interface JumpOption {
  /** 全部分組裡唯一：同一個項目只出現一次（item:AGY-3）。 */
  readonly key: string;
  readonly label: string;
  /** 等寬字的代號：項目編號、專案代號。 */
  readonly code?: string;
  /** 靠右的灰字：項目的標題、頁面所在的專案。 */
  readonly hint?: string;
  /** 名稱後面、灰字前面一個不收縮的小標籤（封存的專案寫「已封存」）：灰字很長被截斷時，標籤也一定看得到。 */
  readonly tag?: string;
  readonly icon?: IconName;
  readonly action: JumpAction;
}

export type JumpGroupKey = 'goto' | 'recent' | 'projects' | 'pages' | 'search';

export interface JumpGroup {
  readonly key: JumpGroupKey;
  readonly label: string;
  readonly options: readonly JumpOption[];
}

export interface JumpSources {
  readonly query: string;
  readonly recent: readonly RecentItem[];
  /** 已經載入的專案清單（側欄同一份）。 */
  readonly projects: readonly Project[];
  /** 照權限排好的頁面（jumpPages.ts）。 */
  readonly pages: readonly JumpOption[];
  /** 跨專案的項目搜尋結果，照伺服器的順序；沒有輸入或還在搜尋時是空的。 */
  readonly searchResults: readonly Item[];
  /**
   * 專案搜尋（GET /v1/search/projects）的結果：只用 snippet 不是 null 的（說明比對到的），
   * 代號與名稱就比對到的前端自己找得到、而且是即時的。沒有輸入或還在搜尋時是空的。
   */
  readonly descriptionHits: readonly ProjectSearchHit[];
  /** 目前所在的專案代號；不在專案裡是 null。搜尋結果在這個專案以外的才帶專案名稱。 */
  readonly currentProjectKey: string | null;
}

/** 封存的專案在快速跳轉裡的標籤。 */
const ARCHIVED_TAG = '已封存';

/** 專案一次最多列這麼多個：再多就要再打幾個字縮小。 */
export const PROJECT_LIMIT = 8;

const ITEM_KEY = /^[A-Za-z][A-Za-z0-9]*-\d+$/;

export interface ItemKeyTarget {
  readonly itemKey: string;
  readonly projectKey: string;
}

/** 輸入像項目編號（AGY-12）時，正規化成大寫的編號與它的專案代號；不像就是 null。 */
export function itemKeyTarget(query: string): ItemKeyTarget | null {
  const trimmed = query.trim();
  if (!ITEM_KEY.test(trimmed)) return null;
  const itemKey = trimmed.toUpperCase();
  return { itemKey, projectKey: itemKey.slice(0, itemKey.lastIndexOf('-')) };
}

function includesText(needle: string, ...haystacks: readonly string[]): boolean {
  return needle === '' || haystacks.some((text) => text.toLowerCase().includes(needle));
}

function itemOption(key: string, projectKey: string, title: string): JumpOption {
  return { key: `item:${key}`, code: key, label: title, action: { kind: 'navigate', to: itemPagePath(projectKey, key) } };
}

/**
 * 搜尋結果的選項。編號已經帶專案代號，但人認得的是專案名稱：不是目前專案的項目，靠右多一個專案名稱
 * （不在專案裡時每一筆都是）；目前專案的不帶，免得每一列都重複同一個名稱。
 */
function searchOption(item: Item, sources: JumpSources): JumpOption {
  const option = itemOption(item.key, item.projectKey, item.title);
  if (item.projectKey === sources.currentProjectKey) return option;
  const project = sources.projects.find((candidate) => candidate.key === item.projectKey);
  return { ...option, hint: project?.name ?? item.projectKey };
}

function gotoOption(target: ItemKeyTarget, knownTitle: string | undefined): JumpOption {
  const base = {
    key: `item:${target.itemKey}`,
    label: `前往 ${target.itemKey}`,
    icon: 'chevronRight' as const,
    action: { kind: 'navigate' as const, to: itemPagePath(target.projectKey, target.itemKey) },
  };
  return knownTitle === undefined ? base : { ...base, hint: knownTitle };
}

/**
 * 專案照上次用的檢視開（表格或看板），和側欄、麵包屑的專案連結一樣。
 * 封存的專案照樣找得到（還看得到），名稱後面一個「已封存」的標籤；說明比對到的專案靠右是摘錄，
 * 標籤是獨立的、不收縮，摘錄自己截斷，所以封存的標示不會被省略號吃掉。
 */
function projectOption(project: Project, snippet: string | null = null): JumpOption {
  return {
    key: `project:${project.key}`,
    code: project.key,
    label: project.name,
    ...(project.archivedAt === null ? {} : { tag: ARCHIVED_TAG }),
    ...(snippet === null ? {} : { hint: snippet }),
    action: { kind: 'navigate', to: projectPageLink(project.key) },
  };
}

/**
 * 專案組：前端比對代號、名稱的先排（沒封存的在前，各自維持原本的順序），說明比對到的接在後面，
 * 兩種加起來最多 PROJECT_LIMIT 個——說明的是補剩下的位子，不擠掉即時比對到的。
 */
function projectOptions(needle: string, sources: JumpSources): readonly JumpOption[] {
  const byName = [...sources.projects]
    .sort((a, b) => Number(a.archivedAt !== null) - Number(b.archivedAt !== null))
    .filter((project) => includesText(needle, project.key, project.name))
    .slice(0, PROJECT_LIMIT)
    .map((project) => projectOption(project));
  const named = new Set(byName.map((option) => option.key));
  const byDescription = sources.descriptionHits
    .filter(({ project, snippet }) => snippet !== null && !named.has(`project:${project.key}`))
    .map(({ project, snippet }) => projectOption(project, snippet));
  return [...byName, ...byDescription].slice(0, PROJECT_LIMIT);
}

function knownTitleOf(itemKey: string, sources: JumpSources): string | undefined {
  const known = [...sources.recent, ...sources.searchResults].find((item) => item.key.toUpperCase() === itemKey);
  return known?.title;
}

function candidateGroups(sources: JumpSources): readonly JumpGroup[] {
  const needle = sources.query.trim().toLowerCase();
  const target = itemKeyTarget(sources.query);
  const goto = target === null ? [] : [gotoOption(target, knownTitleOf(target.itemKey, sources))];
  const recent = sources.recent
    .filter((item) => includesText(needle, item.key, item.title))
    .map((item) => itemOption(item.key, item.projectKey, item.title));
  const pages = sources.pages.filter((page) => includesText(needle, page.label));
  const search = sources.searchResults.map((item) => searchOption(item, sources));
  return [
    { key: 'goto', label: '編號', options: goto },
    { key: 'recent', label: '最近看過', options: recent },
    { key: 'projects', label: '專案', options: projectOptions(needle, sources) },
    { key: 'pages', label: '頁面', options: pages },
    { key: 'search', label: '項目', options: search },
  ];
}

/** 照順序分好的結果：同一個項目只留第一次出現的那一筆，空的分組拿掉。 */
export function buildJumpGroups(sources: JumpSources): readonly JumpGroup[] {
  const seen = new Set<string>();
  return candidateGroups(sources)
    .map((group) => {
      const options = group.options.filter((option) => !seen.has(option.key));
      options.forEach((option) => seen.add(option.key));
      return { ...group, options };
    })
    .filter((group) => group.options.length > 0);
}

/** 所有分組的選項攤平成一串：方向鍵照這個順序走。 */
export function flattenGroups(groups: readonly JumpGroup[]): readonly JumpOption[] {
  return groups.flatMap((group) => group.options);
}
