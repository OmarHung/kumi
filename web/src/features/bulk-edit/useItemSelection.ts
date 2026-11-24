import { useCallback, useId, useMemo, useRef, useState } from 'react';
import type { Item } from '../../api/types';
import { updateThenFocus } from '../../lib/updateThenFocus';

/*
 * 專案的表格的多選（docs/DESIGN.md 7.1「多選」）。選取記的是編號：換條件、排序、版面、專案時清空（scope 換了），
 * 重抓之後不在清單裡的編號拿掉。工具列（批次動作列）與表格是兩個地方，所以狀態放在 useItemTableState。
 */

const NO_KEYS: ReadonlySet<string> = new Set();
const NO_ITEMS: readonly Item[] = [];

interface SelectionState {
  /** 這份選取屬於哪一組條件；和現在的 scope 不同就當成沒有選。 */
  readonly scope: string;
  readonly keys: ReadonlySet<string>;
  /** 目前載入、看得到的項目（表格的順序）：全選、Shift 範圍與「取消選取之後」都以它為準。 */
  readonly loaded: readonly Item[];
}

export type SelectionLevel = 'none' | 'some' | 'all';

export interface ItemSelection {
  readonly count: number;
  /** 選取的項目，照表格的順序。 */
  readonly selectedItems: readonly Item[];
  readonly level: SelectionLevel;
  readonly isSelected: (itemKey: string) => boolean;
  /** 勾選或取消一列；range 是 Shift＋點：從上一次點的那一列到這一列整段，跟著這一次點的結果。 */
  readonly toggle: (itemKey: string, range?: boolean) => void;
  /** 表頭的勾選框：全部已載入的項目都選了就全部取消，否則全選。 */
  readonly toggleAll: () => void;
  readonly clear: () => void;
  /** 取消選取：工具列換回去，焦點回到表頭的勾選框（按的元素跟著動作列一起不見了）。 */
  readonly cancel: () => void;
  /** 只留這些編號（批次操作之後，失敗的留在選取裡）。 */
  readonly keep: (itemKeys: readonly string[]) => void;
  /** 表格載入、換了的項目：不在裡面的編號從選取拿掉。 */
  readonly syncLoaded: (items: readonly Item[]) => void;
  /** 表頭的勾選框的 id；取消選取之後焦點回到它。 */
  readonly headerId: string;
}

function rangeBetween(items: readonly Item[], fromKey: string, toKey: string): readonly string[] {
  const from = items.findIndex((item) => item.key === fromKey);
  const to = items.findIndex((item) => item.key === toKey);
  if (from < 0 || to < 0) return [toKey];
  return items.slice(Math.min(from, to), Math.max(from, to) + 1).map((item) => item.key);
}

function withKeys(keys: ReadonlySet<string>, changed: readonly string[], isChecked: boolean): ReadonlySet<string> {
  const next = new Set(keys);
  changed.forEach((key) => {
    if (isChecked) next.add(key);
    else next.delete(key);
  });
  return next;
}

/** 載入的項目換了：不在裡面的編號拿掉；沒有變的時候回同一份，不讓每次載入都多重畫一次整個專案頁。 */
function withLoaded(current: SelectionState, items: readonly Item[]): SelectionState {
  const present = new Set(items.map((item) => item.key));
  const kept = [...current.keys].filter((key) => present.has(key));
  const keys = kept.length === current.keys.size ? current.keys : new Set(kept);
  const isSameList = items === current.loaded || (items.length === 0 && current.loaded.length === 0);
  return keys === current.keys && isSameList ? current : { ...current, keys, loaded: items };
}

function toggledKeys(current: SelectionState, itemKey: string, anchor: string | null, range: boolean): ReadonlySet<string> {
  const isChecked = !current.keys.has(itemKey);
  const changed = range && anchor !== null ? rangeBetween(current.loaded, anchor, itemKey) : [itemKey];
  return withKeys(current.keys, changed, isChecked);
}

function allToggled(current: SelectionState): ReadonlySet<string> {
  const isAll = current.loaded.length > 0 && current.loaded.every((item) => current.keys.has(item.key));
  return isAll ? NO_KEYS : new Set(current.loaded.map((item) => item.key));
}

export function useItemSelection(scope: string): ItemSelection {
  const headerId = useId();
  const [state, setState] = useState<SelectionState>({ scope, keys: NO_KEYS, loaded: NO_ITEMS });
  const anchorRef = useRef<string | null>(null);
  const keys = state.scope === scope ? state.keys : NO_KEYS;
  // 還沒同步到的編號（重抓與狀態更新之間）不算：讀的時候就篩一次。
  const selectedItems = useMemo(() => state.loaded.filter((item) => keys.has(item.key)), [state.loaded, keys]);
  const count = selectedItems.length;
  const level: SelectionLevel = count === 0 ? 'none' : count === state.loaded.length ? 'all' : 'some';

  const update = useCallback(
    (change: (current: SelectionState) => ReadonlySet<string>) => {
      setState((current) => {
        const base = current.scope === scope ? current : { ...current, scope, keys: NO_KEYS };
        const next = change(base);
        return next === base.keys && base === current ? current : { ...base, keys: next };
      });
    },
    [scope],
  );
  const clear = useCallback(() => {
    anchorRef.current = null;
    update(() => NO_KEYS);
  }, [update]);
  const toggle = useCallback(
    (itemKey: string, range = false) => {
      const anchor = anchorRef.current;
      anchorRef.current = itemKey;
      update((current) => toggledKeys(current, itemKey, anchor, range));
    },
    [update],
  );
  const cancel = useCallback(() => {
    updateThenFocus(clear, [headerId]);
  }, [clear, headerId]);
  const keep = useCallback(
    (itemKeys: readonly string[]) => {
      update(() => new Set(itemKeys));
    },
    [update],
  );
  const toggleAll = useCallback(() => {
    update(allToggled);
  }, [update]);
  const syncLoaded = useCallback((items: readonly Item[]) => {
    setState((current) => withLoaded(current, items));
  }, []);
  const isSelected = useCallback((itemKey: string) => keys.has(itemKey), [keys]);

  return { count, selectedItems, level, isSelected, toggle, toggleAll, clear, cancel, keep, syncLoaded, headerId };
}
