import { useCallback, useState } from 'react';
import type { Cycle, Milestone } from '../../api/planning';
import type { CalendarDate } from '../../api/types';
import { useWeekStart } from '../../lib/weekStart';
import { At } from './At';
import { cycleSegments, milestoneMarks, type TimelineRow } from './timelineModel';
import { stepDaysFor, type TimelineWindow } from './timelineRange';
import { TimelineControls } from './TimelineControls';
import { TimelineHead, todayColumn } from './TimelineHead';
import { RowLabels, RowMarks } from './TimelineRows';
import { useVisibleDays } from './useVisibleDays';
import styles from './Timeline.module.css';

interface TimelineGridProps {
  readonly window: TimelineWindow;
  readonly todayDate: CalendarDate;
  readonly rows: readonly TimelineRow[];
  readonly cycles: readonly Cycle[];
  readonly milestones: readonly Milestone[];
  /** 載入中、失敗、沒有項目的說明：表頭與控制項仍在，說明接在下面。 */
  readonly hasMessage: boolean;
}

/**
 * 寬螢幕的時間軸：左邊一欄黏在左邊（左上角是控制項），右邊是表頭加每個項目一列，自己橫向與直向捲。
 * 里程碑的線與今天的線畫在列的上面、條的下面，貫穿所有列。
 * 表頭只畫這段時間裡有的週期與里程碑那幾排（data-has-cycles、data-has-milestones 讓 CSS 算高度）。
 */
export function TimelineGrid({ window, todayDate, rows, cycles, milestones, hasMessage }: TimelineGridProps) {
  const { scroller, labels, track, visibleDays } = useVisibleDays(window.days);
  const weekStart = useWeekStart();
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const onActive = useCallback((itemKey: string | null) => {
    setActiveKey(itemKey);
  }, []);
  const today = todayColumn(window, todayDate);
  const segments = cycleSegments(cycles, window);
  const marks = milestoneMarks(milestones, window);
  return (
    <div
      ref={scroller}
      className={styles.scroller}
      data-testid="timeline-scroller"
      data-scale={window.scale}
      data-has-message={hasMessage}
      data-has-cycles={segments.length > 0}
      data-has-milestones={marks.length > 0}
    >
      <At days={window.days} className={styles.frame}>
        <div ref={labels} className={styles.labels} data-testid="timeline-labels">
          <div className={styles.corner}>
            <TimelineControls window={window} todayDate={todayDate} stepDays={stepDaysFor(window.scale, visibleDays)} layout="corner" />
          </div>
          <RowLabels rows={rows} activeKey={activeKey} onActive={onActive} />
        </div>
        <div ref={track} className={styles.track} data-testid="timeline-track">
          <TimelineHead window={window} todayDate={todayDate} cycles={segments} marks={marks} />
          <div className={styles.body} data-week-start={weekStart}>
            {marks.map((mark) => (
              <At key={mark.milestone.id} col={mark.col} span={1} className={`${styles.line} ${styles.milestoneLine}`} />
            ))}
            {today !== null && <TodayLine col={today} />}
            <RowMarks rows={rows} activeKey={activeKey} onActive={onActive} />
          </div>
        </div>
      </At>
    </div>
  );
}

function TodayLine({ col }: { readonly col: number }) {
  return <At col={col} span={1} className={`${styles.line} ${styles.todayLine}`} attrs={{ 'data-testid': 'today-line' }} />;
}
