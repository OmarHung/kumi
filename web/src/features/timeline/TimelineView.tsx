import { useMemo } from 'react';
import type { CalendarDate } from '../../api/types';
import { EmptyState } from '../../components/StatusViews/StatusViews';
import { hasNoCurrentCycle } from '../../domain/planningFilter';
import { boardFiltersToSearchParams, type DateViewFilters } from '../../domain/projectView';
import { projectDateViewSource } from '../calendar/dateViewSource';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { NoCurrentCycleHint, PlanningLoadError } from '../item-table/PlanningStates';
import { isPlanningFilterBlocked, usePlanning } from '../item-table/usePlanning';
import type { TimelineRow } from './timelineModel';
import { TimelineFrame } from './TimelineFrame';
import { TimelineList } from './TimelineList';
import { projectTimelineQueries } from './useTimelineData';

export interface ProjectDateViewProps {
  readonly projectKey: string;
  /** 工具列的篩選，和看板同一個型別。 */
  readonly filters: DateViewFilters;
  /** 「今天」：整個專案頁同一個，過期的標示才一致。 */
  readonly todayDate: CalendarDate;
}

/** 連到表格、只看沒有到期日的：其他條件照舊帶著，流程是看板才有的不帶。 */
function undatedTablePath(projectKey: string, filters: DateViewFilters): string {
  // 類型與狀態表格也有，帶著；這裡借用時間軸的寫法，之後把 view 拿掉。
  const params = boardFiltersToSearchParams({ ...filters, due: 'none' }, undefined, 'timeline');
  params.delete('view');
  params.delete('workflow');
  return `/projects/${encodeURIComponent(projectKey)}?${params.toString()}`;
}

/** 窄螢幕的清單：狀態的名稱用這個專案的藍圖。 */
function ProjectTimelineList({ rows }: { readonly rows: readonly TimelineRow[] }) {
  const { detail } = useProjectTable();
  return <TimelineList rows={rows} blueprintOf={() => detail.blueprint} />;
}

/**
 * 專案的時間軸（畫面在 TimelineFrame）：讀這個專案的清單，標示週期與里程碑，沒有日期的連到專案的表格。
 * 週期與里程碑的清單是篩選要用的：讀不到時說讀取失敗，「目前的週期」而現在沒有時說明原因。
 */
export function TimelineView({ projectKey, filters, todayDate }: ProjectDateViewProps) {
  const planning = usePlanning(projectKey);
  const source = useMemo(() => projectDateViewSource(projectKey), [projectKey]);
  const isWaiting = (filters.cycle !== null || filters.milestone !== null) && planning.isPending;
  const blocker = planningBlocker(planning, filters, todayDate);
  return (
    <TimelineFrame
      source={source}
      todayDate={todayDate}
      queriesFor={(window, weekStart) => projectTimelineQueries(filters, window, todayDate, weekStart, planning.cycles)}
      cycles={planning.cycles}
      milestones={planning.milestones}
      isWaiting={isWaiting}
      blocker={blocker}
      undatedHref={undatedTablePath(projectKey, filters)}
      renderList={(rows) => <ProjectTimelineList rows={rows} />}
    />
  );
}

function planningBlocker(planning: ReturnType<typeof usePlanning>, filters: DateViewFilters, todayDate: CalendarDate) {
  if (isPlanningFilterBlocked(planning, filters)) return <PlanningLoadError planning={planning} />;
  if (!hasNoCurrentCycle(filters.cycle, planning.cycles, todayDate)) return null;
  return (
    <EmptyState title="沒有符合條件的項目">
      <NoCurrentCycleHint />
    </EmptyState>
  );
}
