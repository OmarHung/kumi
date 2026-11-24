import { useCallback, useLayoutEffect, useRef, useState, type KeyboardEvent, type RefObject } from 'react';
import { isComposingKey } from '../../lib/keyboard';
import { useFloatingDismiss } from '../Floating/useFloatingDismiss';
import type { SelectDismissReason, SelectOption } from './Select';
import { filterEntries, filterOptions, isFilterable, useFilterQuery, type FilterQuery } from './selectFilter';
import { clampIndex, filterMovedIndex, hasCommandModifier, isPrintable, movedIndex, openingIndex, typeaheadIndex } from './selectKeys';

/** 這麼久之內連續打的字算同一串（打字跳到開頭符合的選項）。 */
const TYPEAHEAD_RESET_MS = 500;

export interface UseSelectArgs {
  readonly options: readonly SelectOption[];
  readonly value: string;
  readonly isOpenInitially: boolean;
  /** 停用（disabled 或 aria-disabled）時打不開。 */
  readonly canOpen: boolean;
  /** 送出中（aria-busy）：打得開，但選了不收新的值。 */
  readonly isBusy: boolean;
  readonly onChange: (value: string) => void;
  readonly onPick: ((value: string) => void) | undefined;
  readonly onDismiss: ((reason: SelectDismissReason) => void) | undefined;
  readonly onKeyDown: ((event: KeyboardEvent<HTMLButtonElement>) => void) | undefined;
}

interface OpenState {
  readonly isOpen: boolean;
  /** 反白的選項（看得到的選項裡第幾個）。選項換了、篩選了時由 clampIndex 夾回範圍裡。 */
  readonly active: number;
}

/** 最新的參數：「點在外面」的監聽與按鍵的處理都讀它，不必因為呼叫端每次給新的函式就重新掛監聽。 */
function useLatest<T>(value: T): RefObject<T> {
  const ref = useRef(value);
  useLayoutEffect(() => {
    ref.current = value;
  });
  return ref;
}

/** 打字跳到選項：記著這一串打到哪裡、最後一個字是什麼時候打的。 */
function useTypeahead() {
  const typed = useRef({ query: '', at: 0 });
  return {
    /** 這個字接在剛才那一串後面（500ms 內），還是重新開始。 */
    next(char: string): string {
      const now = Date.now();
      const query = now - typed.current.at < TYPEAHEAD_RESET_MS ? typed.current.query + char : char;
      typed.current = { query, at: now };
      return query;
    },
    /** 剛才還在打（空白鍵這時是字的一部分，不是「選這一個」）。 */
    isTyping(): boolean {
      return typed.current.query !== '' && Date.now() - typed.current.at < TYPEAHEAD_RESET_MS;
    },
  };
}

/**
 * 焦點在 Select 裡面、卻不在按鈕上（在篩選框裡）時放回按鈕：清單關上之後篩選框跟著不見，焦點才不會掉到頁面本體。
 * 外層是按鈕的父元素（Select.tsx 的 display: contents 容器）。
 */
function focusButtonIfInside(button: HTMLButtonElement | null) {
  const root = button?.parentElement;
  const active = document.activeElement;
  if (button === null || root === null || root === undefined || active === button || !(active instanceof Node) || !root.contains(active)) return;
  button.focus({ preventScroll: true });
}

/**
 * 開關與反白。關上有好幾條路（選了、Esc、Tab、點外面、焦點離開、按鈕被捲出畫面、再按一次按鈕），
 * 而 Tab 之後焦點離開會再觸發一次「焦點離開就關上」：開著沒有用 ref 同步記著，onDismiss 才不會被叫兩次。
 * 點外面以外的關上，焦點在篩選框裡的話先放回按鈕（Tab 也是：瀏覽器接著從按鈕往下走）。
 */
function useOpenState(args: UseSelectArgs, buttonRef: RefObject<HTMLButtonElement | null>, selected: number) {
  const latest = useLatest(args);
  const [state, setState] = useState<OpenState>(() => ({ isOpen: args.isOpenInitially && args.canOpen, active: selected }));
  const isOpenRef = useRef(state.isOpen);
  const dismiss = useCallback(
    (reason: SelectDismissReason) => {
      if (!isOpenRef.current) return;
      isOpenRef.current = false;
      if (reason !== 'outside') focusButtonIfInside(buttonRef.current);
      setState((current) => ({ ...current, isOpen: false }));
      latest.current.onDismiss?.(reason);
    },
    [latest, buttonRef],
  );
  const dismissOutside = useCallback(() => {
    dismiss('outside');
  }, [dismiss]);
  const dismissScrolled = useCallback(() => {
    dismiss('scrolled');
  }, [dismiss]);
  const floating = useFloatingDismiss(state.isOpen, dismissOutside, buttonRef, dismissScrolled);
  return {
    isOpen: state.isOpen,
    active: clampIndex(state.active, args.options.length),
    floating,
    dismiss,
    isOpenNow: () => isOpenRef.current,
    openAt: (index: number) => {
      isOpenRef.current = true;
      setState({ isOpen: true, active: index });
    },
    /** 選了之後關上：不是「沒選就關上」，不叫 onDismiss。 */
    closeAfterPick: () => {
      isOpenRef.current = false;
      focusButtonIfInside(buttonRef.current);
      setState((current) => ({ ...current, isOpen: false }));
    },
    setActive: (index: number) => {
      setState((current) => ({ ...current, active: index }));
    },
  };
}

type OpenControls = ReturnType<typeof useOpenState>;

/** 選了第幾個：不能選的不收；送出中關上但不收新的值；選了目前的值不叫 onChange，onPick 照樣叫。 */
function pickOption(args: UseSelectArgs, open: OpenControls, index: number) {
  const option = args.options[index];
  if (option === undefined || option.disabledReason !== undefined) return;
  open.closeAfterPick();
  if (args.isBusy) return;
  if (option.value !== args.value) args.onChange(option.value);
  args.onPick?.(option.value);
}

interface ClosedKeys {
  readonly selected: number;
  readonly isFilterable: boolean;
  readonly typeTo: TypeTo;
  /** 打開時篩選從頭開始；關著時打的字（有篩選框時）放進篩選框。 */
  readonly resetFilter: (seed?: string) => void;
}

/** 關著時：Enter、空白鍵、方向鍵、Home、End、打字都是打開（不換值）。有篩選框時打的字放進篩選框。回傳有沒有處理。 */
function handleClosedKey(event: KeyboardEvent<HTMLButtonElement>, args: UseSelectArgs, open: OpenControls, keys: ClosedKeys) {
  if (!args.canOpen || hasCommandModifier(event)) return false;
  const index = openingIndex(event.key, keys.selected, args.options.length);
  if (index === null && !isPrintable(event)) return false;
  event.preventDefault();
  if (index === null && keys.isFilterable) {
    keys.resetFilter(event.key);
    open.openAt(0);
    return true;
  }
  keys.resetFilter();
  open.openAt(index ?? keys.typeTo(event.key, keys.selected));
  return true;
}

type TypeTo = (char: string, from: number) => number;

/** 開著時：Esc 與 Tab 關上（值不變）、Enter 與空白鍵選反白的那個、方向鍵與打字移動反白。回傳有沒有處理。 */
function handleOpenKey(event: KeyboardEvent<HTMLButtonElement>, args: UseSelectArgs, open: OpenControls, typeTo: TypeTo, isTyping: boolean) {
  if (event.key === 'Escape') {
    // 不往外傳：對話框、格子、面板才不會跟著反應（規範第 7 節 SidePanel 的順序：清單 → 格子 → 選單 → 面板）。
    event.preventDefault();
    event.stopPropagation();
    open.dismiss('escape');
    return true;
  }
  if (event.key === 'Tab') {
    // 不擋預設動作：焦點照常往下（Shift+Tab 往上）走。
    open.dismiss('tab');
    return true;
  }
  if (event.key === 'Enter' || (event.key === ' ' && !isTyping)) {
    event.preventDefault();
    pickOption(args, open, open.active);
    return true;
  }
  const moved = movedIndex(event.key, open.active, args.options.length);
  if (moved === null && !isPrintable(event)) return false;
  event.preventDefault();
  open.setActive(moved ?? typeTo(event.key, open.active));
  return true;
}

/**
 * 篩選框裡：方向鍵、PageUp、PageDown 移動反白，Enter 選，Esc 關上（不往外傳），Tab 關上照常往下走（焦點先回到按鈕，
 * 瀏覽器再從按鈕往下走）；其他的鍵是打字。輸入法選字中的按鍵都是輸入法的（Esc 也不往外傳，對話框才不會跟著關上）。
 */
function handleFilterKey(event: KeyboardEvent<HTMLElement>, args: UseSelectArgs, open: OpenControls) {
  if (isComposingKey(event)) {
    if (event.key === 'Escape') event.stopPropagation();
    return;
  }
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    open.dismiss('escape');
  } else if (event.key === 'Tab') {
    open.dismiss('tab');
  } else if (event.key === 'Enter') {
    event.preventDefault();
    pickOption(args, open, open.active);
  } else {
    const moved = filterMovedIndex(event.key, open.active, args.options.length);
    if (moved === null) return;
    event.preventDefault();
    open.setActive(moved);
  }
}

/** 篩選框交給 SelectList 的東西：字、改字、輸入法、按鍵。 */
export interface SelectFilterControls {
  readonly text: string;
  /** 拿來篩選的字（輸入法選字中不換）：結果數照它唸。 */
  readonly query: string;
  readonly onTextChange: (text: string) => void;
  readonly onCompositionStart: () => void;
  readonly onCompositionEnd: (text: string) => void;
  readonly onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
}

/** 篩選框的控制：改字（選字中不篩）、輸入法、按鍵。篩選字變了：清空時反白回到目前的值，有字時到第一個符合的。 */
function filterControlsOf(filter: FilterQuery, args: UseSelectArgs, shown: UseSelectArgs, open: OpenControls): SelectFilterControls {
  function applyQuery(query: string | null) {
    if (query === null) return;
    const next = filterOptions(args.options, query);
    open.setActive(query.trim() === '' ? Math.max(next.findIndex((option) => option.value === args.value), 0) : 0);
  }
  return {
    text: filter.text,
    query: filter.query,
    onTextChange: (text) => {
      applyQuery(filter.change(text));
    },
    onCompositionStart: filter.startComposition,
    onCompositionEnd: (text) => {
      applyQuery(filter.endComposition(text));
    },
    onKeyDown: (event) => {
      handleFilterKey(event, shown, open);
    },
  };
}

/** 篩選框（在清單裡，清單在按鈕旁邊的 DOM：外層容器裡唯一的輸入框）。 */
function filterInputOf(button: HTMLButtonElement | null): HTMLInputElement | null {
  return button?.parentElement?.querySelector('input') ?? null;
}

/**
 * 清單開著、焦點卻在按鈕上（在按鈕上按下滑鼠、拖出去才放開，click 沒發生）：按鍵轉給篩選框——焦點移過去，
 * 打的字接在篩選框的字後面，方向鍵、Enter 照篩選後的清單算（不是用完整清單的打字跳轉）。Tab 照按鈕原本的處理。
 */
function forwardToFilter(event: KeyboardEvent<HTMLButtonElement>, controls: SelectFilterControls) {
  if (event.key !== 'Escape') filterInputOf(event.currentTarget)?.focus({ preventScroll: true });
  if (isPrintable(event)) {
    event.preventDefault();
    controls.onTextChange(`${controls.text}${event.key}`);
    return;
  }
  controls.onKeyDown(event);
}

/** 打開時焦點不在按鈕上（Safari 透過 <label> 打開：點標籤不給按鈕焦點）就補一次，之後的按鍵、「焦點離開就關上」才接得到。 */
function focusButton(button: HTMLButtonElement | null) {
  if (button !== null && document.activeElement !== button) button.focus({ preventScroll: true });
}

/** 按按鈕：開著就關上（toggle）；關著就打開在目前的值，篩選從頭開始，焦點不在按鈕上的話補一次。 */
function toggleOpen(args: UseSelectArgs, open: OpenControls, filter: FilterQuery, button: HTMLButtonElement | null, selected: number) {
  if (!args.canOpen) return;
  if (open.isOpenNow()) {
    open.dismiss('toggle');
    return;
  }
  focusButton(button);
  filter.reset();
  open.openAt(selected);
}

/**
 * 下拉的開關、反白與鍵盤（docs/DESIGN.md 7.3）。沒有篩選框時焦點一直在按鈕上；選項多（10 個以上）時打開之後焦點在篩選框，
 * 反白都用 aria-activedescendant 表示。值只有明確選了（點選、Enter、空白鍵）才變；方向鍵與打字只移動反白或篩選。
 */
export function useSelect(args: UseSelectArgs, buttonRef: RefObject<HTMLButtonElement | null>) {
  const filterable = isFilterable(args.options);
  const filter = useFilterQuery();
  const entries = filterEntries(args.options, filterable ? filter.query : '');
  const visible = entries.map((entry) => entry.option);
  const shown: UseSelectArgs = { ...args, options: visible };
  const selected = Math.max(
    args.options.findIndex((option) => option.value === args.value),
    0,
  );
  const open = useOpenState(shown, buttonRef, selected);
  const typeahead = useTypeahead();
  /** 打了一個字：反白跳到符合的選項；找不到就不動（關著時打開在目前的值）。 */
  const typeTo: TypeTo = (char, from) => typeaheadIndex(args.options, typeahead.next(char), from) ?? from;

  const controls = filterControlsOf(filter, args, shown, open);

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    // 輸入法選字中的按鍵（注音的 Enter、方向鍵、Esc）都是輸入法的。
    if (isComposingKey(event)) return;
    if (open.isOpenNow() && filterable && event.key !== 'Tab') {
      forwardToFilter(event, controls);
      return;
    }
    const isHandled = open.isOpenNow()
      ? handleOpenKey(event, shown, open, typeTo, typeahead.isTyping())
      : handleClosedKey(event, args, open, { selected, isFilterable: filterable, typeTo, resetFilter: filter.reset });
    if (!isHandled) args.onKeyDown?.(event);
  }

  return {
    isOpen: open.isOpen,
    active: open.active,
    visible,
    /** 看得到的每一個選項在完整清單裡的位置：選項的 id 照它編。 */
    positions: entries.map((entry) => entry.index),
    isFilterable: filterable,
    filter: controls,
    rootRef: open.floating.rootRef,
    handleRootBlur: open.floating.handleBlur,
    closeWhenAnchorHidden: open.floating.closeWhenAnchorHidden,
    handleKeyDown,
    toggle: () => {
      toggleOpen(args, open, filter, buttonRef.current, selected);
    },
    pick: (index: number) => {
      pickOption(shown, open, index);
    },
    setActive: open.setActive,
  };
}
