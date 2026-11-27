import { useMemo, useRef, type ReactNode, type Ref } from 'react';
import { Link } from 'react-router';
import { useProjectItems } from '../../api/queries';
import type { Item, ItemSortField } from '../../api/types';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState } from '../../components/StatusViews/StatusViews';
import { CURRENT_CYCLE, hasNoCurrentCycle } from '../../domain/planningFilter';
import { hasActiveFilters, nextSort, toItemQuery, type ItemFilters } from '../../domain/itemFilters';
import { useIsBelowWide, useIsCompact } from '../../lib/useMediaQuery';
import type { RovingGridHandle } from '../../lib/useRovingGrid';
import { SelectionProvider } from '../bulk-edit/SelectionContext';
import { useSyncSelection } from '../bulk-edit/useSyncSelection';
import { settingsSectionPath } from '../work-items/itemLinks';
import { DEFAULT_SORT, ItemTable } from './ItemTable';
import { flattenItemPages } from './itemCache';
import { focusLastPage, LoadMore, type ItemsResult } from './LoadMore';
import { NoCurrentCycleHint, PlanningLoadError } from './PlanningStates';
import { QuickAddSlot } from './QuickAddSlot';
import { ProjectItemList } from './ProjectItemList';
import { useProjectTable } from './ProjectTableContext';
import { isPlanningFilterBlocked, usePlanning } from './usePlanning';
import type { ItemTableState } from './useItemTableState';
import styles from './ItemTableView.module.css';
import { useWeekStart } from '../../lib/weekStart';

/** 和後端的預設一樣；一頁 50 個，往下用「載入更多」。 */
export const ITEMS_PAGE_SIZE = 50;

/** 第一次載入時骨架的列數：大約一個畫面，載入好之後不會跳。 */
const SKELETON_ROWS = 8;

/** 第一次載入：表頭與幾列的形狀（列高 --row-height），載入好之後表格出現在同一個位置。 */
function ResultsSkeleton({ isCompact, quickAdd }: { readonly isCompact: boolean; readonly quickAdd: ReactNode }) {
  return (
    <div className={styles.skeleton}>
      {!isCompact && <div className={styles.skeletonHead} aria-hidden="true" />}
      {quickAdd}
      <Skeleton label="正在載入項目…" rows={SKELETON_ROWS} />
    </div>
  );
}

interface EmptyResultsProps {
  readonly isFiltered: boolean;
  /** 篩了「目前的週期」、而現在沒有進行中的週期：空白是因為這個，不是沒有項目。 */
  readonly isNoCurrentCycle?: boolean;
  readonly onClear: () => void;
}

function EmptyResults({ isFiltered, isNoCurrentCycle = false, onClear }: EmptyResultsProps) {
  const { can } = useProjectTable();
  const isCompact = useIsCompact();
  // 窄螢幕一定也不到寬螢幕；兩個都問，不靠媒體查詢的實作剛好一致。
  const isBelowWide = useIsBelowWide() || isCompact;
  if (isFiltered) {
    const action = (
      <Button onClick={onClear}>清除條件</Button>
    );
    return (
      <EmptyState title="沒有符合條件的項目" action={action}>
        {isNoCurrentCycle ? <NoCurrentCycleHint /> : '換個搜尋文字或篩選條件，或清除條件看全部的項目。'}
      </EmptyState>
    );
  }
  const createHint = isBelowWide ? '按「新增項目」建立第一個項目。' : '在上方輸入標題，按「新增」建立第一個項目。';
  return (
    <EmptyState title="這個專案還沒有項目" action={<NewProjectNextSteps />}>
      {can.create ? createHint : '有人新增項目後會出現在這裡。'}
    </EmptyState>
  );
}

/**
 * 剛開的專案除了新增項目，常要做的是加人、看流程（docs/DESIGN.md 8.1 第 3 點：空白要給下一步）。照權限：
 * 能管理專案才有「加入成員」（和改狀態被擋下時同一個打開成員對話框的方式，ProjectTableContext 的 onOpenMembers），
 * 看得到設定才有「看看流程設定」。對話框關上後焦點回到「加入成員」（Dialog 自己做）。
 */
function NewProjectNextSteps() {
  const { detail, can, onOpenMembers } = useProjectTable();
  const canReadSettings = detail.myPermissions.includes('blueprint.read');
  if (!can.manage && !canReadSettings) return null;
  return (
    <>
      {can.manage && <Button onClick={onOpenMembers}>加入成員</Button>}
      {canReadSettings && (
        <Link to={settingsSectionPath(detail.project.key, 'workflows')} className={buttonClassName('secondary')}>
          看看流程設定
        </Link>
      )}
    </>
  );
}

/** 沒有表格可以放的結果（錯誤、空白）：快速新增單獨在最上面，說明在它下面。 */
function MessageBelow({ quickAdd, children }: { readonly quickAdd: ReactNode; readonly children: ReactNode }) {
  return (
    <>
      {quickAdd}
      <div className={styles.message}>{children}</div>
    </>
  );
}

/** 第一次就讀不到：說明與「重試」。 */
function LoadError({ result }: { readonly result: ItemsResult }) {
  const retry = () => {
    void result.refetch();
  };
  return <ErrorState error={result.error} title="項目載入失敗" onRetry={retry} />;
}

/**
 * 依條件讀項目。這三種情況項目的請求都不送：清單還沒載入（換成編號之前先送，會多打一次沒有結果的請求）、
 * 清單讀不到（結果說不準，不能當成沒有）、現在沒有進行中的週期（結果一定是空的，也不送假編號）。
 */
function useItemResults(filters: ItemFilters, staleNow: Date) {
  const { detail, todayDate } = useProjectTable();
  const planning = usePlanning(detail.project.key);
  const weekStart = useWeekStart();
  const isWaitingForCycles = filters.cycle === CURRENT_CYCLE && planning.isCyclesPending;
  const isBlocked = isPlanningFilterBlocked(planning, filters);
  const hasNoCurrent = !isWaitingForCycles && !isBlocked && hasNoCurrentCycle(filters.cycle, planning.cycles, todayDate);
  const query = toItemQuery(filters, ITEMS_PAGE_SIZE, todayDate, weekStart, planning.cycles, staleNow);
  const result = useProjectItems(detail.project.key, query, !isWaitingForCycles && !isBlocked && !hasNoCurrent);
  return { planning, isBlocked, hasNoCurrent, result };
}

/** 點表頭的排序：換成下一個狀態；回到預設（編號遞增）就從條件拿掉。 */
function sortedBy(filters: ItemFilters, field: ItemSortField): ItemFilters {
  const next = nextSort(filters.sort ?? DEFAULT_SORT, field);
  const isDefault = next === null || (next.field === DEFAULT_SORT.field && next.direction === DEFAULT_SORT.direction);
  return { ...filters, sort: isDefault ? null : next };
}

interface ResultsTableProps {
  readonly table: ItemTableState;
  readonly items: readonly Item[];
  readonly footer: ReactNode;
  readonly gridRef: Ref<RovingGridHandle>;
  readonly quickAdd: ReactNode;
  readonly isRefreshing: boolean;
}

/** 寬螢幕的表格：排序換了就改條件（回到預設的排序時拿掉）。 */
function ResultsTable({ table, items, footer, gridRef, quickAdd, isRefreshing }: ResultsTableProps) {
  const { filters, setFilters, columns, visibility, selection } = table;
  return (
    <SelectionProvider value={selection}>
      <ItemTable
        items={items}
        columns={columns}
        visibility={visibility}
        sort={filters.sort ?? DEFAULT_SORT}
        onSort={(field) => { setFilters(sortedBy(filters, field)); }}
        isRefreshing={isRefreshing}
        footer={footer}
        gridRef={gridRef}
        widthStore={table.widths}
        quickAdd={quickAdd}
      />
    </SelectionProvider>
  );
}

/**
 * 依條件讀項目，處理載入中（骨架）、錯誤、兩種空白，然後交給表格（窄螢幕是清單）。
 * 快速新增（寬螢幕才有，不然是 null）有表格時併進表格表頭下面的第一列；沒有表格（載入中、錯誤、空白）時
 * 照一樣的長相單獨放在最上面，打到一半的內容在 useItemTableState 裡，換位置不會掉。
 */
export function ItemResults({ table, quickAddHost }: { readonly table: ItemTableState; readonly quickAddHost: HTMLDivElement | null }) {
  const quickAdd = quickAddHost === null ? null : <QuickAddSlot host={quickAddHost} />;
  const { filters, clearFilters, selection } = table;
  const isCompact = useIsCompact();
  const { planning, isBlocked, hasNoCurrent, result } = useItemResults(filters, table.staleNow);
  const items = useMemo(() => flattenItemPages(result.data), [result.data]);
  const gridRef = useRef<RovingGridHandle>(null);
  useSyncSelection(selection, items, isCompact);

  if (isBlocked || hasNoCurrent) {
    return (
      <MessageBelow quickAdd={quickAdd}>
        {isBlocked ? <PlanningLoadError planning={planning} /> : <EmptyResults isFiltered isNoCurrentCycle onClear={clearFilters} />}
      </MessageBelow>
    );
  }
  if (result.isPending) {
    return <ResultsSkeleton isCompact={isCompact} quickAdd={quickAdd} />;
  }
  if (result.data === undefined) {
    return (
      <MessageBelow quickAdd={quickAdd}>
        <LoadError result={result} />
      </MessageBelow>
    );
  }
  if (items.length === 0) {
    return (
      <MessageBelow quickAdd={quickAdd}>
        <EmptyResults isFiltered={hasActiveFilters(filters)} onClear={clearFilters} />
      </MessageBelow>
    );
  }
  const onButtonGone = () => {
    focusLastPage(result, items, gridRef);
  };
  const footer = <LoadMore result={result} count={items.length} onButtonGone={onButtonGone} />;
  if (isCompact) {
    return <ProjectItemList items={items} isRefreshing={result.isPlaceholderData} footer={footer} />;
  }
  return <ResultsTable table={table} items={items} footer={footer} gridRef={gridRef} quickAdd={quickAdd} isRefreshing={result.isPlaceholderData} />;
}
