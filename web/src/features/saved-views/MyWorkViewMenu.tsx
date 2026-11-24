import { useItemTypes, useMyWorkViews, useProjectDetails } from '../../api/queries';
import { defaultMyWorkViewName, hasMyWorkViewConditions, myWorkQueryOf, myWorkViewMatches, myWorkViewParams, type MyWorkViewState } from '../../domain/myWorkViews';
import { filterableStates } from '../../domain/workflow';
import { MY_WORK_VIEW_API } from './useSavedViewMutations';
import { useViewActionsFor } from './useViewActions';
import { ViewMenuShell } from './ViewMenuShell';

interface MyWorkViewMenuProps {
  /** 目前畫面上的版面、條件與表格的分組。 */
  readonly current: MyWorkViewState;
}

/**
 * 「我的工作」工具列上的「檢視」：和專案頁的一樣，只是檢視存在使用者自己名下、不屬於任何專案，
 * 套用時導到 /my-work 加上那組條件，開著的面板照專案頁的做法留著（keepPanelItem）。
 */
export function MyWorkViewMenu({ current }: MyWorkViewMenuProps) {
  const views = useMyWorkViews().data ?? [];
  const types = useItemTypes().data ?? [];
  // 只有選了專案才會有個別的狀態；名稱要那個專案的藍圖。
  const { project: projectKey, type } = current.filters;
  const details = useProjectDetails(projectKey === null ? [] : [projectKey]);
  const detail = projectKey === null ? undefined : details.get(projectKey);
  const projectStates = detail === undefined ? null : filterableStates(detail.blueprint, type);
  const stored = { layout: current.layout, query: myWorkQueryOf(current) };
  const actions = useViewActionsFor({ api: MY_WORK_VIEW_API, current: stored, paramsOf: myWorkViewParams });
  return (
    <ViewMenuShell
      views={views}
      currentLayout={current.layout}
      matches={(view) => myWorkViewMatches(view, current)}
      hasConditions={hasMyWorkViewConditions(current)}
      suggestedName={defaultMyWorkViewName(current, { types, projectStates })}
      actions={actions}
    />
  );
}
