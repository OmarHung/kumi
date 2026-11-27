import { useEffect, useRef, type KeyboardEvent, type ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { useProjectDetails } from '../../api/queries';
import type { CalendarDate, Item } from '../../api/types';
import { List } from '../../components/List/List';
import { cx } from '../../lib/cx';
import { weekdayHeaders, dayHeading, type MonthGrid, type MonthKey } from './calendarMonth';
import { markersOf, type DayMarkers } from './calendarData';
import { useCalendarScope } from './calendarScope';
import { dayButtonName, dotsOfDay, moveSelectedDay, selectedDayOf } from './compactDay';
import { markerRowsOfDay } from './dayRows';
import { MilestoneMarker } from './DayEntries';
import { ItemListRow } from '../work-items/ItemListRow';
import type { WeekStart } from '../../lib/weekStart';
import styles from './CompactCalendar.module.css';

const NO_ITEMS: readonly Item[] = [];

interface CompactCalendarProps {
  readonly month: MonthKey;
  readonly title: string;
  readonly grid: MonthGrid;
  readonly weekStart: WeekStart;
  readonly todayDate: CalendarDate;
  readonly days: ReadonlyMap<CalendarDate, readonly Item[]>;
  readonly markers: ReadonlyMap<CalendarDate, DayMarkers>;
  /** 「N 個項目沒有到期日」：放在最下面。 */
  readonly noDue: ReactNode;
}

interface MonthProps extends Omit<CompactCalendarProps, 'noDue'> {
  readonly selected: CalendarDate;
  readonly onSelect: (date: CalendarDate, isFromKeyboard: boolean) => void;
}

/**
 * 小月曆：週幾的一列加整個月的格子。選的那一格 aria-pressed（它是「選一天」的按鈕，不是切換頁面，所以不用 aria-current）。
 * 只有選的那一格在 Tab 的順序裡，方向鍵在格子之間移動（左右一天、上下一週，Home、End 到月初月底），選與焦點一起走。
 */
function MiniMonth({ month, title, grid, weekStart, todayDate, days, markers, selected, onSelect }: MonthProps) {
  const rootRef = useRef<HTMLDivElement>(null);
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const next = moveSelectedDay(selected, event.key, month);
    if (next === null) return;
    event.preventDefault();
    if (next !== selected) onSelect(next, true);
  };
  return (
    <div className={styles.month} ref={rootRef} role="group" aria-label={title} onKeyDown={onKeyDown} data-content-start>
      <div className={styles.weekdays} aria-hidden="true">
        {weekdayHeaders(weekStart).map((weekday) => (
          <span key={weekday} className={styles.weekday}>
            {weekday}
          </span>
        ))}
      </div>
      <div className={styles.grid}>
        {grid.weeks.flat().map((date) => (
          <DayCell key={date} date={date} isInMonth={date.startsWith(month)} todayDate={todayDate} isSelected={date === selected} items={days.get(date) ?? NO_ITEMS} markers={markersOf(markers, date)} onSelect={onSelect} />
        ))}
      </div>
    </div>
  );
}

interface DayCellProps {
  readonly date: CalendarDate;
  readonly isInMonth: boolean;
  readonly todayDate: CalendarDate;
  readonly isSelected: boolean;
  readonly items: readonly Item[];
  readonly markers: DayMarkers;
  readonly onSelect: (date: CalendarDate, isFromKeyboard: boolean) => void;
}

function DayCell({ date, isInMonth, todayDate, isSelected, items, markers, onSelect }: DayCellProps) {
  const dayOfMonth = Number(date.slice(8));
  if (!isInMonth) {
    return (
      <span className={cx(styles.cell, styles.outside)} aria-hidden="true">
        <span className={styles.number}>{dayOfMonth}</span>
        <span className={styles.dots} />
      </span>
    );
  }
  const markerCount = markerRowsOfDay(markers).length;
  const dots = dotsOfDay(items, markerCount, todayDate);
  return (
    <button
      type="button"
      data-date={date}
      className={cx(styles.cell, date === todayDate && styles.today, isSelected && styles.selected)}
      aria-pressed={isSelected}
      aria-label={dayButtonName(date, todayDate, items.length, markerCount)}
      tabIndex={isSelected ? 0 : -1}
      onClick={() => {
        onSelect(date, false);
      }}
    >
      <span className={styles.number} aria-hidden="true">
        {dayOfMonth}
      </span>
      <span className={styles.dots} aria-hidden="true">
        {dots.map((dot, index) => (
          <span key={`${dot}-${String(index)}`} className={cx(styles.dot, styles[dot])} />
        ))}
      </span>
    </button>
  );
}

interface SelectedDayProps {
  readonly date: CalendarDate;
  readonly todayDate: CalendarDate;
  readonly items: readonly Item[];
  readonly markers: DayMarkers;
}

/** 選的那一天：標題、週期與里程碑的標示（前面空心點）、項目清單（和專案清單同一個長相）。 */
function SelectedDay({ date, todayDate, items, markers }: SelectedDayProps) {
  const { projectKey, isAssigneeShown } = useCalendarScope();
  const projectKeys = [...new Set(items.map((item) => item.projectKey))];
  const projects = useProjectDetails(projectKeys);
  const markerRows = markerRowsOfDay(markers);
  return (
    <section className={styles.day} aria-label="選的那一天">
      <h3 className={styles.dayHeading}>
        {dayHeading(date)}
        {date === todayDate && ' · 今天'}
      </h3>
      {markerRows.length > 0 && (
        <ul className={styles.markers}>
          {markerRows.map((row) => (
            <li key={row.key} className={styles.markerLine}>
              <span className={cx(styles.dot, styles.marker)} aria-hidden="true" />
              {row.kind === 'milestone' && <MilestoneMarker projectKey={projectKey} id={row.milestone.id} name={row.milestone.name} hasSymbol={false} />}
              {row.kind === 'cycleStart' && <span>{row.cycle.name} 開始</span>}
              {row.kind === 'cycleEnd' && <span>{row.cycle.name} 結束</span>}
            </li>
          ))}
        </ul>
      )}
      {items.length === 0 && markerRows.length === 0 && <p className={styles.empty}>這一天沒有項目。</p>}
      {items.length > 0 && (
        <List label={`${dayHeading(date)}的項目`} isFlush>
          {items.map((item) => (
            <ItemListRow
              key={item.key}
              item={item}
              blueprint={projects.get(item.projectKey)?.blueprint}
              todayDate={todayDate}
              projectName={projectKey === null ? (projects.get(item.projectKey)?.project.name ?? item.projectKey) : undefined}
              showAssignee={projectKey !== null || isAssigneeShown}
            />
          ))}
        </List>
      )}
    </section>
  );
}

/**
 * 窄螢幕（48rem 以下）的行事曆：上面一個整月的小月曆，下面是選的那一天。選的那一天記在網址的 day（YYYY-MM-DD）：
 * 和 month 一樣是「網址就是狀態」，重新整理與分享都留著；它是預設（本月的今天、別月的 1 號）時不寫，用 replace、不多一筆瀏覽紀錄。
 */
export function CompactCalendar({ noDue, ...content }: CompactCalendarProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const { month, todayDate, days, markers } = content;
  const selected = selectedDayOf(searchParams.get('day'), month, todayDate);
  const isKeyboardMove = useRef(false);
  useEffect(() => {
    if (!isKeyboardMove.current) return;
    isKeyboardMove.current = false;
    document.querySelector<HTMLElement>(`button[data-date="${selected}"]`)?.focus();
  }, [selected]);
  const onSelect = (date: CalendarDate, isFromKeyboard: boolean) => {
    isKeyboardMove.current = isFromKeyboard;
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        if (date === selectedDayOf(null, month, todayDate)) next.delete('day');
        else next.set('day', date);
        return next;
      },
      { replace: true },
    );
  };
  return (
    <>
      <MiniMonth {...content} selected={selected} onSelect={onSelect} />
      <SelectedDay date={selected} todayDate={todayDate} items={days.get(selected) ?? NO_ITEMS} markers={markersOf(markers, selected)} />
      {noDue}
    </>
  );
}
