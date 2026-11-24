import { useSavedViews } from '../../api/queries';
import { useCurrentUser } from '../../auth/CurrentUser';
import { defaultViewName, hasViewConditions, viewMatches, type ViewState } from '../../domain/savedViews';
import { usePlanning } from '../item-table/usePlanning';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { useViewActions } from './useViewActions';
import { ViewMenuShell } from './ViewMenuShell';

interface ViewMenuProps {
  /** 目前畫面上的版面與條件（表格或看板）。 */
  readonly current: ViewState;
}

/**
 * 工具列上的「檢視」：個人存起來的版面與條件。點一個檢視就套用（導去那個網址：上一頁、分享網址照舊）。
 * 目前的條件和某個檢視完全一樣時，選單多了「重新命名」「刪除」；
 * 套用了某個檢視又改了條件時，多一個「更新成目前的條件」（動作在 useViewActions，選單本身在 ViewMenuShell）。
 */
export function ViewMenu({ current }: ViewMenuProps) {
  const { detail } = useProjectTable();
  const me = useCurrentUser().personId;
  const views = useSavedViews(detail.project.key).data ?? [];
  const planning = usePlanning(detail.project.key);
  const actions = useViewActions(detail.project.key, detail.blueprint, current);
  return (
    <ViewMenuShell
      views={views}
      currentLayout={current.layout}
      matches={(view) => viewMatches(view, current, detail.blueprint)}
      hasConditions={hasViewConditions(current, detail.blueprint)}
      suggestedName={defaultViewName(current.layout, current.query, { blueprint: detail.blueprint, cycles: planning.isCyclesFailed ? null : planning.cycles, milestones: planning.isMilestonesFailed ? null : planning.milestones, members: detail.members, me })}
      actions={actions}
    />
  );
}
