import { clampWidth, DEFAULT_BOUNDS, remToPx, type ColumnWidths, type WidthBounds } from '../../components/Table/columnWidths';
import type { TableVariables } from '../../components/Table/Table';

/*
 * 我的工作的欄（docs/DESIGN.md 8.3「我的工作」、7.1「欄寬」）：所有組的表格共用一組欄寬，寬度是 MyWork.module.css 的區域變數，
 * 使用者拉過的寬度換掉那些變數（設在包住所有組的捲動區塊上），每一組的欄一起變、照樣對齊。記在這個瀏覽器，只有一份。
 * 欄的順序也是所有組共用（myWorkColumnOrder.ts）；標題和它左邊的欄固定在左邊，位置也是捲動區塊上的變數。
 */

export const MY_WORK_WIDTHS_KEY = 'my-work.widths';

export type MyWorkColumnId = 'key' | 'title' | 'state' | 'priority' | 'due' | 'project' | 'assignee';

export interface MyWorkColumn {
  readonly id: MyWorkColumnId;
  readonly label: string;
  readonly bounds: WidthBounds;
}

// 固定在左邊的欄的範圍和專案的表格一樣（features/item-table/itemColumnWidths.ts）。
const KEY_BOUNDS: WidthBounds = { min: 3, max: 16 };
const TITLE_BOUNDS: WidthBounds = { min: 6, max: 32 };

const COLUMNS: readonly MyWorkColumn[] = [
  { id: 'key', label: '編號', bounds: KEY_BOUNDS },
  { id: 'title', label: '標題', bounds: TITLE_BOUNDS },
  { id: 'state', label: '狀態', bounds: DEFAULT_BOUNDS },
  { id: 'priority', label: '優先度', bounds: DEFAULT_BOUNDS },
  { id: 'due', label: '到期日', bounds: DEFAULT_BOUNDS },
  { id: 'project', label: '專案', bounds: DEFAULT_BOUNDS },
  { id: 'assignee', label: '負責人', bounds: DEFAULT_BOUNDS },
];

const BY_ID = new Map(COLUMNS.map((column) => [column.id, column]));

/**
 * 畫面上的欄，照使用者的順序（myWorkColumnOrder.ts，預設是編號、專案、標題、狀態、優先度、到期日、負責人）。
 * 依專案分組沒有專案欄；負責人欄只有看「我建立的」時才有（看「我負責的」時每一列都是我，不必多一欄）。
 */
export function myWorkColumns(order: readonly MyWorkColumnId[], showProject: boolean, showAssignee = false): readonly MyWorkColumn[] {
  return order.flatMap((id) => {
    const column = BY_ID.get(id);
    const isHidden = (id === 'project' && !showProject) || (id === 'assignee' && !showAssignee);
    return column === undefined || isHidden ? [] : [column];
  });
}

/**
 * 每一欄換掉的變數。標題欄沒拉過時吃掉剩下的寬度（最小 --my-work-title-min）；拉過之後就是那個寬度，
 * 換掉的正是最小寬度：固定欄的位置、表格的最小寬度、捲動讓出的寬度都用它。
 */
const VARIABLES: Readonly<Record<MyWorkColumnId, `--${string}`>> = {
  key: '--my-work-key-width',
  title: '--my-work-title-min',
  state: '--my-work-state-width',
  priority: '--my-work-priority-width',
  due: '--my-work-due-width',
  project: '--my-work-project-width',
  assignee: '--my-work-assignee-width',
};

/**
 * 拉過的標題欄夾在表格寬度（MyWorkGrid 包住捲動區塊的容器 my-work-table 的 100cqi）減掉它左邊固定的欄與
 * --my-work-scroll-reserve 之內：寬螢幕拉寬之後開了項目面板、或展開側欄，固定欄跟著縮，其他欄還捲得出來（和專案的表格一樣）。
 */
function variableValue({ id, bounds }: MyWorkColumn, width: number): string {
  if (id !== 'title') return `${String(clampWidth(width, bounds))}px`;
  // 標題欄只夾最小：沒拉過時它吃掉剩下的寬度、可能比 32rem 寬，從那裡往窄拉一點的寬度要留著；上限交給容器。
  const px = `${String(Math.round(Math.max(width, remToPx(bounds.min))))}px`;
  return `min(${px}, 100cqi - var(--my-work-pinned-width) - var(--my-work-scroll-reserve))`;
}

/** 幾欄的寬度加起來（CSS 的式子）；沒有就是 0。 */
function widthSum(ids: readonly MyWorkColumnId[]): string {
  if (ids.length === 0) return '0px';
  const terms = ids.map((id) => `var(${VARIABLES[id]})`);
  return terms.length === 1 ? (terms[0] ?? '0px') : `calc(${terms.join(' + ')})`;
}

/**
 * 固定欄的位置：每一欄的 left 是它左邊固定欄的寬度加起來（--my-work-<欄>-left，MyWork.module.css 的 .pin*）；
 * --my-work-pinned-width 是標題左邊固定欄的寬度（捲動時讓出的寬度、拉寬的標題欄的上限都用它）。
 * 標題一定是最後一個固定欄（myWorkColumnOrder.ts），所以加起來的寬度不會用到它（它沒拉過時寬度不是變數）。
 */
function pinVariables(pinned: readonly MyWorkColumnId[]): [`--${string}`, string][] {
  const offsets = pinned.map((id, index): [`--${string}`, string] => [`--my-work-${id}-left`, widthSum(pinned.slice(0, index))]);
  return [...offsets, ['--my-work-pinned-width', widthSum(pinned.filter((id) => id !== 'title'))]];
}

/**
 * 捲動區塊上的變數：拉過的寬度換掉的（畫在各欄的範圍裡：記住的寬度可能被改壞，{"key": 5000}），
 * 加上固定欄（標題和它左邊的欄，畫面上的順序）的位置。
 */
export function myWorkVariables(widths: ColumnWidths, pinned: readonly MyWorkColumnId[]): TableVariables {
  const sized = COLUMNS.flatMap((column): [`--${string}`, string][] => {
    const width = widths[column.id];
    return width === undefined ? [] : [[VARIABLES[column.id], variableValue(column, width)]];
  });
  return Object.fromEntries([...sized, ...pinVariables(pinned)]);
}

/** 記住的寬度裡有沒有這一頁的欄（別的版本留下的、被改壞的鍵不算）：「重設欄寬」要不要出現。 */
export function hasSavedWidths(saved: ColumnWidths): boolean {
  return COLUMNS.some(({ id }) => saved[id] !== undefined);
}
