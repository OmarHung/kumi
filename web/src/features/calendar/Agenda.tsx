import type { CalendarDate, Item } from '../../api/types';
import { cx } from '../../lib/cx';
import { markersOf, type DayMarkers } from './calendarData';
import { dayHeading, type MonthGrid, type MonthKey } from './calendarMonth';
import { rowsOfDay } from './dayRows';
import { DayRows } from './DayEntries';
import styles from './Calendar.module.css';

interface AgendaProps {
  readonly month: MonthKey;
  readonly grid: MonthGrid;
  readonly todayDate: CalendarDate;
  readonly days: ReadonlyMap<CalendarDate, readonly Item[]>;
  readonly markers: ReadonlyMap<CalendarDate, DayMarkers>;
}

/**
 * 窄螢幕（48rem 以下）的行事曆：月曆的格子太小，換成議程——這個月有東西的日子，一天一段（日期標題加那一天的全部項目）。
 * 沒有項目、里程碑與週期標示的日子不列；頭尾補的上下個月的日子不是這個月，也不列。
 */
export function Agenda({ month, grid, todayDate, days, markers }: AgendaProps) {
  const dates = grid.weeks
    .flat()
    .filter((date) => date.startsWith(month))
    .filter((date) => (days.get(date)?.length ?? 0) > 0 || hasMarkers(markers, date));
  if (dates.length === 0) {
    return <p className={styles.agendaEmpty} data-content-start>這個月沒有項目。</p>;
  }
  return (
    <div className={styles.agenda}>
      {dates.map((date, index) => (
        <section key={date} data-content-start={index === 0 ? true : undefined} className={cx(styles.agendaDay, date === todayDate && styles.agendaToday)} aria-current={date === todayDate ? 'date' : undefined}>
          <h3 className={styles.agendaHeading}>
            {dayHeading(date)}
            {date === todayDate && <span className="visually-hidden">（今天）</span>}
          </h3>
          <ul className={styles.entries}>
            <DayRows rows={rowsOfDay(markersOf(markers, date), days.get(date) ?? [])} />
          </ul>
        </section>
      ))}
    </div>
  );
}

function hasMarkers(markers: ReadonlyMap<CalendarDate, DayMarkers>, date: CalendarDate): boolean {
  const day = markersOf(markers, date);
  return day.milestones.length + day.cycleStarts.length + day.cycleEnds.length > 0;
}
