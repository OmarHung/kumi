import { lazy, Suspense, useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { createColumnWidthStore } from '../../components/Table/columnWidths';
import { useMyWork } from '../../api/queries';
import { useCurrentUser } from '../../auth/CurrentUser';
import type { CalendarDate, Item } from '../../api/types';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { isTerminal } from '../../domain/dates';
import {
  DEFAULT_MY_WORK_FILTERS,
  hasActiveMyWorkFilters,
  matchesMyWorkFilters,
  myWorkConditions,
  type MyWorkFilters,
  type MyWorkLayout,
} from '../../domain/myWorkView';
import { updateThenFocus, type FocusTarget } from '../../lib/updateThenFocus';
import { useArrivalNotice } from '../../lib/useArrivalNotice';
import { useToday } from '../../lib/useToday';
import { useWeekStart } from '../../lib/weekStart';
import type { RovingGridHandle } from '../../lib/useRovingGrid';
import { UndoableArrival } from '../bulk-edit/UndoableArrival';
import { ItemPanelHost } from '../item-page/ItemPanelHost';
import { StateAnnouncementContext, type StateAnnouncement } from '../item-table/cells/stateAnnouncement';
import { flattenItemPages } from '../item-table/itemCache';
import { createColumnOrderStore, MY_WORK_ORDER_KEY } from './myWorkColumnOrder';
import { MY_WORK_WIDTHS_KEY } from './myWorkColumns';
import { pruneToProject } from './myWorkChoices';
import { MyWorkResults } from './MyWorkResults';
import { myWorkStatus } from './myWorkStatus';
import { MyWorkToolbar } from './MyWorkToolbar';
import { useMyWorkItems } from './useMyWorkItems';
import { useMyWorkRows } from './useMyWorkRows';
import { useMyWorkScope } from './useMyWorkScope';
import { useMyWorkUrlState } from './useMyWorkUrlState';

const NO_ITEMS: readonly Item[] = [];

// 看板帶著拖曳的套件、行事曆與時間軸只有打開它們的人才需要：和專案頁一樣拆成另外的檔案（lazy）。
const MyWorkBoard = lazy(() => import('./board/MyWorkBoard').then((module) => ({ default: module.MyWorkBoard })));
const MyWorkCalendar = lazy(() => import('./MyWorkCalendar').then((module) => ({ default: module.MyWorkCalendar })));
const MyWorkTimeline = lazy(() => import('./MyWorkTimeline').then((module) => ({ default: module.MyWorkTimeline })));

const LAYOUT_LABELS: Readonly<Record<Exclude<MyWorkLayout, 'table'>, string>> = { board: '看板', calendar: '行事曆', timeline: '時間軸' };

interface OtherLayoutProps {
  readonly layout: Exclude<MyWorkLayout, 'table'>;
  readonly filters: MyWorkFilters;
  readonly todayDate: CalendarDate;
  /** 選了的專案讀到了（沒選時一直是 true）：沒讀到之前網址上的類型、狀態可能是舊專案的，不送請求。 */
  readonly isReady: boolean;
  /** 看板關上說明、卡片都不在畫面上時焦點的去處。 */
  readonly focusFallback: readonly FocusTarget[];
}

/** 看板、行事曆、時間軸：各自讀自己的資料（條件一樣，讀的範圍不同）。 */
function OtherLayout({ layout, filters, todayDate, isReady, focusFallback }: OtherLayoutProps) {
  if (!isReady) return <LoadingState label={`正在載入${LAYOUT_LABELS[layout]}…`} />;
  return (
    <Suspense fallback={<LoadingState label={`正在載入${LAYOUT_LABELS[layout]}…`} />}>
      {layout === 'board' && <MyWorkBoard filters={filters} todayDate={todayDate} focusFallback={focusFallback} />}
      {layout === 'calendar' && <MyWorkCalendar filters={filters} todayDate={todayDate} />}
      {layout === 'timeline' && <MyWorkTimeline filters={filters} todayDate={todayDate} />}
    </Suspense>
  );
}

/**
 * 條件（含選的專案）、「今天」與週的第一天都定下來之後的東西：給伺服器的條件、項目符不符合條件、
 * 剛改成完成或取消的項目會不會離開清單。
 */
function useMyWorkConditions(filters: MyWorkFilters) {
  const me = useCurrentUser().personId;
  const todayDate = useToday();
  const weekStart = useWeekStart();
  const conditions = useMemo(() => myWorkConditions(filters, todayDate, weekStart), [filters, todayDate, weekStart]);
  // 剛改成完成、取消的項目只有在它真的不再符合條件時才會離開（條件是「所有狀態」時它還符合）。
  const isLeaving = useCallback(
    (item: Item) => isTerminal(item.stateCategory) && !matchesMyWorkFilters(item, filters, me, todayDate, weekStart),
    [filters, me, todayDate, weekStart],
  );
  return { conditions, todayDate, isLeaving };
}

/** 網址上的條件、讀到的項目與畫面上的列。選了專案時，類型、狀態要在那個專案裡；讀到專案的內容之前清單先不送。 */
function useMyWorkData(urlFilters: MyWorkFilters, setFilters: (next: MyWorkFilters) => void, isTable: boolean) {
  // 側欄的數量永遠是「我負責、還沒結束的」，不跟著條件變：讀的是這一份（MyWorkCount 只讀已經載入的）。
  useMyWork();
  const scope = useMyWorkScope(urlFilters.project);
  const filters = pruneToProject(urlFilters, scope.detail);
  useEffect(() => {
    if (filters !== urlFilters) setFilters(filters);
  }, [filters, urlFilters, setFilters]);
  const { conditions, todayDate, isLeaving } = useMyWorkConditions(filters);
  // 表格的清單只有表格要：看板、行事曆、時間軸各自讀，不必多打一份。
  const result = useMyWorkItems(conditions, scope.isReady && isTable);
  const listed = useMemo(() => (conditions === null ? NO_ITEMS : flattenItemPages(result.data)), [conditions, result.data]);
  // 條件換了就重新開始記：原本的列是在原本的條件下記的。
  const rows = useMyWorkRows(listed, JSON.stringify(conditions));
  // 工具列只有「這一頁從來沒有載入成功過」才不放：換條件後新的查詢沒成功過（isLoadingError），但舊的清單讀過了，
  // 使用者要用工具列改條件或清除條件才走得出去。
  const [hasLoaded, setHasLoaded] = useState(false);
  if (result.isSuccess && !hasLoaded) setHasLoaded(true);
  return {
    filters,
    isReady: scope.isReady,
    isLoadFailed: conditions !== null && result.isLoadingError && !hasLoaded,
    isImpossible: conditions === null,
    isFiltered: hasActiveMyWorkFilters(filters),
    todayDate,
    isLeaving,
    result,
    listed,
    rows,
  };
}

/**
 * 頁面上的控制：標題、搜尋框與「篩選」按鈕（焦點的去處）、表格的欄寬。
 * 「重設欄寬」「清除條件」按了就跟著不見：焦點不接手會掉回頁面本體（lib/updateThenFocus.ts）。
 */
function usePageControls(setFilters: (next: MyWorkFilters) => void) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const filterButtonId = useId();
  const [widthStore] = useState(() => createColumnWidthStore(MY_WORK_WIDTHS_KEY));
  const [orderStore] = useState(() => createColumnOrderStore(MY_WORK_ORDER_KEY));
  const resetWidths = () => {
    updateThenFocus(widthStore.reset, [headingRef]);
  };
  // 焦點到搜尋框；窄螢幕搜尋框收在浮層裡，到「篩選」按鈕。
  const clearFilters = () => {
    updateThenFocus(() => {
      setFilters(DEFAULT_MY_WORK_FILTERS);
    }, [searchRef, filterButtonId]);
  };
  return { headingRef, searchRef, filterButtonId, widthStore, orderStore, resetWidths, clearFilters };
}

/**
 * 「我的工作」：跨專案的項目，版面（表格、看板、行事曆、時間軸）、條件與分組都在網址（domain/myWorkView.ts）。
 * 預設是我負責、還沒結束的。表格是清單頁（整頁捲動、表頭黏在頁列下面；說明文字不放頁列），其他三種是工作區頁（剛好一個畫面高）。
 * 剛改成完成或取消而不再符合條件的留在原位直到離開這一頁，或條件換了（useMyWorkRows 記在這一層：切換分組、寬窄、開關面板都還在）。
 * 狀態格（表格與項目面板裡的）補填完成的那一句交給這一頁（StateAnnouncementContext），和工具列的那一句合成一句唸。
 * 表格的欄寬與欄的順序也在這一層（工具列的「重設欄寬」「欄的順序」與表格共用；切換分組、寬窄時不重讀）。
 */
export function MyWorkPage() {
  const gridRef = useRef<RovingGridHandle>(null);
  const { layout, urlFilters, setFilters } = useMyWorkUrlState();
  const isTable = layout === 'table';
  const controls = usePageControls(setFilters);
  const { headingRef, searchRef, filterButtonId, widthStore, orderStore, clearFilters } = controls;
  // 在項目面板裡刪除之後留在這裡：焦點到標題，「已刪除 …」填進一直掛著的 status。
  const arrival = useArrivalNotice(headingRef);
  const { filters, isLoadFailed, ...data } = useMyWorkData(urlFilters, setFilters, isTable);
  const [fill, setFill] = useState<StateAnnouncement | null>(null);
  // 工具列一直都在（換條件、清除條件都要用它）；只有第一次就讀不到時不放。載入中先放著，清單出來時不會被往下推。
  const toolbar = (
    <MyWorkToolbar
      layout={layout}
      filters={filters}
      onFiltersChange={setFilters}
      onClear={clearFilters}
      searchRef={searchRef}
      filterButtonId={filterButtonId}
      message={isTable ? myWorkStatus(data.rows, fill, data.isLeaving) : ''}
      widthStore={widthStore}
      orderStore={orderStore}
      onResetWidths={controls.resetWidths}
    />
  );
  return (
    <StateAnnouncementContext value={setFill}>
      <PageLayout
        variant={isTable ? 'list' : 'workspace'}
        bar={<PageBar title="我的工作" headingRef={headingRef} />}
        toolbar={isLoadFailed ? undefined : toolbar}
      >
        {/* 點標題在右邊開項目面板（跨專案：面板用那個項目自己的專案內容）。 */}
        <ItemPanelHost layout={isTable ? 'page' : 'fill'}>
          <UndoableArrival message={arrival} />
          {isTable ? (
            <MyWorkResults {...data} onClear={clearFilters} widthStore={widthStore} orderStore={orderStore} gridRef={gridRef} />
          ) : (
            <OtherLayout layout={layout} filters={filters} todayDate={data.todayDate} isReady={data.isReady} focusFallback={[searchRef, filterButtonId, headingRef]} />
          )}
        </ItemPanelHost>
      </PageLayout>
    </StateAnnouncementContext>
  );
}
