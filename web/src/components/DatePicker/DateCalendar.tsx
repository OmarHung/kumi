import type { Ref, RefObject } from 'react';
import type { CalendarDate } from '../../api/types';
import { today } from '../../domain/dates';
import { TRANSIENT_FOCUS_ATTRIBUTE } from '../../lib/focusable';
import { Button } from '../Button/Button';
import { Floating } from '../Floating/Floating';
import { Icon } from '../Icon/Icon';
import { IconButton } from '../IconButton/IconButton';
import { monthLabel, monthsOfYear, yearLabel, yearRangeLabel, yearsFrom } from './calendarDates';
import type { CalendarView } from './calendarView';
import { DayGrid, PeriodGrid, selectedPeriodKey } from './CalendarGrids';
import type { DatePickerState } from './useDatePicker';
import styles from './DatePicker.module.css';

export interface DateCalendarProps {
  /** 對話框的 id：輸入框的 aria-controls 指到它。 */
  readonly id: string;
  readonly calendarRef: Ref<HTMLDivElement>;
  readonly anchorRef: RefObject<HTMLInputElement | null>;
  /** 目前的值（不是日期時沒有選的那一天）。 */
  readonly value: string;
  readonly picker: DatePickerState;
}

/** 每一種畫面的左右兩顆按鈕的名稱。 */
const STEP_LABELS: Readonly<Record<CalendarView, readonly [string, string]>> = {
  days: ['上一個月', '下一個月'],
  months: ['上一年', '下一年'],
  years: ['前 12 年', '後 12 年'],
};

/** 這一頁的標題：日期畫面「2026 年 12 月」、月份畫面「2026 年」、年份畫面「2024～2035」。 */
function pageTitle(view: CalendarView, cursor: CalendarDate, pageStart: number): string {
  if (view === 'months') return yearLabel(cursor);
  if (view === 'years') return yearRangeLabel(pageStart);
  return monthLabel(cursor);
}

/**
 * 標題：日期與月份畫面是往下一層的按鈕（「2026 年 12 月」→ 月份、「2026 年」→ 年份），名稱以看得到的字開頭（WCAG 2.5.3）；
 * 年份畫面是範圍，不是按鈕（沒有再下一層）。
 */
function PageTitle({ picker }: { readonly picker: DatePickerState }) {
  const { view, cursor } = picker;
  const title = pageTitle(view.view, cursor, view.pageStart);
  if (view.view === 'years') return <p className={styles.title}>{title}</p>;
  const isDays = view.view === 'days';
  return (
    <Button variant="ghost" size="sm" className={styles.titleButton} aria-label={`${title}，${isDays ? '選擇年份與月份' : '選擇年份'}`} onClick={isDays ? view.showMonths : view.showYears}>
      {title}
      <Icon name="chevronDown" className={styles.titleChevron} />
    </Button>
  );
}

/**
 * 上一頁｜標題｜下一頁。換頁、換畫面時用只給報讀軟體的區域唸出新的標題（按鈕本身的名稱不會自己唸）。
 * 打字讓日曆跟著換月時不唸：和打字的回音搶著唸（區域清空，報讀軟體不唸空的內容）。
 */
function CalendarHeader({ picker }: { readonly picker: DatePickerState }) {
  const { view, cursor } = picker;
  const [previous, next] = STEP_LABELS[view.view];
  const title = view.view === 'years' ? `${yearRangeLabel(view.pageStart)} 年` : pageTitle(view.view, cursor, view.pageStart);
  const spoken = picker.isTypingDriven ? '' : title;
  return (
    <div className={styles.header}>
      <IconButton
        icon="chevronLeft"
        label={previous}
        size="sm"
        onClick={() => {
          view.stepPage(-1);
        }}
      />
      <PageTitle picker={picker} />
      <IconButton
        icon="chevronRight"
        label={next}
        size="sm"
        onClick={() => {
          view.stepPage(1);
        }}
      />
      <p className="visually-hidden" aria-live="polite">
        {spoken}
      </p>
    </div>
  );
}

/** 這一頁的格子：日期、月份（這一年的 12 個月）或年份（這一頁的 12 年）。 */
function CalendarBody({ picker, value }: { readonly picker: DatePickerState; readonly value: string }) {
  const { view, cursor } = picker;
  const todayDate = today();
  if (view.view === 'days') {
    return <DayGrid label={monthLabel(cursor)} {...{ cursor, value, todayDate }} onPick={picker.pick} onKeyDown={picker.handleGridKeyDown} />;
  }
  const isMonths = view.view === 'months';
  const length = isMonths ? 7 : 4;
  const cells = isMonths
    ? monthsOfYear(cursor).map((cell, index) => ({ ...cell, date: view.dateOfMonth(index + 1) }))
    : yearsFrom(view.pageStart).map((cell) => ({ ...cell, date: view.dateOfYear(Number(cell.key)) }));
  return (
    <PeriodGrid
      label={isMonths ? yearLabel(cursor) : `${yearRangeLabel(view.pageStart)} 年`}
      cells={cells}
      focusKey={cursor.slice(0, length)}
      selectedKey={selectedPeriodKey(value, length)}
      currentKey={todayDate.slice(0, length)}
      onChoose={view.choose}
      onKeyDown={view.handlePeriodKeyDown}
    />
  );
}

/** 「今天」「清除」：在哪一種畫面都是明確的選擇（onPick），和選日期一樣。 */
function CalendarFooter({ onPick }: { readonly onPick: (value: string) => void }) {
  return (
    <div className={styles.footer}>
      <Button
        variant="ghost"
        size="sm"
        onClick={() => {
          onPick(today());
        }}
      >
        今天
      </Button>
      <Button
        variant="ghost"
        size="sm"
        onClick={() => {
          onPick('');
        }}
      >
        清除
      </Button>
    </div>
  );
}

/**
 * 日期欄位的日曆（docs/DESIGN.md 7.3「日期」）：「選擇日期」對話框，三層畫面（日期 → 月份 → 年份，calendarView.ts），
 * 下面一列「今天」「清除」。浮在 top layer（Floating）；在上面按下滑鼠不帶走焦點，用滑鼠換月份、換畫面時焦點留在輸入框、
 * 照樣可以打字。焦點只是暫時在這裡（TRANSIENT_FOCUS_ATTRIBUTE）：對話框的焦點陷阱算頭尾時不算它。
 */
export function DateCalendar({ id, calendarRef, anchorRef, value, picker }: DateCalendarProps) {
  return (
    <Floating anchorRef={anchorRef} align="start" keepsAnchorFocus onAnchorHidden={picker.closeWhenAnchorHidden} className={styles.calendar}>
      <div ref={calendarRef} id={id} role="dialog" aria-label="選擇日期" className={styles.dialog} onKeyDown={picker.handleCalendarKeyDown} {...{ [TRANSIENT_FOCUS_ATTRIBUTE]: '' }}>
        <CalendarHeader picker={picker} />
        <CalendarBody picker={picker} value={value} />
        <CalendarFooter onPick={picker.pick} />
      </div>
    </Floating>
  );
}
