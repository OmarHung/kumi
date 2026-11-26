import { useSyncExternalStore } from 'react';
import type { MyWorkColumnId } from './myWorkColumns';

/*
 * 「我的工作」表格的欄的順序（docs/DESIGN.md 8.3「我的工作」）：拖表頭或用「欄的順序」調，所有組共用一份，
 * 記在這個瀏覽器（個人的顯示偏好，8.3「網址就是狀態」），讀寫失敗當成沒有設定。
 *
 * 只有一條規則：**標題和它左邊的欄固定在左邊**（橫向捲動時認得出是哪一個項目），所以標題左邊只放得下
 * 認得出項目的欄——編號與專案。狀態、優先度、到期日放到標題左邊的話，固定的欄會寬到擠掉其他欄、捲不到右邊。
 */

export const MY_WORK_ORDER_KEY = 'my-work.column-order';

/**
 * 預設：編號、專案、標題、狀態、優先度、到期日（使用者 2026-11-21：專案放在標題左邊），負責人在最後：
 * 它只在看「我建立的」時才畫出來，放在最後不動到原本的欄的位置。
 */
export const DEFAULT_COLUMN_ORDER: readonly MyWorkColumnId[] = ['key', 'project', 'title', 'state', 'priority', 'due', 'assignee'];

/** 可以放在標題左邊（固定在左邊）的欄。 */
const LEADING: ReadonlySet<MyWorkColumnId> = new Set<MyWorkColumnId>(['key', 'project']);

function isColumnId(value: unknown): value is MyWorkColumnId {
  return DEFAULT_COLUMN_ORDER.some((id) => id === value);
}

function isValid(order: readonly MyWorkColumnId[]): boolean {
  return order.slice(0, order.indexOf('title')).every((id) => LEADING.has(id));
}

/** 少了的欄補在預設順序裡它前面那一欄的後面（前面沒有就放最前面）；負責人補在最後。 */
function withMissing(known: readonly MyWorkColumnId[]): readonly MyWorkColumnId[] {
  return DEFAULT_COLUMN_ORDER.reduce<readonly MyWorkColumnId[]>((order, id, index) => {
    if (order.includes(id)) return order;
    const before = DEFAULT_COLUMN_ORDER[index - 1];
    const at = before === undefined ? 0 : order.indexOf(before) + 1;
    return [...order.slice(0, at), id, ...order.slice(at)];
  }, known);
}

/** 放在標題左邊、但不能放在那裡的欄，照原本的先後移到標題的右邊。 */
function withValidLeading(order: readonly MyWorkColumnId[]): readonly MyWorkColumnId[] {
  const titleAt = order.indexOf('title');
  const misplaced = order.slice(0, titleAt).filter((id) => !LEADING.has(id));
  if (misplaced.length === 0) return order;
  const rest = order.filter((id) => !misplaced.includes(id));
  const at = rest.indexOf('title') + 1;
  return [...rest.slice(0, at), ...misplaced, ...rest.slice(at)];
}

/** 記住的順序整理成一份完整、符合規則的順序：不認得的、重複的拿掉，少了的補回去，放錯邊的移到標題右邊。 */
export function normalizeColumnOrder(raw: unknown): readonly MyWorkColumnId[] {
  if (!Array.isArray(raw)) return DEFAULT_COLUMN_ORDER;
  const known = [...new Set(raw.filter(isColumnId))];
  return withValidLeading(withMissing(known));
}

/** 固定在左邊的欄：標題和它左邊的（照畫面上的順序）。 */
export function pinnedColumnsOf(visible: readonly MyWorkColumnId[]): readonly MyWorkColumnId[] {
  return visible.slice(0, visible.indexOf('title') + 1);
}

/**
 * 把 id 放到看得到的欄的第 slot 個交界（0 是最前面，visible.length 是最後面）。看不到的欄（依專案分組時的專案）
 * 留在原本的相對位置：放在交界右邊那一欄的前面，最後面的交界是最後一個看得到的欄的後面。
 */
function placed(order: readonly MyWorkColumnId[], visible: readonly MyWorkColumnId[], id: MyWorkColumnId, slot: number): readonly MyWorkColumnId[] {
  const rest = order.filter((candidate) => candidate !== id);
  const anchor = visible[slot];
  if (anchor !== undefined) {
    const at = rest.indexOf(anchor);
    return [...rest.slice(0, at), id, ...rest.slice(at)];
  }
  const last = visible.at(-1);
  const at = last === undefined ? rest.length : rest.indexOf(last) + 1;
  return [...rest.slice(0, at), id, ...rest.slice(at)];
}

/**
 * 拖曳放開：id 拖到看得到的欄的第 slot 個交界。放不下的地方（狀態拖到標題左邊、標題拖到狀態右邊）改放最近的、
 * 放得下的交界；最近的就是原位時是沒有變化（null）。
 */
export function dropColumn(
  order: readonly MyWorkColumnId[],
  visible: readonly MyWorkColumnId[],
  id: MyWorkColumnId,
  slot: number,
): readonly MyWorkColumnId[] | null {
  const from = visible.indexOf(id);
  if (from < 0) return null;
  const slots = Array.from({ length: visible.length + 1 }, (_, index) => index).toSorted(
    (a, b) => Math.abs(a - slot) - Math.abs(b - slot) || a - b,
  );
  for (const candidate of slots) {
    if (candidate === from || candidate === from + 1) return null;
    const next = placed(order, visible, id, candidate);
    if (isValid(next)) return next;
  }
  return null;
}

/**
 * 拖到第 slot 個交界時，放開實際會放在現在畫面上的第幾個交界（畫放下去的位置用）；沒有變化是 null。
 * 用 dropColumn 的結果算：被拖的欄在新順序裡右邊那一欄的左緣，沒有右邊的欄就是最後面。
 */
export function resolveDropSlot(
  order: readonly MyWorkColumnId[],
  visible: readonly MyWorkColumnId[],
  id: MyWorkColumnId,
  slot: number,
): number | null {
  const next = dropColumn(order, visible, id, slot);
  if (next === null) return null;
  const shown = next.filter((candidate) => visible.includes(candidate));
  const after = shown[shown.indexOf(id) + 1];
  return after === undefined ? visible.length : visible.indexOf(after);
}

/** 往前（-1）或往後（1）越過一個看得到的欄；不能動（已經在最前、最後，或越過之後不符合規則）是 null。 */
export function stepColumn(
  order: readonly MyWorkColumnId[],
  visible: readonly MyWorkColumnId[],
  id: MyWorkColumnId,
  step: -1 | 1,
): readonly MyWorkColumnId[] | null {
  const from = visible.indexOf(id);
  const slot = step === -1 ? from - 1 : from + 2;
  if (from < 0 || slot < 0 || slot > visible.length) return null;
  const next = placed(order, visible, id, slot);
  return isValid(next) ? next : null;
}

export interface ColumnOrderState {
  readonly order: readonly MyWorkColumnId[];
  /** 調過順序（記在這個瀏覽器的和預設不同）：「回到預設的順序」看它。 */
  readonly isCustom: boolean;
}

export interface ColumnOrderStore {
  readonly subscribe: (listener: () => void) => () => void;
  readonly getSnapshot: () => ColumnOrderState;
  readonly set: (order: readonly MyWorkColumnId[]) => void;
  readonly reset: () => void;
}

function isDefault(order: readonly MyWorkColumnId[]): boolean {
  return order.length === DEFAULT_COLUMN_ORDER.length && order.every((id, index) => id === DEFAULT_COLUMN_ORDER[index]);
}

function stateOf(order: readonly MyWorkColumnId[]): ColumnOrderState {
  return isDefault(order) ? { order: DEFAULT_COLUMN_ORDER, isCustom: false } : { order, isCustom: true };
}

// localStorage 可能被停用（私密瀏覽、政策）或內容被改壞：讀不到就當成沒有設定。
function read(storageKey: string): readonly MyWorkColumnId[] {
  try {
    const raw = localStorage.getItem(storageKey);
    return raw === null ? DEFAULT_COLUMN_ORDER : normalizeColumnOrder(JSON.parse(raw));
  } catch {
    return DEFAULT_COLUMN_ORDER;
  }
}

function write(storageKey: string, order: readonly MyWorkColumnId[]): void {
  try {
    if (isDefault(order)) localStorage.removeItem(storageKey);
    else localStorage.setItem(storageKey, JSON.stringify(order));
  } catch {
    // 存不了只是下次回到預設的順序，不必打斷使用者。
  }
}

/**
 * 欄的順序，所有組的表格與「欄的順序」共用（小的外部狀態，和欄寬的 store 一樣：components/Table/columnWidths.ts）。
 * 存進去的一律先整理（normalizeColumnOrder）：呼叫端給錯也不會留下不符合規則的順序。
 */
export function createColumnOrderStore(storageKey: string): ColumnOrderStore {
  let state = stateOf(read(storageKey));
  const listeners = new Set<() => void>();
  const publish = (order: readonly MyWorkColumnId[]) => {
    state = stateOf(order);
    write(storageKey, order);
    listeners.forEach((listener) => {
      listener();
    });
  };
  return {
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    getSnapshot: () => state,
    set: (order) => {
      publish(normalizeColumnOrder(order));
    },
    reset: () => {
      publish(DEFAULT_COLUMN_ORDER);
    },
  };
}

export function useColumnOrder(store: ColumnOrderStore): ColumnOrderState {
  return useSyncExternalStore(store.subscribe, store.getSnapshot);
}
