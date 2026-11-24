import type { KeyboardEvent } from 'react';
import type { CalendarDate } from '../../api/types';
import { cx } from '../../lib/cx';
import { useWeekStart } from '../../lib/weekStart';
import { dayLabel, dayOfMonth, isCalendarDate, isSameMonth, monthGrid, PERIOD_COLUMNS, type PeriodCell, weekdaysFrom, weeksOf } from './calendarDates';
import styles from './DatePicker.module.css';

/*
 * 日曆的格子（docs/DESIGN.md 7.3「日期」）：日期、月份、年份三種，都是 div 加角色（grid、row、gridcell），排版用 CSS grid。
 * 不用 <table>：浮層不是 portal，日曆留在打開它的元素旁邊的 DOM 裡；放在表格的格子裡時，外層表格的 `.table td` 這類樣式
 * 會套到日曆的格子上，外層用 querySelectorAll('tr') 數列的程式也會數到日曆的列。
 * 每一格本身停得到（roving tabindex，只有焦點那一格是 0），data-focus-key 給換畫面、移動之後放焦點用。
 */

interface DayCellProps {
  readonly date: CalendarDate;
  readonly cursor: CalendarDate;
  readonly value: string;
  readonly todayDate: CalendarDate;
  readonly onPick: (value: string) => void;
}

/** 一天：名稱是完整的日期加星期；選的那一天 aria-selected，今天 aria-current="date"，別的月份 data-outside（灰字、照樣可以選）。 */
function DayCell({ date, cursor, value, todayDate, onPick }: DayCellProps) {
  return (
    <div
      role="gridcell"
      className={cx(styles.cell, styles.day)}
      data-focus-key={date}
      data-outside={!isSameMonth(date, cursor) || undefined}
      tabIndex={date === cursor ? 0 : -1}
      aria-label={dayLabel(date)}
      aria-selected={date === value || undefined}
      aria-current={date === todayDate ? 'date' : undefined}
      onClick={() => {
        onPick(date);
      }}
    >
      {dayOfMonth(date)}
    </div>
  );
}

export interface DayGridProps {
  readonly label: string;
  readonly cursor: CalendarDate;
  readonly value: string;
  readonly todayDate: CalendarDate;
  readonly onPick: (value: string) => void;
  readonly onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
}

/** 日期畫面：名稱是月份（「2026 年 11 月」），表頭從一週的第一天開始（照偏好，名稱是「星期一」…），六列 42 天。 */
export function DayGrid({ label, cursor, value, todayDate, onPick, onKeyDown }: DayGridProps) {
  const weekStart = useWeekStart();
  return (
    <div role="grid" aria-label={label} className={styles.grid} onKeyDown={onKeyDown}>
      <div role="row" className={styles.week}>
        {weekdaysFrom(weekStart).map((weekday) => (
          <span key={weekday.long} role="columnheader" aria-label={weekday.long} className={styles.weekday}>
            {weekday.short}
          </span>
        ))}
      </div>
      {weeksOf(monthGrid(cursor, weekStart)).map((week) => (
        <div key={week[0]} role="row" className={styles.week}>
          {week.map((date) => (
            <DayCell key={date} {...{ date, cursor, value, todayDate, onPick }} />
          ))}
        </div>
      ))}
    </div>
  );
}

/** 月份、年份畫面的一格：選了之後的那一天（同一天，夾到月底）。 */
export interface PeriodChoice extends PeriodCell {
  readonly date: CalendarDate;
}

export interface PeriodGridProps {
  /** 格子的名稱：「2026 年」（月份畫面）、「2024～2035 年」（年份畫面）。 */
  readonly label: string;
  readonly cells: readonly PeriodChoice[];
  /** 焦點那一格的 key。 */
  readonly focusKey: string;
  /** 選的日期所在的那一格、今天所在的那一格的 key；值不是日期時沒有選的那一格。 */
  readonly selectedKey: string | null;
  readonly currentKey: string;
  readonly onChoose: (date: CalendarDate) => void;
  readonly onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
}

/**
 * 月份、年份畫面：3×4 的格子，撐滿日期畫面六列的高度（換畫面時日曆不跳）。選的那一格 aria-selected（實心），
 * 今天所在的那一格 aria-current="date"（環線）：「date」是「一組日期裡目前的那一個」，月、年也是日期的單位，和日期格同一套。
 */
export function PeriodGrid({ label, cells, focusKey, selectedKey, currentKey, onChoose, onKeyDown }: PeriodGridProps) {
  return (
    <div role="grid" aria-label={label} className={styles.period} onKeyDown={onKeyDown}>
      {weeksOfPeriod(cells).map((row) => (
        <div key={row[0]?.key} role="row" className={styles.periodRow}>
          {row.map((cell) => (
            <div
              key={cell.key}
              role="gridcell"
              className={styles.cell}
              data-focus-key={cell.key}
              tabIndex={cell.key === focusKey ? 0 : -1}
              aria-label={cell.label}
              aria-selected={cell.key === selectedKey || undefined}
              aria-current={cell.key === currentKey ? 'date' : undefined}
              onClick={() => {
                onChoose(cell.date);
              }}
            >
              {cell.short}
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

/** 3×4：一列三格。 */
function weeksOfPeriod(cells: readonly PeriodChoice[]): readonly (readonly PeriodChoice[])[] {
  return Array.from({ length: Math.ceil(cells.length / PERIOD_COLUMNS) }, (_, row) => cells.slice(row * PERIOD_COLUMNS, (row + 1) * PERIOD_COLUMNS));
}

/** 值所在的那一格的 key（月份畫面 YYYY-MM、年份畫面 YYYY）；值不是日期時沒有。 */
export function selectedPeriodKey(value: string, length: number): string | null {
  return isCalendarDate(value) ? value.slice(0, length) : null;
}
