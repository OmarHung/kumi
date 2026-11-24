import type { ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import type { Cycle, Milestone } from '../../api/planning';
import type { CalendarDate } from '../../api/types';
import { EmptyState, ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { formatDate } from '../../domain/dates';
import { useIsCompact } from '../../lib/useMediaQuery';
import { useWeekStart, type WeekStart } from '../../lib/weekStart';
import type { DateViewSource } from '../calendar/dateViewSource';
import { cycleSegments, milestoneMarks, timelineRows, type CycleSegment, type MilestoneMark, type TimelineRow } from './timelineModel';
import { formatRangeIn, fromFromSearchParams, scaleFromSearchParams, stepDaysFor, windowOf, type TimelineWindow } from './timelineRange';
import { TimelineControls } from './TimelineControls';
import { TimelineGrid } from './TimelineGrid';
import { TimelineNotes } from './TimelineNotes';
import { useTimelineData, type TimelineData, type TimelineQueries } from './useTimelineData';
import styles from './Timeline.module.css';

const NO_CYCLES: readonly Cycle[] = [];
const NO_MILESTONES: readonly Milestone[] = [];

/** 給報讀軟體的文字：畫面的範圍、數量，以及這段時間裡的週期與里程碑（位置沒有意義，日期寫在文字裡）。 */
function ScreenReaderSummary(props: { readonly text: string; readonly planning: readonly string[] }) {
  return (
    <>
      <p role="status" className="visually-hidden">
        {props.text}
      </p>
      {props.planning.length > 0 && (
        <ul aria-label="週期與里程碑" className="visually-hidden">
          {props.planning.map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      )}
    </>
  );
}

/** 週期與里程碑各一句（給報讀軟體）：名稱加日期。 */
function planningLines(window: TimelineWindow, cycles: readonly CycleSegment[], milestones: readonly MilestoneMark[]): readonly string[] {
  return [
    ...cycles.map(({ cycle, range }) => `週期：${cycle.name}，${range}`),
    ...milestones.map(({ milestone }) => `里程碑：${milestone.name}，${formatRangeIn(window, milestone.dueDate, milestone.dueDate)}`),
  ];
}

export interface TimelineFrameProps {
  readonly source: DateViewSource;
  readonly todayDate: CalendarDate;
  /** 這段時間要讀的兩份清單（條件由上層換成 API 的參數）。 */
  readonly queriesFor: (window: TimelineWindow, weekStart: WeekStart) => TimelineQueries;
  /** 週期與里程碑的標示；沒有時不給。 */
  readonly cycles?: readonly Cycle[];
  readonly milestones?: readonly Milestone[];
  /** 還在等週期與里程碑（篩選要用它們換成條件）：先不送請求、說載入中。 */
  readonly isWaiting?: boolean;
  /** 讀不到週期、里程碑，或篩選的結果一定是空的：取代時間軸說明原因。 */
  readonly blocker?: ReactNode;
  /** 連到表格、只看沒有日期的那個網址。 */
  readonly undatedHref: string;
  /** 窄螢幕取代時間軸的清單。 */
  readonly renderList: (rows: readonly TimelineRow[]) => ReactNode;
}

interface MessageInput {
  readonly blocker: ReactNode;
  readonly isWaiting: boolean;
  readonly data: TimelineData;
  readonly hasRows: boolean;
}

/** 表頭之外要說的事：載入中、失敗、沒有項目；都沒有時是 null，直接畫項目。 */
function statusMessage({ blocker, isWaiting, data, hasRows }: MessageInput) {
  if (blocker !== null) return blocker;
  if (isWaiting || data.dated.isPending) return <LoadingState label="正在載入時間軸…" />;
  if (data.dated.isError) return <ErrorState error={data.dated.error} title="時間軸載入失敗" onRetry={data.dated.retry} />;
  if (hasRows) return null;
  return (
    <EmptyState title="這段時間沒有項目">用「往前」「往後」換一段時間，或調整上方的篩選條件。</EmptyState>
  );
}

/**
 * 時間軸的畫面（docs/design/08-interaction.md「時間軸」）：每個項目一列，從開始日畫到到期日；
 * 唯讀，不能拖曳改日期。畫面的刻度（scale）與第一天（from）記在網址上。專案頁（TimelineView）與「我的工作」
 * （MyWorkTimeline）共用，差在去哪裡讀項目、有沒有週期與里程碑的標示、窄螢幕的清單，都由上層傳進來。
 * 窄螢幕不畫時間軸，換成依開始日排的清單。
 */
export function TimelineFrame({
  source,
  todayDate,
  queriesFor,
  cycles = NO_CYCLES,
  milestones = NO_MILESTONES,
  isWaiting = false,
  blocker = null,
  undatedHref,
  renderList,
}: TimelineFrameProps) {
  const [searchParams] = useSearchParams();
  const weekStart = useWeekStart();
  const window = windowOf(scaleFromSearchParams(searchParams), fromFromSearchParams(searchParams, todayDate, weekStart));
  const isCompact = useIsCompact();
  const data = useTimelineData(source, queriesFor(window, weekStart), !isWaiting && blocker === null);
  const rows = timelineRows(data.dated.visible, window, todayDate);
  const segments = cycleSegments(cycles, window);
  const marks = milestoneMarks(milestones, window);
  const message = statusMessage({ blocker, isWaiting, data, hasRows: rows.length > 0 });
  const summary = data.dated.isPending || isWaiting ? '時間軸載入中。' : `顯示 ${formatDate(window.from)} 到 ${formatDate(window.to)}，共 ${String(rows.length)} 個項目。`;
  return (
    <div role="region" aria-label="時間軸" className={styles.view}>
      <ScreenReaderSummary text={summary} planning={planningLines(window, segments, marks)} />
      {isCompact ? (
        <>
          <TimelineControls window={window} todayDate={todayDate} stepDays={stepDaysFor(window.scale, null)} layout="bar" />
          {message ?? renderList(rows)}
          <p className={styles.compactNote}>時間軸在寬螢幕上看。</p>
        </>
      ) : (
        <>
          <TimelineGrid window={window} todayDate={todayDate} rows={rows} cycles={cycles} milestones={milestones} hasMessage={message !== null} />
          {message !== null && <div className={styles.message}>{message}</div>}
        </>
      )}
      <TimelineNotes
        undatedHref={undatedHref}
        undatedCount={data.undatedCount}
        isUndatedTruncated={data.isUndatedTruncated}
        isTruncated={data.dated.isTruncated}
        isMoreFailed={data.dated.isMoreFailed}
      />
    </div>
  );
}
