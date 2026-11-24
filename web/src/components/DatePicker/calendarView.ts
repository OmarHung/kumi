import type { KeyboardEvent, RefObject } from 'react';
import type { CalendarDate } from '../../api/types';
import { isComposingKey } from '../../lib/keyboard';
import { addMonths, dayOfMonth, monthOf, PERIOD_PAGE_SIZE, shiftMonthsKeepingDay, shiftYearsKeepingDay, yearOf, yearPageContaining, yearPageStartFor } from './calendarDates';
import { monthMoveTarget, yearMoveTarget } from './datePickerKeys';

/*
 * 日曆的三層畫面（docs/DESIGN.md 7.3「日期」）：日期 → 月份 → 年份。標題按鈕往下一層（日期 → 月份 → 年份），
 * 選了月份、年份或按 Esc 往上一層。到期日、開始日常要跳到明年或幾個月後，一個月一個月按太慢。
 * 換畫面、換月、換年都不是「選了」：只有選了日期才叫 onChange／onPick。
 */

export type CalendarView = 'days' | 'months' | 'years';

export interface ViewState {
  readonly view: CalendarView;
  /** 進月份畫面時的那一天：在月份、年份之間移動時用它再夾到月底（1 月 31 日經過 2 月到 3 月還是 31 日）。 */
  readonly day: number;
  /** 年份畫面這一頁的第一年。 */
  readonly pageStart: number;
}

/** 打開日曆一律從日期畫面開始。 */
export const DAYS_VIEW: ViewState = { view: 'days', day: 1, pageStart: 0 };

/** 焦點那一格的 data-focus-key：日期畫面是 YYYY-MM-DD，月份畫面是 YYYY-MM，年份畫面是 YYYY。 */
export function focusKeyOf(view: CalendarView, date: CalendarDate): string {
  if (view === 'months') return date.slice(0, 7);
  if (view === 'years') return date.slice(0, 4);
  return date;
}

interface CursorMover {
  readonly cursor: CalendarDate;
  readonly moveTo: (date: CalendarDate, focusCell: boolean) => void;
}

/**
 * 換畫面時，焦點本來就在日曆裡（用鍵盤、或按了日曆裡的按鈕）才把焦點放到新畫面對應的那一格；
 * 焦點在輸入框（用滑鼠點開、照樣可以打字）時不搶。
 */
function isFocusInCalendar(calendarRef: RefObject<HTMLElement | null>): boolean {
  const active = document.activeElement;
  return active instanceof Node && calendarRef.current?.contains(active) === true;
}

/**
 * 焦點在格子上（用鍵盤走進日曆之後，再用滑鼠按左右兩顆按鈕：在日曆上按下滑鼠不帶走焦點）：換頁時原本那一格跟著不見，
 * 要把焦點移到新的一頁對應的那一格，否則掉到頁面本體。焦點在按鈕或輸入框上就不動。
 */
function isFocusOnCell(calendarRef: RefObject<HTMLElement | null>): boolean {
  return isFocusInCalendar(calendarRef) && document.activeElement?.getAttribute('role') === 'gridcell';
}

/** 左右兩顆按鈕：日期畫面一個月、月份畫面一年、年份畫面 12 年（年份畫面連頁面一起換）。焦點在格子上才跟著移。 */
function stepPage(state: ViewState, cursor: CursorMover, setState: (next: ViewState) => void, delta: number, focusCell: boolean) {
  if (state.view === 'days') {
    cursor.moveTo(addMonths(cursor.cursor, delta), focusCell);
  } else if (state.view === 'months') {
    cursor.moveTo(shiftYearsKeepingDay(cursor.cursor, delta, state.day), focusCell);
  } else {
    setState({ ...state, pageStart: state.pageStart + delta * PERIOD_PAGE_SIZE });
    cursor.moveTo(shiftYearsKeepingDay(cursor.cursor, delta * PERIOD_PAGE_SIZE, state.day), focusCell);
  }
}

/** 月份、年份畫面的按鍵：方向鍵、Home、End、PageUp、PageDown 移動焦點那一格（年份走出這一頁就換頁），Enter 與空白鍵選它。 */
function handlePeriodKey(event: KeyboardEvent<HTMLElement>, state: ViewState, cursor: CursorMover, controls: { readonly choose: (date: CalendarDate) => void; readonly setState: (next: ViewState) => void }) {
  if (isComposingKey(event)) return;
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault();
    controls.choose(cursor.cursor);
    return;
  }
  const target = state.view === 'months' ? monthMoveTarget(event.key, cursor.cursor, state.day) : yearMoveTarget(event.key, cursor.cursor, state.day, state.pageStart);
  if (target === null) return;
  event.preventDefault();
  if (state.view === 'years') controls.setState({ ...state, pageStart: yearPageContaining(yearOf(target), state.pageStart) });
  cursor.moveTo(target, true);
}

/** 三層畫面的控制：往下一層、選了月份或年份、往上一層（Esc）、左右兩顆按鈕、月份與年份畫面的按鍵。 */
export function viewControls(state: ViewState, setState: (next: ViewState) => void, cursor: CursorMover, calendarRef: RefObject<HTMLElement | null>) {
  const goTo = (next: ViewState, date: CalendarDate) => {
    const shouldFocus = isFocusInCalendar(calendarRef);
    setState(next);
    cursor.moveTo(date, shouldFocus);
  };
  const choose = (date: CalendarDate) => {
    goTo({ ...state, view: state.view === 'years' ? 'months' : 'days' }, date);
  };
  return {
    ...state,
    reset: () => {
      setState(DAYS_VIEW);
    },
    showMonths: () => {
      goTo({ ...state, view: 'months', day: dayOfMonth(cursor.cursor) }, cursor.cursor);
    },
    showYears: () => {
      goTo({ ...state, view: 'years', pageStart: yearPageStartFor(yearOf(cursor.cursor)) }, cursor.cursor);
    },
    /** 選了月份（回到日期畫面）或年份（回到月份畫面）：焦點那一格是同一天（夾到月底）。不是選了日期。 */
    choose,
    /** 往上一層（Esc）：年份 → 月份 → 日期。已經在日期畫面時回傳 false（Esc 照舊關上日曆）。 */
    goUp: (): boolean => {
      if (state.view === 'days') return false;
      goTo({ ...state, view: state.view === 'years' ? 'months' : 'days' }, cursor.cursor);
      return true;
    },
    stepPage: (delta: number) => {
      stepPage(state, cursor, setState, delta, isFocusOnCell(calendarRef));
    },
    handlePeriodKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      handlePeriodKey(event, state, cursor, { choose, setState });
    },
    /** 月份畫面的一格選了之後的那一天：那個月的「進月份畫面時的那一天」（夾到月底）。 */
    dateOfMonth: (month: number) => shiftMonthsKeepingDay(cursor.cursor, month - monthOf(cursor.cursor), state.day),
    /** 年份畫面的一格選了之後的那一天：那一年的同一個月、同一天（夾到月底）。 */
    dateOfYear: (year: number) => shiftYearsKeepingDay(cursor.cursor, year - yearOf(cursor.cursor), state.day),
  };
}

export type ViewControls = ReturnType<typeof viewControls>;
