import type { StateCategory, Workflow } from '../../../api/blueprint';
import type { Item } from '../../../api/types';
import { moveAvailability, moveTargets, type MoveAvailability, type MoveTarget } from '../../../domain/workflow';
import { OPEN_CATEGORIES } from '../../../domain/dueFilter';
import { STATE_CATEGORIES, statusCategories, type MyWorkStatus } from '../../../domain/myWorkView';

export interface BoardCategoryContext {
  /** 選了的專案、而且讀到了它的內容：流程裡有狀態的類別。給了就不畫流程沒有的類別（Scrum 沒有「待規劃」）。 */
  readonly projectCategories?: readonly StateCategory[];
  /** 畫面上的卡片（含這次移過去、留著的）所在的類別：沒有那一欄卡片就不見了。 */
  readonly occupied?: readonly StateCategory[];
}

/**
 * 看板有哪些欄（internal/HANDOFF-my-work-views.md 5.3），照 STATE_CATEGORIES 的順序。
 * 預設的條件（未結束的狀態）多一欄「完成」：不是條件的一部分，是放下去的目標，裡面只有這次看的期間移過去的卡片。
 * 某一個狀態時欄是它的類別；stateCategoriesOfState 是那個狀態在專案藍圖裡的類別（還沒讀到就是空的，欄也還沒有）。
 * 照規則算出來的欄，先拿掉選了的專案流程裡沒有的類別，再加上卡片所在的類別（移到這一頁沒有那一欄的類別，例如已取消）。
 */
export function boardCategories(
  status: MyWorkStatus,
  stateCategoriesOfState: readonly StateCategory[],
  { projectCategories, occupied = [] }: BoardCategoryContext = {},
): readonly StateCategory[] {
  const listed = statusCategories(status) ?? (status.kind === 'all' ? STATE_CATEGORIES : stateCategoriesOfState);
  const byRule: readonly StateCategory[] = status.kind === 'open' ? [...OPEN_CATEGORIES, 'done'] : listed;
  const inProject = projectCategories === undefined ? byRule : byRule.filter((category) => projectCategories.includes(category));
  return STATE_CATEGORIES.filter((category) => inProject.includes(category) || occupied.includes(category));
}

function dueRank(item: Item): string {
  // 沒有到期日的最後：比任何日期字串都大。
  return item.dueDate ?? '9999-99-99';
}

/** 欄內的順序：到期日早的在前，沒有到期日的最後，再依專案代號與編號。 */
export function compareCards(a: Item, b: Item): number {
  return dueRank(a).localeCompare(dueRank(b)) || a.projectKey.localeCompare(b.projectKey) || a.number - b.number;
}

/**
 * 拖到某一欄（類別）之後要做什麼：
 * - ignore：放回自己的類別（類別裡換狀態用「移到…」），或還不知道流程；
 * - move：那個類別只有一個走得到的狀態，直接移；
 * - choose：好幾個走得到，問要哪一個（選單裡列那個類別全部的狀態，走不到的停用並寫出原因）；
 * - blocked：一個都走不到：照樣送出第一個目標（角色的說明優先），以後端的說明為準，和專案的看板一樣；
 * - empty：這個流程沒有那個類別的狀態，沒有東西可送。
 */
export type DropPlan =
  | { readonly kind: 'ignore' }
  | { readonly kind: 'move'; readonly to: string }
  | { readonly kind: 'choose'; readonly targets: readonly MoveTarget[] }
  | { readonly kind: 'blocked'; readonly to: string; readonly availability: MoveAvailability }
  | { readonly kind: 'empty' };

export function planDrop(workflow: Workflow | undefined, item: Item, category: StateCategory, myRole: string | null): DropPlan {
  if (workflow === undefined || item.stateCategory === category) return { kind: 'ignore' };
  const targets = workflow.states
    .filter((state) => state.category === category)
    .map((state) => ({ state, availability: moveAvailability(workflow, item.state, state.key, myRole) }));
  const open = targets.filter((target) => target.availability.kind === 'open');
  const [first] = targets;
  if (first === undefined) return { kind: 'empty' };
  if (open.length > 1) return { kind: 'choose', targets };
  const [only] = open;
  if (only !== undefined) return { kind: 'move', to: only.state.key };
  const explained = targets.find((target) => target.availability.kind === 'role') ?? first;
  return { kind: 'blocked', to: explained.state.key, availability: explained.availability };
}

export interface GroupedMoveTarget {
  readonly target: MoveTarget;
  /** 前面畫分隔線：換了一組（走不到的另成一組，其餘依類別）。 */
  readonly startsGroup: boolean;
}

function byCategory(targets: readonly MoveTarget[]): readonly MoveTarget[] {
  return STATE_CATEGORIES.flatMap((category) => targets.filter((target) => target.state.category === category));
}

/** 「移到…」的選項：走得到的在前、走不到的在後，各自依類別分組（組與組之間一條線）。 */
export function groupedMoveTargets(workflow: Workflow, item: Item, myRole: string | null): readonly GroupedMoveTarget[] {
  const all = moveTargets(workflow, item.state, myRole);
  const isOpen = (target: MoveTarget) => target.availability.kind === 'open';
  const ordered = [...byCategory(all.filter(isOpen)), ...byCategory(all.filter((target) => !isOpen(target)))];
  return ordered.map((target, index) => {
    const previous = ordered[index - 1];
    const startsGroup =
      previous !== undefined && (isOpen(previous) !== isOpen(target) || previous.state.category !== target.state.category);
    return { target, startsGroup };
  });
}

/**
 * 這一欄要畫的卡片：伺服器的結果，拿掉正在送出、要移去別欄的，再放進正在移過來的，照欄內的順序排。
 * 送出中的移動不寫進快取：背景重抓帶回舊位置也不會出現兩張；失敗時什麼都不必復原，卡片自然回到原欄。
 * categoryOfMove 是移動的目標狀態在那張卡片自己專案裡的類別（讀不到藍圖時是 undefined，卡片先不畫在任何欄）。
 */
export function columnItems(
  all: readonly Item[],
  category: StateCategory,
  pending: readonly { readonly item: Item; readonly to: string }[],
  categoryOfMove: (move: { readonly item: Item; readonly to: string }) => StateCategory | undefined,
): readonly Item[] {
  const moving = new Set(pending.map((move) => move.item.key));
  const staying = all.filter((item) => !moving.has(item.key) && item.stateCategory === category);
  const arriving = pending
    .filter((move) => categoryOfMove(move) === category)
    .map((move) => ({ ...move.item, state: move.to, stateCategory: category }));
  return [...staying, ...arriving].toSorted(compareCards);
}
