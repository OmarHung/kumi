import { Popover } from '../../components/Popover/Popover';
import type { CalendarDate, Item } from '../../api/types';
import { cx } from '../../lib/cx';
import { markersOf, type DayMarkers } from './calendarData';
import { dayHeading, type MonthGrid as Grid, type MonthKey, weekdayHeaders } from './calendarMonth';
import type { WeekStart } from '../../lib/weekStart';
import type { DayCover, WeekLayout, WeekSegment } from './calendarSpans';
import { markerRowsOfDay, rowsOfDay, rowsOfSpanDay, splitRows, splitSpanRows, type DayRow, type DayRowSplit } from './dayRows';
import { DayRows } from './DayEntries';
import { SpanLanes } from './SpanLanes';
import styles from './Calendar.module.css';

const NO_ITEMS: readonly Item[] = [];

/** 期間排法裡一格要知道的：這一天碰到的項目（收起來幾個），以及這一週的條（只有第一天的格子帶，條鋪滿整週）。 */
interface SpanDay {
  readonly cover: DayCover;
  readonly segments: readonly WeekSegment[] | null;
}

interface DayCellProps {
  readonly date: CalendarDate;
  readonly isOutside: boolean;
  readonly isToday: boolean;
  readonly items: readonly Item[];
  readonly markers: DayMarkers;
  /** 期間排法；null 是依到期日。 */
  readonly span: SpanDay | null;
}

interface MoreRowsProps {
  readonly date: CalendarDate;
  readonly rows: readonly DayRow[];
  readonly hiddenCount: number;
}

/** 放不下的那幾行：按鈕打開 Popover，列出這一天的全部（里程碑、週期標示與項目）。 */
function MoreRows({ date, rows, hiddenCount }: MoreRowsProps) {
  return (
    <Popover
      label={`${dayHeading(date)}的項目`}
      renderTrigger={(props) => (
        // 看到的字不變；名稱帶日期，一個月最多三十幾個同名的按鈕才分得出是哪一天。
        <button type="button" className={styles.more} aria-label={`${shortDate(date)}還有 ${String(hiddenCount)} 個項目`} {...props}>
          還有 {hiddenCount} 個
        </button>
      )}
    >
      <h4 className={styles.popoverTitle}>{dayHeading(date)}</h4>
      <ul className={styles.popoverList}>
        <DayRows rows={rows} />
      </ul>
    </Popover>
  );
}

function shortDate(date: CalendarDate): string {
  return `${String(Number(date.slice(5, 7)))} 月 ${String(Number(date.slice(8)))} 日`;
}

/** 這一格底下的行：依到期日排時是項目與標示（最多三行）；期間排法時項目在上面的車道，這裡只有標示與「還有 N 個」。 */
function entriesOf(markers: DayMarkers, items: readonly Item[], span: SpanDay | null): { readonly rows: readonly DayRow[]; readonly split: DayRowSplit } {
  if (span === null) {
    const rows = rowsOfDay(markers, items);
    return { rows, split: splitRows(rows) };
  }
  return { rows: rowsOfSpanDay(markers, span.cover.entries), split: splitSpanRows(markerRowsOfDay(markers), span.cover.hiddenCount) };
}

function DayCell({ date, isOutside, isToday, items, markers, span }: DayCellProps) {
  const dayOfMonth = Number(date.slice(8));
  const { rows, split } = entriesOf(markers, items, span);
  const { shown, hiddenCount } = split;
  return (
    <td className={cx(styles.day, span !== null && styles.daySpan, isOutside && styles.outside, isToday && styles.today)} aria-current={isToday ? 'date' : undefined}>
      {/* 格子上只看得到日期的數字（今天多一圈外框，形狀不靠顏色）；標題是完整的「11 月 18 日（週三）」，報讀軟體靠它知道在哪一天。 */}
      <h3 className={styles.dayNumber}>
        <span className={styles.dayNumberText} aria-hidden="true">
          {dayOfMonth}
        </span>
        <span className="visually-hidden">{isToday ? `${dayHeading(date)}（今天）` : dayHeading(date)}</span>
      </h3>
      {span !== null && span.segments !== null && span.segments.length > 0 && <SpanLanes segments={span.segments} />}
      {/* 車道的位置先留著（條是疊在格子上面的），底下的標示與「還有 N 個」才不會被蓋住。 */}
      {span !== null && <div className={styles.laneSpace} aria-hidden="true" />}
      <ul className={styles.entries}>
        <DayRows rows={shown} isTitleOnly />
        {hiddenCount > 0 && (
          <li>
            <MoreRows date={date} rows={rows} hiddenCount={hiddenCount} />
          </li>
        )}
      </ul>
    </td>
  );
}

export interface MonthTableProps {
  readonly month: MonthKey;
  readonly title: string;
  readonly grid: Grid;
  readonly weekStart: WeekStart;
  readonly todayDate: CalendarDate;
  readonly days: ReadonlyMap<CalendarDate, readonly Item[]>;
  readonly markers: ReadonlyMap<CalendarDate, DayMarkers>;
  /** 期間排法每一週的條；null 是依到期日排（days 才有用）。 */
  readonly spanLayout: readonly WeekLayout[] | null;
}

function spanDayOf(week: WeekLayout | undefined, dayIndex: number): SpanDay | null {
  const cover = week?.days[dayIndex];
  return week === undefined || cover === undefined ? null : { cover, segments: dayIndex === 0 ? week.segments : null };
}

/** 月曆：<table>，表頭照一週從哪天開始排；頭尾補的上下個月的日子字淡一點。 */
export function MonthTable({ month, title, grid, weekStart, todayDate, days, markers, spanLayout }: MonthTableProps) {
  return (
    <table className={styles.month} aria-label={title} data-content-start>
      <thead>
        <tr>
          {weekdayHeaders(weekStart).map((weekday) => (
            <th key={weekday} scope="col" className={styles.weekday}>
              {weekday}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {grid.weeks.map((week, weekIndex) => (
          <tr key={week[0]}>
            {week.map((date, dayIndex) => (
              <DayCell
                key={date}
                date={date}
                isOutside={!date.startsWith(month)}
                isToday={date === todayDate}
                items={days.get(date) ?? NO_ITEMS}
                markers={markersOf(markers, date)}
                span={spanDayOf(spanLayout?.[weekIndex], dayIndex)}
              />
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
