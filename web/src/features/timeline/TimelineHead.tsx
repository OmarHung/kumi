import type { CalendarDate } from '../../api/types';
import { cx } from '../../lib/cx';
import { At } from './At';
import { dayCells, monthCells, type CycleSegment, type MilestoneMark } from './timelineModel';
import { daysBetween, formatRangeIn, type TimelineWindow } from './timelineRange';
import styles from './Timeline.module.css';

interface TimelineHeadProps {
  readonly window: TimelineWindow;
  readonly todayDate: CalendarDate;
  /** 這段時間裡的週期與里程碑：沒有的那一排不畫（高度預算，看 Timeline.module.css 的 --head-rows）。 */
  readonly cycles: readonly CycleSegment[];
  readonly marks: readonly MilestoneMark[];
}

/**
 * 表頭：月份、日期兩排，加上這段時間裡有週期才有的週期一排、有里程碑才有的里程碑名稱一排。整個是畫給眼睛看的（aria-hidden）：
 * 位置對報讀軟體沒有意義，同樣的資訊由 TimelineView 的一段只給報讀軟體的文字說（日期寫在文字裡）。
 */
export function TimelineHead({ window, todayDate, cycles, marks }: TimelineHeadProps) {
  return (
    <div className={styles.head} aria-hidden="true">
      <div className={styles.headRow}>
        {monthCells(window).map((cell) => (
          <At key={cell.key} col={cell.col} span={cell.span} className={styles.cell}>
            {cell.label}
          </At>
        ))}
      </div>
      <div className={styles.headRow}>
        {dayCells(window, todayDate).map((cell) => (
          <At key={cell.key} col={cell.col} span={cell.span} className={cx(styles.cell, styles.dayCell)} attrs={{ 'data-today': cell.isToday }}>
            {cell.label}
          </At>
        ))}
      </div>
      {cycles.length > 0 && (
        <div className={styles.headRow}>
          {cycles.map(({ cycle, placement, range }) => (
            <At
              key={cycle.id}
              col={placement.col}
              span={placement.span}
              className={styles.cycle}
              title={`${cycle.name} ${range}`}
              attrs={{ 'data-clipped-start': placement.isClippedStart, 'data-clipped-end': placement.isClippedEnd }}
            >
              {`${cycle.name} ${range}`}
            </At>
          ))}
        </div>
      )}
      {marks.length > 0 && (
        <div className={styles.headRow}>
          {marks.map((mark) => (
            <At key={mark.milestone.id} col={mark.col} span={mark.labelSpan} className={styles.milestoneName} title={`${mark.milestone.name}，${formatRangeIn(window, mark.milestone.dueDate, mark.milestone.dueDate)}`}>
              {mark.milestone.name}
            </At>
          ))}
        </div>
      )}
    </div>
  );
}

/** 今天在畫面裡的第幾天；不在畫面裡是 null。 */
export function todayColumn(window: TimelineWindow, todayDate: CalendarDate): number | null {
  const col = daysBetween(window.from, todayDate);
  return col >= 0 && col < window.days ? col : null;
}
