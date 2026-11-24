import type { CalendarDate, ItemQuery } from '../../api/types';
import { myWorkConditions, myWorkSearchParams, type MyWorkFilters } from '../../domain/myWorkView';
import { useWeekStart, type WeekStart } from '../../lib/weekStart';
import type { CalendarDateMode } from '../../lib/calendarDateMode';
import { CALENDAR_PAGE_SIZE, earlierOf, laterOf } from '../calendar/calendarData';
import { CalendarFrame } from '../calendar/CalendarFrame';
import type { MonthGrid } from '../calendar/calendarMonth';
import { MY_WORK_DATE_VIEW_SOURCE, useDateViewPages } from '../calendar/dateViewSource';
import { NoDueMessage } from '../calendar/NoDueNotice';
import { useUnplacedCount } from '../calendar/useUnplacedCount';

export interface MyWorkDateViewProps {
  readonly filters: MyWorkFilters;
  /** 「今天」：整頁用同一個，過期與今天的標示才一致。 */
  readonly todayDate: CalendarDate;
}

/**
 * 期間排法：讀和畫面那幾週期間重疊的（datesFrom、datesTo），到期條件照帶、不和畫面取交集；
 * 「沒有到期日」後端不能和日期範圍一起用，不帶、讀回來由畫面把有到期日的拿掉（CalendarFrame 的 isDueNoneFilter）。
 */
function visibleSpanQuery(filters: MyWorkFilters, grid: MonthGrid, todayDate: CalendarDate, weekStart: WeekStart): ItemQuery | null {
  const conditions = myWorkConditions({ ...filters, due: filters.due === 'none' ? null : filters.due }, todayDate, weekStart);
  if (conditions === null) return null;
  return { ...conditions, datesFrom: grid.start, datesTo: grid.end, sort: { field: 'number', direction: 'asc' }, limit: CALENDAR_PAGE_SIZE };
}

/**
 * 畫面上看得到的那幾週：條件照「我的工作」的，到期日和畫面的範圍取交集。
 * null 是一定沒有項目（條件互相矛盾、到期條件落在畫面之外、或「沒有到期日」：月曆上沒有那一天）。
 */
function visibleQuery(filters: MyWorkFilters, grid: MonthGrid, todayDate: CalendarDate, weekStart: WeekStart): ItemQuery | null {
  const conditions = myWorkConditions(filters, todayDate, weekStart);
  if (conditions === null || conditions.noDue === true) return null;
  const dueFrom = laterOf(grid.start, conditions.dueFrom);
  const dueTo = earlierOf(grid.end, conditions.dueTo);
  if (dueFrom > dueTo) return null;
  return { ...conditions, dueFrom, dueTo, sort: { field: 'due_date', direction: 'asc' }, limit: CALENDAR_PAGE_SIZE };
}

/** 沒有到期日的：同樣的條件、只看沒有到期日的；到期條件是已過期、今天、本週時一定不符合，回 null。 */
function noDueConditions(filters: MyWorkFilters, todayDate: CalendarDate, weekStart: WeekStart): ItemQuery | null {
  if (filters.due !== null && filters.due !== 'none') return null;
  const conditions = myWorkConditions({ ...filters, due: null }, todayDate, weekStart);
  return conditions === null ? null : { ...conditions, noDue: true, limit: CALENDAR_PAGE_SIZE };
}

interface NoDueProps extends MyWorkDateViewProps {
  readonly placement: 'top' | 'bottom';
  readonly mode: CalendarDateMode;
}

/** 「N 個項目沒有到期日」：連到「我的工作」的表格（條件照舊，加上「沒有到期日」）。只讀一頁，滿了就說「以上」。 */
function MyWorkNoDueNotice({ filters, todayDate, placement, mode }: NoDueProps) {
  const weekStart = useWeekStart();
  const query = noDueConditions(filters, todayDate, weekStart);
  const result = useDateViewPages(MY_WORK_DATE_VIEW_SOURCE, query ?? {}, query !== null);
  const { count, hasMore } = useUnplacedCount(result, mode);
  if (query === null || count === 0) return null;
  const search = myWorkSearchParams('table', { ...filters, due: 'none' }).toString();
  return <NoDueMessage count={count} hasMore={hasMore} mode={mode} placement={placement} to={{ pathname: '/my-work', search: `?${search}` }} />;
}

/**
 * 「我的工作」的行事曆（畫面在 CalendarFrame）：讀跨專案的清單，沒有週期與里程碑的標示。
 * 項目點了開項目面板（由頁面的 ItemPanelHost 提供）；月份在網址的 month，和專案頁一樣。
 */
export function MyWorkCalendar({ filters, todayDate }: MyWorkDateViewProps) {
  return (
    <CalendarFrame
      source={MY_WORK_DATE_VIEW_SOURCE}
      todayDate={todayDate}
      projectKey={null}
      visibleQuery={(grid, weekStart, mode) => (mode === 'span' ? visibleSpanQuery : visibleQuery)(filters, grid, todayDate, weekStart)}
      isDueNoneFilter={filters.due === 'none'}
      renderNoDue={(placement, mode) => <MyWorkNoDueNotice filters={filters} todayDate={todayDate} placement={placement} mode={mode} />}
    />
  );
}
