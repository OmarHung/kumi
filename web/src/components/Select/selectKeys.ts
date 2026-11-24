import type { KeyboardEvent } from 'react';
import type { SelectOption } from './Select';

/*
 * Select 的按鍵（docs/DESIGN.md 7.3 的鍵盤表）：哪個鍵打開、反白到哪裡。純函式，useSelect.ts 用。
 */

/** PageUp／PageDown 一次移動幾個選項。 */
const PAGE_SIZE = 10;

export function clampIndex(index: number, count: number): number {
  return Math.min(Math.max(index, 0), count - 1);
}

/** 關著時按了會打開的鍵與打開時反白在哪裡（Alt+↓ 的 key 也是 ArrowDown）；不是這些鍵回 null。 */
export function openingIndex(key: string, selected: number, count: number): number | null {
  switch (key) {
    case 'Enter':
    case ' ':
    case 'ArrowDown':
    case 'ArrowUp':
      return selected;
    case 'Home':
      return 0;
    case 'End':
      return count - 1;
    default:
      return null;
  }
}

/** 開著時移動反白的鍵：到頭尾就停，不繞回去（和 Menu 不同：清單是一串值，繞回去看不出到底了）。 */
export function movedIndex(key: string, active: number, count: number): number | null {
  switch (key) {
    case 'ArrowDown':
      return clampIndex(active + 1, count);
    case 'ArrowUp':
      return clampIndex(active - 1, count);
    case 'Home':
      return 0;
    case 'End':
      return count - 1;
    case 'PageDown':
      return clampIndex(active + PAGE_SIZE, count);
    case 'PageUp':
      return clampIndex(active - PAGE_SIZE, count);
    default:
      return null;
  }
}

/**
 * 篩選框裡移動反白的鍵：方向鍵與 PageUp／PageDown。Home、End 留給輸入框（游標到頭尾），
 * 規範 7.3「選項很多的時候」只說方向鍵、Enter、Esc、Tab 和沒有篩選框時一樣。
 */
export function filterMovedIndex(key: string, active: number, count: number): number | null {
  return key === 'Home' || key === 'End' ? null : movedIndex(key, active, count);
}

export function hasCommandModifier(event: KeyboardEvent): boolean {
  return event.ctrlKey || event.metaKey;
}

export function isPrintable(event: KeyboardEvent): boolean {
  return event.key.length === 1 && !hasCommandModifier(event) && !event.altKey;
}

/**
 * 打字跳到開頭符合的選項（不分大小寫）。只打一個字或同一個字重複打：從反白的下一個開始找（輪流）；
 * 打成一串：從反白的這一個開始找（打「bl」時停在 Banana 的還要能留在符合的那一個）。找不到回 null。
 */
export function typeaheadIndex(options: readonly SelectOption[], query: string, active: number): number | null {
  const first = query.charAt(0);
  const isRepeat = query.replaceAll(first, '') === '';
  const needle = (isRepeat ? first : query).toLocaleLowerCase();
  const start = isRepeat ? active + 1 : active;
  for (let step = 0; step < options.length; step += 1) {
    const index = (start + step + options.length) % options.length;
    if (options[index]?.label.toLocaleLowerCase().startsWith(needle) === true) return index;
  }
  return null;
}
