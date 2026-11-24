import { useCallback, useLayoutEffect, useRef, useState, type FocusEvent, type KeyboardEvent, type KeyboardEventHandler, type PointerEvent, type RefObject } from 'react';
import type { CalendarDate } from '../../api/types';
import { today } from '../../domain/dates';
import { focusableIn } from '../../lib/focusable';
import { isComposingKey, preventComposingSubmit } from '../../lib/keyboard';
import { useWeekStart, type WeekStart } from '../../lib/weekStart';
import { useFloatingDismiss } from '../Floating/useFloatingDismiss';
import { displayText, isCalendarDate, normalizeTyped } from './calendarDates';
import { DAYS_VIEW, focusKeyOf, viewControls, type CalendarView, type ViewControls, type ViewState } from './calendarView';
import { gridMoveTarget } from './datePickerKeys';

/** 沒選就關上日曆的原因（和 Select 的一樣）。scrolled 是輸入框被捲出看得到的範圍；toggle 目前沒有入口（見 DatePicker.tsx）。 */
export type DatePickerDismissReason = 'escape' | 'outside' | 'tab' | 'toggle' | 'scrolled';

export interface UseDatePickerArgs {
  readonly value: string;
  /** 停用與唯讀打不開。 */
  readonly canOpen: boolean;
  /** 送出中（aria-busy）：打得開，但選了不收新的值。 */
  readonly isBusy: boolean;
  readonly isOpenInitially: boolean;
  readonly onChange: (value: string) => void;
  readonly onPick: ((value: string) => void) | undefined;
  readonly onDismiss: ((reason: DatePickerDismissReason) => void) | undefined;
  readonly onKeyDown: KeyboardEventHandler<HTMLInputElement> | undefined;
  readonly onBlur: ((event: FocusEvent<HTMLElement>) => void) | undefined;
}

/** 最新的參數：「點在外面」的監聽與按鍵的處理都讀它，不必因為呼叫端每次給新的函式就重新掛監聽。 */
function useLatest<T>(value: T): RefObject<T> {
  const ref = useRef(value);
  useLayoutEffect(() => {
    ref.current = value;
  });
  return ref;
}

/** 打開時焦點那一格：選的那一天，沒有值（或值不是日期）時是今天。 */
function startingCursor(value: string): CalendarDate {
  return isCalendarDate(value) ? value : today();
}

/**
 * 焦點在日曆裡時放回輸入框：日曆關上之後跟著不見，焦點才不會掉到頁面本體（對話框的焦點陷阱、面板的 Esc 都靠焦點在原地）。
 * 點在外面也一樣，而且在按下的當下（pointerdown）就放：點的是另一個停得到的元素時，瀏覽器接著把焦點移過去（不會被搶回來），
 * 焦點從輸入框離開，呼叫端的 onBlur 也照樣叫；點的是停不到的地方（對話框的背景）就留在輸入框。
 */
function returnFocus(calendarRef: RefObject<HTMLElement | null>, inputRef: RefObject<HTMLInputElement | null>) {
  const active = document.activeElement;
  if (active instanceof Node && calendarRef.current?.contains(active) === true) inputRef.current?.focus({ preventScroll: true });
}

/**
 * 開關。關上有好幾條路（選了、Esc、Tab、點外面、焦點離開、輸入框被捲出畫面），Tab 之後焦點離開會再觸發一次
 * 「焦點離開就關上」：開著沒有用 ref 同步記著，onDismiss 才不會被叫兩次。
 */
function useOpenState(args: UseDatePickerArgs, inputRef: RefObject<HTMLInputElement | null>, calendarRef: RefObject<HTMLElement | null>) {
  const latest = useLatest(args);
  const [isOpen, setIsOpen] = useState(() => args.isOpenInitially && args.canOpen);
  const isOpenRef = useRef(isOpen);
  const dismiss = useCallback(
    (reason: DatePickerDismissReason) => {
      if (!isOpenRef.current) return;
      isOpenRef.current = false;
      returnFocus(calendarRef, inputRef);
      setIsOpen(false);
      latest.current.onDismiss?.(reason);
    },
    [latest, calendarRef, inputRef],
  );
  const dismissOutside = useCallback(() => {
    dismiss('outside');
  }, [dismiss]);
  const dismissScrolled = useCallback(() => {
    dismiss('scrolled');
  }, [dismiss]);
  const floating = useFloatingDismiss(isOpen, dismissOutside, inputRef, dismissScrolled);
  return {
    isOpen,
    floating,
    dismiss,
    isOpenNow: () => isOpenRef.current,
    open: () => {
      isOpenRef.current = true;
      setIsOpen(true);
    },
    /** 選了之後關上：不是「沒選就關上」，不叫 onDismiss。 */
    closeAfterPick: () => {
      isOpenRef.current = false;
      returnFocus(calendarRef, inputRef);
      setIsOpen(false);
    },
  };
}

/**
 * 日曆裡焦點那一格（roving tabindex 的那一格，也決定顯示哪個月、哪一年）。用鍵盤移動、↓ 進入日曆、換畫面時要把焦點放上去：
 * 新的那一格在下一次畫面更新才有，所以記下「要放焦點」，畫好之後（layout effect）再放。格子用 data-focus-key 找（calendarView.ts）。
 */
function useCursor(value: string, calendarRef: RefObject<HTMLElement | null>, view: CalendarView) {
  const [cursor, setCursor] = useState(() => startingCursor(value));
  // 最後一次是打字讓日曆換月的：換頁的標題不唸（和打字的回音搶著唸）；按鈕、方向鍵、換畫面造成的才唸。
  const [isTypingDriven, setIsTypingDriven] = useState(false);
  const [focusRequest, setFocusRequest] = useState(0);
  const shouldFocusRef = useRef(false);
  const focusKey = focusKeyOf(view, cursor);
  useLayoutEffect(() => {
    if (!shouldFocusRef.current) return;
    const cell = calendarRef.current?.querySelector<HTMLElement>(`[data-focus-key="${focusKey}"]`);
    if (cell === null || cell === undefined) return;
    shouldFocusRef.current = false;
    cell.focus({ preventScroll: true });
  }, [focusKey, focusRequest, calendarRef]);
  return {
    cursor,
    isTypingDriven,
    moveTo: (date: CalendarDate, focusCell: boolean, isTyping = false) => {
      if (focusCell) {
        shouldFocusRef.current = true;
        setFocusRequest((count) => count + 1);
      }
      setIsTypingDriven(isTyping);
      setCursor(date);
    },
  };
}

type OpenControls = ReturnType<typeof useOpenState>;
type CursorControls = ReturnType<typeof useCursor>;

interface PickContext {
  readonly args: UseDatePickerArgs;
  readonly open: OpenControls;
  readonly clearDraft: () => void;
}

/** 從日曆明確選了（日期、「今天」、「清除」）：關上；送出中不收新的值；和目前一樣不叫 onChange，onPick 照樣叫。 */
function pickDate(context: PickContext, date: string) {
  context.open.closeAfterPick();
  context.clearDraft();
  if (context.args.isBusy) return;
  if (date !== context.args.value) context.args.onChange(date);
  context.args.onPick?.(date);
}

/** 開關、焦點那一格、三層畫面：按鍵與事件的處理都要用到。 */
interface Controls {
  readonly open: OpenControls;
  readonly cursor: CursorControls;
  readonly view: ViewControls;
}

/** 打開：一律從日期畫面開始，焦點那一格從選的那一天（或今天）開始；focusCell 時焦點移進日曆。 */
function openCalendar(args: UseDatePickerArgs, { open, cursor, view }: Controls, focusCell: boolean) {
  if (!args.canOpen) return;
  if (!open.isOpenNow()) {
    open.open();
    view.reset();
    cursor.moveTo(startingCursor(args.value), focusCell);
    return;
  }
  if (focusCell) cursor.moveTo(cursor.cursor, true);
}

/** Esc：月份、年份畫面是回到上一層畫面；日期畫面是關上日曆。不往外傳：格子、對話框、面板才不會跟著反應（一次一層）。 */
function handleEscape(event: KeyboardEvent<HTMLElement>, { open, view }: Controls) {
  event.preventDefault();
  event.stopPropagation();
  if (!view.goUp()) open.dismiss('escape');
}

/**
 * 輸入框的按鍵：↓（或 Alt+↓）打開並把焦點移進日曆；開著時 Esc 往上一層或關上（不往外傳）、Tab 關上（照常往下走）。
 * 回傳有沒有處理；沒處理的交給呼叫端的 onKeyDown（格子的 Enter、關著時的 Esc）。
 */
function handleInputKey(event: KeyboardEvent<HTMLInputElement>, args: UseDatePickerArgs, controls: Controls): boolean {
  const { open } = controls;
  if (event.key === 'ArrowDown' && !event.ctrlKey && !event.metaKey && args.canOpen) {
    event.preventDefault();
    openCalendar(args, controls, true);
    return true;
  }
  if (!open.isOpenNow()) return false;
  if (event.key === 'Escape') {
    handleEscape(event, controls);
    return true;
  }
  if (event.key === 'Tab') {
    // 不擋預設動作：焦點照常往下（Shift+Tab 往上）走，日曆在那之前已經不在了。
    open.dismiss('tab');
    return true;
  }
  return false;
}

/** 日曆裡（焦點在日期格上）：方向鍵、Home、End、PageUp、PageDown 移動焦點那一格，Enter 與空白鍵選它。 */
function handleGridKey(event: KeyboardEvent<HTMLElement>, cursor: CursorControls, pick: (date: string) => void, weekStart: WeekStart) {
  if (isComposingKey(event)) return;
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault();
    pick(cursor.cursor);
    return;
  }
  const target = gridMoveTarget(event.key, event.shiftKey, cursor.cursor, weekStart);
  if (target === null) return;
  event.preventDefault();
  cursor.moveTo(target, true);
}

/**
 * 日曆裡任何地方：Esc 往上一層畫面，日期畫面是關上、焦點回到輸入框（不往外傳）；從最後一個停得到的元素（「清除」）
 * 再按 Tab 是關上並往下走——焦點先回到輸入框，瀏覽器再從輸入框往下走（日曆在那之前已經不在了）。
 */
function handleCalendarKey(event: KeyboardEvent<HTMLElement>, controls: Controls) {
  if (isComposingKey(event)) return;
  if (event.key === 'Escape') {
    handleEscape(event, controls);
    return;
  }
  if (event.key !== 'Tab' || event.shiftKey) return;
  const stops = focusableIn(event.currentTarget);
  if (stops[stops.length - 1] === event.target) controls.open.dismiss('tab');
}

/** 焦點離開整個元件：移到外面的元素，或去了「沒有元素」（切到別的視窗）。 */
function leavesRoot(event: FocusEvent<HTMLElement>): boolean {
  const next = event.relatedTarget;
  return !(next instanceof Node) || !event.currentTarget.contains(next);
}

interface InputContext {
  readonly args: UseDatePickerArgs;
  readonly controls: Controls;
  readonly setDraft: (draft: string | null) => void;
  readonly setKeyboardSuppressed: (isSuppressed: boolean) => void;
}

/** 輸入框的事件：點了打開日曆（焦點留在輸入框）、打字、按鍵。 */
function inputHandlers({ args, controls, setDraft, setKeyboardSuppressed }: InputContext) {
  const { open, cursor } = controls;
  return {
    handleInputClick: () => {
      openCalendar(args, controls, false);
    },
    /**
     * 用手指點開日曆時，輸入框暫時不叫出螢幕鍵盤（inputMode="none"）：鍵盤會蓋住日曆。日曆開著時再點一次輸入框，
     * 就是要打字了，恢復成一般的輸入框；用滑鼠點的照樣可以直接打字。
     */
    handleInputPointerDown: (event: PointerEvent<HTMLInputElement>) => {
      if (event.pointerType !== 'touch') {
        setKeyboardSuppressed(false);
        return;
      }
      setKeyboardSuppressed(!open.isOpenNow() && args.canOpen);
    },
    /** 打字：顯示打的字；解析得了就讓日曆跳到那一天；值變了才叫 onChange（解析不了的傳原始文字）。 */
    handleInputChange: (typed: string) => {
      setDraft(typed);
      const next = normalizeTyped(typed);
      if (isCalendarDate(next)) cursor.moveTo(next, false, true);
      if (next !== args.value) args.onChange(next);
    },
    handleInputKeyDown: (event: KeyboardEvent<HTMLInputElement>) => {
      // 輸入法選字中的按鍵（注音的 Enter、方向鍵、Esc）都是輸入法的；選字的 Enter 也不能送出表單。
      if (isComposingKey(event)) {
        preventComposingSubmit(event);
        return;
      }
      if (!handleInputKey(event, args, controls)) args.onKeyDown?.(event);
    },
    /** 外層的 blur：日曆照「焦點移到外面就關上」；離開整個元件時排好打的字，再叫呼叫端的 onBlur。 */
    handleRootBlur: (event: FocusEvent<HTMLElement>) => {
      open.floating.handleBlur(event);
      if (!leavesRoot(event)) return;
      setDraft(null);
      args.onBlur?.(event);
    },
  };
}

/**
 * 日期欄位的開關、焦點那一格、打的字與鍵盤（docs/DESIGN.md 7.3「日期」，照 WAI-ARIA APG 的 Date Picker Combobox：
 * 焦點留在輸入框，↓ 才進日曆）。打字時顯示打的字（不重新排版，游標不會跳），離開欄位或從日曆選了才排成 2026/12/15。
 */
export function useDatePicker(args: UseDatePickerArgs, inputRef: RefObject<HTMLInputElement | null>, calendarRef: RefObject<HTMLElement | null>) {
  const open = useOpenState(args, inputRef, calendarRef);
  const [viewState, setViewState] = useState<ViewState>(DAYS_VIEW);
  const cursor = useCursor(args.value, calendarRef, viewState.view);
  const view = viewControls(viewState, setViewState, cursor, calendarRef);
  const weekStart = useWeekStart();
  const controls: Controls = { open, cursor, view };
  const [draft, setDraft] = useState<string | null>(null);
  const [isKeyboardSuppressed, setKeyboardSuppressed] = useState(false);
  const clearDraft = () => {
    setDraft(null);
  };
  const pick = (date: string) => {
    pickDate({ args, open, clearDraft }, date);
  };
  // 打的字和值一致（值就是打的字解析出來的）時顯示打的字；外面換了值、或還沒打，就顯示值。
  const text = draft !== null && normalizeTyped(draft) === args.value ? draft : displayText(args.value);
  return {
    isOpen: open.isOpen,
    text,
    /** 用手指點開、日曆開著時是 'none'（不叫出螢幕鍵盤）。 */
    inputMode: open.isOpen && isKeyboardSuppressed ? ('none' as const) : undefined,
    cursor: cursor.cursor,
    isTypingDriven: cursor.isTypingDriven,
    rootRef: open.floating.rootRef,
    closeWhenAnchorHidden: open.floating.closeWhenAnchorHidden,
    pick,
    view,
    ...inputHandlers({ args, controls, setDraft, setKeyboardSuppressed }),
    handleGridKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      handleGridKey(event, cursor, pick, weekStart);
    },
    handleCalendarKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      handleCalendarKey(event, controls);
    },
  };
}

export type DatePickerState = ReturnType<typeof useDatePicker>;
