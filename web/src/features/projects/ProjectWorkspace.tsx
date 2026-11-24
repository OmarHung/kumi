import { lazy, Suspense, type ReactNode } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import type { ProjectDetail } from '../../api/types';
import { SelectField } from '../../components/Field/Field';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { Toolbar } from '../../components/Toolbar/Toolbar';
import { useIsCompact } from '../../lib/useMediaQuery';
import { useToday } from '../../lib/useToday';
import { queryFromBoardFilters, queryFromTableFilters, type ViewState } from '../../domain/savedViews';
import { switchViewParams, viewFromSearchParams, type ProjectView } from '../../domain/projectView';
import { BoardToolbarControls } from '../board/BoardToolbar';
import { useBoardFilters } from '../board/useBoardFilters';
import { BulkActionBar } from '../bulk-edit/BulkActionBar';
import { ItemPanelHost } from '../item-page/ItemPanelHost';
import { ItemTableView } from '../item-table/ItemTableView';
import { ItemToolbarControls, ItemToolbarEnd } from '../item-table/ItemToolbar';
import { abilitiesOf, ProjectTableProvider } from '../item-table/ProjectTableContext';
import { useItemTableState } from '../item-table/useItemTableState';
import { ViewMenu } from '../saved-views/ViewMenu';
import { panelItemOf, searchWithPanelItem } from '../work-items/itemLinks';
import { useRememberProjectView } from './projectViewPreference';

// 看板帶著拖曳的套件，只有打開看板的人才需要：拆成另一個檔案，表格與其他頁面不必先下載它。
// 工具列上的看板篩選（BoardToolbar、useBoardFilters）不帶拖曳的套件，直接匯入。
const BoardView = lazy(() => import('../board/BoardView').then((module) => ({ default: module.BoardView })));
// 行事曆與時間軸一樣是 lazy：只有打開它們的人才下載。兩者的 props 一樣（ProjectDateViewProps，見 features/calendar/CalendarView.tsx）。
const CalendarView = lazy(() => import('../calendar/CalendarView').then((module) => ({ default: module.CalendarView })));
const TimelineView = lazy(() => import('../timeline/TimelineView').then((module) => ({ default: module.TimelineView })));

const VIEWS: readonly { readonly view: ProjectView; readonly label: string }[] = [
  { view: 'table', label: '表格' },
  { view: 'board', label: '看板' },
  { view: 'calendar', label: '行事曆' },
  { view: 'timeline', label: '時間軸' },
];

/**
 * 表格、看板、行事曆與時間軸的切換。寬螢幕是連結的分頁：網址就是狀態，可以在新分頁打開、上一頁回到原本的檢視。
 * 48rem 以下是一個下拉（標籤視覺上隱藏）：四個分頁加「檢視」會把第一列排滿，「篩選」掉到第二列、內容整個往下推（docs/DESIGN.md 6.1）；
 * 選了就導到同樣的網址，所以帶過去的條件與記住上次的版面都和分頁一樣。
 * 開著的項目面板（item）跟著帶過去：換檢視不該把正在看的項目關掉。
 */
function ViewSwitch({ view }: { readonly view: ProjectView }) {
  const [searchParams] = useSearchParams();
  const isCompact = useIsCompact();
  const navigate = useNavigate();
  const panelItem = panelItemOf(searchParams);
  const tabs = VIEWS.map((option) => {
    const isCurrent = option.view === view;
    // 目前的檢視連到自己（條件不變）；另一個只帶兩邊都有的條件。
    const params = isCurrent ? searchParams : switchViewParams(searchParams, option.view);
    const search = isCurrent ? `?${params.toString()}` : searchWithPanelItem(`?${params.toString()}`, panelItem);
    return { key: option.view, label: option.label, to: { search }, isCurrent };
  });
  if (!isCompact) {
    return <NavTabs label="版面" tabs={tabs} />;
  }
  return (
    <SelectField
      label="版面"
      size="sm"
      isLabelHidden
      value={view}
      options={tabs.map((tab) => ({ value: tab.key, label: tab.label }))}
      onChange={(next) => {
        const target = tabs.find((tab) => tab.key === next);
        if (target !== undefined && !target.isCurrent) {
          void navigate(target.to);
        }
      }}
    />
  );
}

interface ProjectWorkspaceProps {
  readonly detail: ProjectDetail;
  /** 頁列（PageBar）。 */
  readonly bar: ReactNode;
  /** 內容最上面固定的提示（背景重抓失敗、換頁後的提示）：切換檢視時不重新掛載。 */
  readonly notices: ReactNode;
  readonly onOpenMembers: () => void;
}

/**
 * 專案頁的版面：頁列、工具列（檢視分頁加上這個檢視的控制項）、內容（表格、看板、行事曆或時間軸，記在網址的 view 參數）。
 * 四種檢視都是工作區頁（剛好一個畫面高；表格自己兩個方向捲，看板每一欄自己直向捲，行事曆與時間軸在內容區裡自己捲），用同一個 PageLayout，
 * 切換時工具列上的分頁是同一個元素（焦點與報讀軟體的位置不會跳走）。
 * 外殼（ProjectPage）已經處理好載入、錯誤與找不到，這裡拿到的一定是載入好的專案。
 */
export function ProjectWorkspace({ detail, bar, notices, onOpenMembers }: ProjectWorkspaceProps) {
  const [searchParams] = useSearchParams();
  const isCompact = useIsCompact();
  const view = viewFromSearchParams(searchParams);
  const isTable = view === 'table';
  const table = useItemTableState(detail);
  const board = useBoardFilters(detail);
  // 整張表與整個看板用同一個「今天」，過期的標示才一致；回到分頁時重取，開過夜的頁面不會還查昨天的「今天到期」。
  const todayDate = useToday();
  const projectKey = detail.project.key;
  // 記住這個專案上次用的檢視：側欄與麵包屑的專案連結照它開（projectViewPreference.ts）。
  useRememberProjectView(projectKey, view, searchParams.has('view'));
  // 存檢視、比對檢視用的：目前畫面上的版面與條件。
  const current: ViewState = isTable
    ? { layout: 'table', query: queryFromTableFilters(table.filters) }
    : { layout: view, query: queryFromBoardFilters(board.filters) };
  // 選了項目時，工具列那一列換成批次動作列（同一個位置、同樣高度）；窄螢幕的清單不能多選。
  const isSelecting = isTable && !isCompact && table.selection.count > 0;
  const toolbar = isSelecting ? (
    <Toolbar>
      <BulkActionBar selection={table.selection} bulk={table.bulk} />
    </Toolbar>
  ) : (
    <Toolbar end={isTable ? <ItemToolbarEnd table={table} /> : undefined}>
      <ViewSwitch view={view} />
      <ViewMenu key={projectKey} current={current} />
      {isTable ? <ItemToolbarControls table={table} /> : <BoardToolbarControls board={board} view={view} />}
    </Toolbar>
  );
  return (
    <ProjectTableProvider value={{ detail, can: abilitiesOf(detail), todayDate, onOpenMembers }}>
      <PageLayout variant="workspace" bar={bar} toolbar={toolbar}>
        {/* 點表格的編號、看板卡片的標題、行事曆與時間軸上的項目在右邊開項目面板；各檢視共用同一個面板，切換檢視時面板不重新掛上。 */}
        <ItemPanelHost layout="fill" projectKey={projectKey}>
          {notices}
          {isTable ? (
            <ItemTableView table={table} />
          ) : (
            <Suspense fallback={<LoadingState label={`正在載入${VIEWS.find((option) => option.view === view)?.label ?? ''}…`} />}>
              {view === 'board' && <BoardView board={board} />}
              {view === 'calendar' && <CalendarView projectKey={projectKey} filters={board.filters} todayDate={todayDate} />}
              {view === 'timeline' && <TimelineView projectKey={projectKey} filters={board.filters} todayDate={todayDate} />}
            </Suspense>
          )}
        </ItemPanelHost>
      </PageLayout>
    </ProjectTableProvider>
  );
}
