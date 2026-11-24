import { useMemo } from 'react';
import type { ProjectBlueprint } from '../../api/blueprint';
import { useProjectDetails } from '../../api/queries';
import type { Item } from '../../api/types';
import { myWorkConditions, myWorkSearchParams, MY_WORK_PAGE_SIZE, type MyWorkFilters } from '../../domain/myWorkView';
import type { WeekStart } from '../../lib/weekStart';
import { MY_WORK_DATE_VIEW_SOURCE } from '../calendar/dateViewSource';
import type { TimelineRow } from '../timeline/timelineModel';
import type { TimelineWindow } from '../timeline/timelineRange';
import { TimelineFrame } from '../timeline/TimelineFrame';
import { TimelineList } from '../timeline/TimelineList';
import type { TimelineQueries } from '../timeline/useTimelineData';
import type { MyWorkDateViewProps } from './MyWorkCalendar';

/**
 * 這段時間要讀的兩份清單，做法和專案的時間軸一樣：「沒有到期日」後端不能和日期範圍一起用，
 * 日期範圍那份不帶到期條件、在畫面上把有到期日的拿掉；沒有日期的那份只在到期條件是「沒有到期日」或沒設時才讀。
 */
function queriesOf(filters: MyWorkFilters, window: TimelineWindow, todayDate: MyWorkDateViewProps['todayDate'], weekStart: WeekStart): TimelineQueries {
  const isDuelessOnly = filters.due === 'none';
  const dated = myWorkConditions({ ...filters, due: isDuelessOnly ? null : filters.due }, todayDate, weekStart);
  const undated = filters.due === null || isDuelessOnly ? myWorkConditions({ ...filters, due: null }, todayDate, weekStart) : null;
  return {
    dated: dated === null ? null : { ...dated, datesFrom: window.from, datesTo: window.to, sort: { field: 'number', direction: 'asc' }, limit: MY_WORK_PAGE_SIZE },
    undated: undated === null ? null : { ...undated, noDue: true, limit: MY_WORK_PAGE_SIZE },
    dropsDueItems: isDuelessOnly,
  };
}

/** 窄螢幕的清單：每個項目的狀態名稱用它自己專案的藍圖（還沒讀到時 TimelineList 用類別的名稱）。 */
function MyWorkTimelineList({ rows }: { readonly rows: readonly TimelineRow[] }) {
  const projectKeys = useMemo(() => [...new Set(rows.map((row) => row.item.projectKey))], [rows]);
  const projects = useProjectDetails(projectKeys);
  const blueprintOf = (item: Item): ProjectBlueprint | undefined => projects.get(item.projectKey)?.blueprint;
  return <TimelineList rows={rows} blueprintOf={blueprintOf} />;
}

/**
 * 「我的工作」的時間軸（畫面在 TimelineFrame）：讀跨專案的清單，沒有週期與里程碑的標示。
 * 沒有日期的項目連到「我的工作」的表格，條件照舊再加「沒有到期日」；刻度與第一天在網址的 scale、from，和專案頁一樣。
 */
export function MyWorkTimeline({ filters, todayDate }: MyWorkDateViewProps) {
  const undatedSearch = myWorkSearchParams('table', { ...filters, due: 'none' }).toString();
  return (
    <TimelineFrame
      source={MY_WORK_DATE_VIEW_SOURCE}
      todayDate={todayDate}
      queriesFor={(window, weekStart) => queriesOf(filters, window, todayDate, weekStart)}
      undatedHref={`/my-work?${undatedSearch}`}
      renderList={(rows) => <MyWorkTimelineList rows={rows} />}
    />
  );
}
