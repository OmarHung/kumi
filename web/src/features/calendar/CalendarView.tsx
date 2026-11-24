import { useMemo } from 'react';
import type { CalendarDate } from '../../api/types';
import { hasNoCurrentCycle } from '../../domain/planningFilter';
import type { DateViewFilters } from '../../domain/projectView';
import { NoCurrentCycleHint, PlanningLoadError } from '../item-table/PlanningStates';
import { isPlanningFilterBlocked, usePlanning, type Planning } from '../item-table/usePlanning';
import { visibleItemsQuery, visibleSpanQuery } from './calendarData';
import { CalendarFrame } from './CalendarFrame';
import { projectDateViewSource } from './dateViewSource';
import { NoDueNotice } from './NoDueNotice';

/**
 * 行事曆與時間軸（features/timeline/TimelineView.tsx）的元件介面：一樣，自己讀資料（項目、週期、里程碑），
 * 自己管網址上自己的參數。篩選的型別是 DateViewFilters（domain/projectView.ts）：工具列上和看板同一套條件再加類型與狀態，
 * 流程只有看板用，這兩種版面忽略它。
 */
export interface ProjectDateViewProps {
  readonly projectKey: string;
  readonly filters: DateViewFilters;
  /** 「今天」（lib/useToday.ts）：整個專案頁用同一個，過期與今天的標示才一致。 */
  readonly todayDate: CalendarDate;
}

/** 週期與里程碑的清單是篩選要用的：讀不到時說讀取失敗，不能當成「沒有」。 */
function planningProblem(planning: Planning, filters: DateViewFilters, todayDate: CalendarDate) {
  if (isPlanningFilterBlocked(planning, filters)) return <PlanningLoadError planning={planning} />;
  if (!planning.isCyclesPending && hasNoCurrentCycle(filters.cycle, planning.cycles, todayDate)) {
    return <NoCurrentCycleHint />;
  }
  return null;
}

/** 專案的行事曆（畫面在 CalendarFrame）：讀這個專案的清單，標示週期與里程碑，沒有到期日的連到專案的表格。 */
export function CalendarView({ projectKey, filters, todayDate }: ProjectDateViewProps) {
  const planning = usePlanning(projectKey);
  const source = useMemo(() => projectDateViewSource(projectKey), [projectKey]);
  return (
    <CalendarFrame
      source={source}
      todayDate={todayDate}
      projectKey={projectKey}
      cycles={planning.cycles}
      milestones={planning.milestones}
      isWaiting={planning.isPending}
      problem={planningProblem(planning, filters, todayDate)}
      visibleQuery={(grid, weekStart, mode) =>
        (mode === 'span' ? visibleSpanQuery : visibleItemsQuery)(filters, grid, todayDate, weekStart, planning.cycles)
      }
      isDueNoneFilter={filters.due === 'none'}
      renderNoDue={(placement, mode) => (
        <NoDueNotice projectKey={projectKey} filters={filters} todayDate={todayDate} cycles={planning.cycles} mode={mode} placement={placement} />
      )}
    />
  );
}
