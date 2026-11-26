import { useMemo, useRef, type ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import type { Cycle, Milestone } from '../../api/planning';
import type { CalendarDate, Item, ItemQuery } from '../../api/types';
import { Notice } from '../../components/Notice/Notice';
import { ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { useCalendarDateMode, type CalendarDateMode } from '../../lib/calendarDateMode';
import { useIsCompact } from '../../lib/useMediaQuery';
import { useWeekStart, type WeekStart } from '../../lib/weekStart';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { Agenda } from './Agenda';
import { CalendarBar } from './CalendarBar';
import { CALENDAR_ITEM_LIMIT, itemsByDay, markersByDay } from './calendarData';
import { monthGrid, monthOf, monthTitle, type MonthGrid } from './calendarMonth';
import { CalendarScopeContext, type CalendarScope } from './calendarScope';
import { itemSequence, layoutWeeks } from './calendarSpans';
import type { DateViewSource } from './dateViewSource';
import { MonthTable, type MonthTableProps } from './MonthGrid';
import { useAllItems } from './useAllItems';
import styles from './Calendar.module.css';

const TRUNCATED_MESSAGE = `這段時間的項目超過 ${CALENDAR_ITEM_LIMIT.toLocaleString('en-US')} 個，只顯示前 ${CALENDAR_ITEM_LIMIT.toLocaleString('en-US')} 個；用篩選縮小範圍。`;

function TruncatedNotice() {
  return (
    <div className={styles.notices}>
      <Notice tone="warning">{TRUNCATED_MESSAGE}</Notice>
    </div>
  );
}

const NO_CYCLES: readonly Cycle[] = [];
const NO_MILESTONES: readonly Milestone[] = [];
const NO_ITEMS: readonly Item[] = [];
const EMPTY_DAYS: ReadonlyMap<CalendarDate, readonly Item[]> = new Map();

export interface CalendarFrameProps {
  readonly source: DateViewSource;
  readonly todayDate: CalendarDate;
  /** 里程碑連去的專案；null 是沒有單一專案（我的工作）。 */
  readonly projectKey: string | null;
  /** 月曆上標示的週期與里程碑；沒有時不給。 */
  readonly cycles?: readonly Cycle[];
  readonly milestones?: readonly Milestone[];
  /** 還在等週期與里程碑（篩選要用它們換成條件）：先不送請求、說載入中。 */
  readonly isWaiting?: boolean;
  /** 讀不到週期、里程碑，或篩選的結果一定是空的：取代月曆說明原因。 */
  readonly problem?: ReactNode;
  /** 畫面上看得到的那幾週要讀的條件（依到期日，或期間重疊）；null 是一定沒有項目、不必送請求。 */
  readonly visibleQuery: (grid: MonthGrid, weekStart: WeekStart, mode: CalendarDateMode) => ItemQuery | null;
  /** 工具列的到期條件是「沒有到期日」：期間排法後端不能和日期範圍一起用，讀回來由這裡把有到期日的拿掉。 */
  readonly isDueNoneFilter?: boolean;
  /** 項目的名稱與提示多一句負責人（「我的工作」看「我建立的」時）。 */
  readonly isAssigneeShown?: boolean;
  /** 沒有日期的提示（依到期日：「N 個項目沒有到期日」；期間：「…沒有開始日也沒有到期日」）；窄螢幕放在議程最下面。0 個時它自己回 null。 */
  readonly renderNoDue: (placement: 'top' | 'bottom', mode: CalendarDateMode) => ReactNode;
}

interface CalendarBodyProps {
  readonly problem: ReactNode;
  readonly all: ReturnType<typeof useAllItems>;
  readonly isFirstLoad: boolean;
  readonly isCompact: boolean;
  readonly noDue: ReactNode;
  readonly content: Omit<MonthTableProps, 'title'>;
  readonly title: string;
}

/** 月曆本體：說明原因、載入失敗、載入中，或月曆（窄螢幕是議程，沒有到期日的說明放在最下面）。 */
function CalendarBody({ problem, all, isFirstLoad, isCompact, noDue, content, title }: CalendarBodyProps) {
  if (problem !== null) return problem;
  if (all.isError) return <ErrorState error={all.error} title="項目載入失敗" onRetry={all.retry} />;
  if (isFirstLoad) return <LoadingState label="正在載入行事曆…" />;
  if (isCompact) {
    return (
      <>
        <Agenda {...content} />
        {noDue}
      </>
    );
  }
  return (
    <div className={styles.scroll} aria-busy={all.isStale || all.isLoading}>
      <MonthTable {...content} title={title} />
    </div>
  );
}

/**
 * 行事曆的畫面：一個月一頁，項目排在到期日那一天（或從開始日畫到到期日，個人偏好；窄螢幕的議程一律依到期日），唯讀。專案頁（CalendarView）與「我的工作」（MyWorkCalendar）共用，
 * 兩邊差在去哪裡讀項目、有沒有週期與里程碑的標示、沒有到期日的連結去哪，都由上層傳進來。
 * 月份在網址的 month（YYYY-MM），沒有就是今天所在的月；讀的範圍是畫面上看得到的那幾週（含頭尾上下個月的日子）。
 */
export function CalendarFrame({
  source,
  todayDate,
  projectKey,
  cycles = NO_CYCLES,
  milestones = NO_MILESTONES,
  isWaiting = false,
  problem = null,
  visibleQuery,
  isDueNoneFilter = false,
  isAssigneeShown = false,
  renderNoDue,
}: CalendarFrameProps) {
  const [searchParams] = useSearchParams();
  const isCompact = useIsCompact();
  const preferredMode = useCalendarDateMode();
  // 窄螢幕的議程本來就依日期列出，不放切換，也不管偏好。
  const mode: CalendarDateMode = isCompact ? 'due' : preferredMode;
  const weekStart = useWeekStart();
  const month = monthOf(searchParams.get('month'), todayDate);
  const grid = useMemo(() => monthGrid(month, weekStart), [month, weekStart]);
  const query = isWaiting || problem !== null ? null : visibleQuery(grid, weekStart, mode);
  const all = useAllItems(source, query);
  const days = useMemo(() => (mode === 'due' ? itemsByDay(all.items) : EMPTY_DAYS), [mode, all.items]);
  const spanItems = useMemo(
    () => (mode === 'span' ? all.items.filter((item) => !isDueNoneFilter || item.dueDate === null) : NO_ITEMS),
    [mode, all.items, isDueNoneFilter],
  );
  const spanLayout = useMemo(() => (mode === 'span' ? layoutWeeks(grid.weeks, spanItems) : null), [mode, grid.weeks, spanItems]);
  const markers = useMemo(() => markersByDay(cycles, milestones), [cycles, milestones]);
  const regionRef = useRef<HTMLDivElement>(null);
  const scope = useMemo<CalendarScope>(() => ({ todayDate, projectKey, isAssigneeShown }), [todayDate, projectKey, isAssigneeShown]);
  // 面板裡的「上一個、下一個」走行事曆上的順序：依到期日排時日期由早到晚、同一天依優先度與編號；期間排法照開始日、再照編號。
  useItemSequence(
    mode === 'span' ? itemSequence(spanItems) : grid.weeks.flat().flatMap((date) => (days.get(date) ?? []).map((item) => item.key)),
    regionRef,
  );
  const isFirstLoad = isWaiting || (all.isLoading && all.items.length === 0 && !all.isStale);
  const content = { month, grid, weekStart, todayDate, days, markers, spanLayout };
  const noDue = !isWaiting && problem === null ? renderNoDue(isCompact ? 'bottom' : 'top', mode) : null;
  return (
    <CalendarScopeContext value={scope}>
      <div ref={regionRef} role="region" aria-label="行事曆" className={styles.view}>
        <CalendarBar month={month} todayDate={todayDate} />
        {all.isTruncated && <TruncatedNotice />}
        {!isCompact && noDue}
        <CalendarBody problem={problem} all={all} isFirstLoad={isFirstLoad} isCompact={isCompact} noDue={noDue} content={content} title={monthTitle(month)} />
      </div>
    </CalendarScopeContext>
  );
}
