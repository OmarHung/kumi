import { useSyncExternalStore } from 'react';

/*
 * 表格的欄寬（docs/DESIGN.md 7.1「欄寬」）：欄位 id 對到寬度（px）。兩種來源：
 * - user：使用者拉過（拖曳、點兩下、鍵盤）的寬度。個人的顯示偏好，記在這個瀏覽器的 localStorage（8.3「網址就是狀態」）；
 *   讀寫失敗當成沒有設定。
 * - measured：沒拉過的欄第一次畫出來時照內容量一次的寬度（useMeasuredWidths）。只在記憶體：內容換了下次再量，
 *   存起來反而會留著舊資料的寬度。
 * 畫面上用 effective：拉過的優先，沒拉過的用量出來的，兩個都沒有就是表格 CSS 的預設寬度。
 *
 * 是一個小的外部狀態（useSyncExternalStore），不是上一層的 useState：拖曳中每移一下就換一次寬度，
 * 放在上一層會讓整張表格（50 列、每一格都用專案的 context）跟著重畫；這樣只有欄寬、表頭與捲動區塊的變數重畫。
 */
export type ColumnWidths = Readonly<Record<string, number>>;

export interface ColumnWidthState {
  readonly user: ColumnWidths;
  /** 已經寫進這個瀏覽器的（拖曳中的暫時值不算）：「有沒有調過欄寬」看它。 */
  readonly saved: ColumnWidths;
  readonly measured: ColumnWidths;
  readonly effective: ColumnWidths;
  /** 重設過幾次：重設之後沒拉過的欄要重新量（useMeasuredWidths 看它）。 */
  readonly epoch: number;
}

export interface ColumnWidthStore {
  readonly subscribe: (listener: () => void) => () => void;
  readonly getSnapshot: () => ColumnWidthState;
  /**
   * 換一欄拉的寬度（px；undefined 是拿掉，回到沒拉過）。isFinal 是 false 時只換畫面（拖曳中），
   * 放開或鍵盤調整時才寫進這個瀏覽器。
   */
  readonly setWidth: (columnId: string, width: number | undefined, isFinal?: boolean) => void;
  /** 換掉量出來的寬度（整份）。不寫進這個瀏覽器。 */
  readonly setMeasured: (measured: ColumnWidths) => void;
  /** 回到預設：清掉拉過的與量過的寬度，沒拉過的欄重新量。 */
  readonly reset: () => void;
}

/** 一欄可以調的範圍（rem）：最小、最大。 */
export interface WidthBounds {
  readonly min: number;
  readonly max: number;
}

/** 一般的欄：最小約三個字（3rem），最大 40rem。固定欄另外定（各表格）。 */
export const DEFAULT_BOUNDS: WidthBounds = { min: 3, max: 40 };

/** 鍵盤一次調多少（rem）。 */
export const RESIZE_STEP = 1;

const FALLBACK_ROOT_SIZE = 16;
const EMPTY: ColumnWidths = {};

function isWidths(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

// 內容可能被改壞：只留正的有限數字，其他的欄當成沒有設定。
function read(storageKey: string): ColumnWidths {
  try {
    const raw = localStorage.getItem(storageKey);
    const parsed: unknown = raw === null ? null : JSON.parse(raw);
    if (!isWidths(parsed)) return EMPTY;
    const entries = Object.entries(parsed).flatMap(([id, width]) =>
      typeof width === 'number' && Number.isFinite(width) && width > 0 ? [[id, width] as const] : [],
    );
    return Object.fromEntries(entries);
  } catch {
    return EMPTY;
  }
}

function write(storageKey: string, widths: ColumnWidths): void {
  try {
    if (Object.keys(widths).length === 0) localStorage.removeItem(storageKey);
    else localStorage.setItem(storageKey, JSON.stringify(widths));
  } catch {
    // 存不了只是下次回到預設寬度，不必打斷使用者。
  }
}

function isSame(a: ColumnWidths, b: ColumnWidths): boolean {
  const keys = Object.keys(a);
  return keys.length === Object.keys(b).length && keys.every((key) => a[key] === b[key]);
}

interface Parts {
  readonly user: ColumnWidths;
  readonly saved: ColumnWidths;
  readonly measured: ColumnWidths;
  readonly epoch: number;
}

function stateOf({ user, saved, measured, epoch }: Parts): ColumnWidthState {
  return { user, saved, measured, effective: { ...measured, ...user }, epoch };
}

function withWidth(widths: ColumnWidths, columnId: string, width: number | undefined): ColumnWidths {
  if (width !== undefined) return { ...widths, [columnId]: width };
  return Object.fromEntries(Object.entries(widths).filter(([id]) => id !== columnId));
}

/** 一張表格（或共用欄寬的一組表格）的欄寬，拉過的以 storageKey 記在這個瀏覽器。 */
export function createColumnWidthStore(storageKey: string): ColumnWidthStore {
  const initial = read(storageKey);
  let state = stateOf({ user: initial, saved: initial, measured: EMPTY, epoch: 0 });
  const listeners = new Set<() => void>();
  const publish = (next: ColumnWidthState) => {
    state = next;
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
    setWidth: (columnId, width, isFinal = true) => {
      const user = state.user[columnId] === width ? state.user : withWidth(state.user, columnId, width);
      const saved = isFinal ? user : state.saved;
      if (user !== state.user || saved !== state.saved) publish(stateOf({ ...state, user, saved }));
      if (isFinal) write(storageKey, user);
    },
    setMeasured: (measured) => {
      if (!isSame(measured, state.measured)) publish(stateOf({ ...state, measured }));
    },
    reset: () => {
      publish(stateOf({ user: EMPTY, saved: EMPTY, measured: EMPTY, epoch: state.epoch + 1 }));
      write(storageKey, EMPTY);
    },
  };
}

/** 目前畫面上用的欄寬（拉過的優先，沒拉過的用量出來的）；換了才重畫。 */
export function useColumnWidths(store: ColumnWidthStore): ColumnWidths {
  return useSyncExternalStore(store.subscribe, () => store.getSnapshot().effective);
}

/** 整份狀態（拉過的、量過的、重設的次數）。 */
export function useColumnWidthState(store: ColumnWidthStore): ColumnWidthState {
  return useSyncExternalStore(store.subscribe, store.getSnapshot);
}

/** 根元素的字級（1rem 是幾 px）：預設寬度與範圍用 rem 寫，拉的寬度記的是 px。 */
export function remToPx(rem: number): number {
  const size = Number.parseFloat(getComputedStyle(document.documentElement).fontSize);
  return rem * (Number.isFinite(size) && size > 0 ? size : FALLBACK_ROOT_SIZE);
}

/** 夾在範圍裡、取整數（px）。 */
export function clampWidth(width: number, bounds: WidthBounds): number {
  return Math.round(Math.min(Math.max(width, remToPx(bounds.min)), remToPx(bounds.max)));
}
