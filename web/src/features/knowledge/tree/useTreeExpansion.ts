import { useMemo, useState } from 'react';
import { ancestorsOf, type TreeModel } from './treeModel';

const STORAGE_PREFIX = 'knowledge.tree.expanded.';

function storageKey(spaceKey: string): string {
  return `${STORAGE_PREFIX}${spaceKey}`;
}

function isNumberList(value: unknown): value is readonly number[] {
  return Array.isArray(value) && value.every((entry) => typeof entry === 'number');
}

/** 讀不到、壞掉的內容都當成「都收合」：展開的記憶只是方便，不能因為它讓畫面壞掉。 */
export function readExpanded(spaceKey: string): ReadonlySet<number> {
  try {
    const raw = localStorage.getItem(storageKey(spaceKey));
    const parsed: unknown = raw === null ? [] : JSON.parse(raw);
    return new Set(isNumberList(parsed) ? parsed : []);
  } catch {
    return new Set();
  }
}

function writeExpanded(spaceKey: string, expanded: ReadonlySet<number>): void {
  try {
    localStorage.setItem(storageKey(spaceKey), JSON.stringify([...expanded]));
  } catch {
    // 寫不進去（容量滿了、隱私設定）：這一次沒記住，畫面照舊。
  }
}

/** 目前這一頁的上層一定展開：從網址、搜尋、連結走到深處的頁面，樹上要看得到它在哪。回傳這一次自動加進來的。 */
function missingAncestors(expanded: ReadonlySet<number>, model: TreeModel, currentNumber: number | null): readonly number[] {
  const current = currentNumber === null ? undefined : model.nodeOf(currentNumber);
  if (current === undefined) return [];
  return ancestorsOf(model, current)
    .map((ancestor) => ancestor.number)
    .filter((number) => !expanded.has(number));
}

/** persisted：使用者自己動過的（寫進記憶）；auto：為了讓目前這一頁看得到而自動展開的（不寫進記憶）。 */
interface ExpansionState {
  readonly persisted: ReadonlySet<number>;
  readonly auto: ReadonlySet<number>;
  readonly currentNumber: number | null;
}

function withAuto(state: ExpansionState, model: TreeModel, currentNumber: number | null): ExpansionState {
  const effective = new Set([...state.persisted, ...state.auto]);
  return { ...state, auto: new Set([...state.auto, ...missingAncestors(effective, model, currentNumber)]), currentNumber };
}

/**
 * 頁面樹哪些頁面是展開的：每個空間一份記在 localStorage，換頁、重新整理都在。
 * 換到另一頁時，那一頁的上層會展開（之後使用者可以再收起來）；自動展開的不寫進記憶，使用者自己動過才記
 * （不然只是點過幾頁，下次整棵樹都停在上一次走過的路上）。
 */
export function useTreeExpansion(spaceKey: string, model: TreeModel, currentNumber: number | null) {
  const [state, setState] = useState<ExpansionState>(() => withAuto({ persisted: readExpanded(spaceKey), auto: new Set(), currentNumber }, model, currentNumber));
  // 渲染中調整狀態：換了頁就接著把上層加進來，同一輪就畫對，不會先閃一下收合的樣子。
  if (state.currentNumber !== currentNumber) setState(withAuto(state, model, currentNumber));
  const expanded = useMemo(() => new Set([...state.persisted, ...state.auto]), [state.persisted, state.auto]);

  function setExpanded(pageNumber: number, isExpanded: boolean) {
    if (expanded.has(pageNumber) === isExpanded) return;
    const persisted = new Set(state.persisted);
    const auto = new Set(state.auto);
    // 使用者動過：不管原本是不是自動展開的，從此由他決定。
    auto.delete(pageNumber);
    if (isExpanded) persisted.add(pageNumber);
    else persisted.delete(pageNumber);
    writeExpanded(spaceKey, persisted);
    setState({ ...state, persisted, auto });
  }

  return {
    expanded,
    expand: (pageNumber: number) => {
      setExpanded(pageNumber, true);
    },
    collapse: (pageNumber: number) => {
      setExpanded(pageNumber, false);
    },
    toggle: (pageNumber: number) => {
      setExpanded(pageNumber, !expanded.has(pageNumber));
    },
  };
}
