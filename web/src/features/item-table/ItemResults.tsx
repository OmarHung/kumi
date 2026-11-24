import { useMemo, useRef } from 'react';
import { Link } from 'react-router';
import { useProjectItems } from '../../api/queries';
import type { ItemSortField } from '../../api/types';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState } from '../../components/StatusViews/StatusViews';
import { CURRENT_CYCLE, hasNoCurrentCycle } from '../../domain/planningFilter';
import { hasActiveFilters, nextSort, toItemQuery, type ItemFilters } from '../../domain/itemFilters';
import { useIsCompact } from '../../lib/useMediaQuery';
import type { RovingGridHandle } from '../../lib/useRovingGrid';
import { SelectionProvider } from '../bulk-edit/SelectionContext';
import { useSyncSelection } from '../bulk-edit/useSyncSelection';
import { settingsSectionPath } from '../work-items/itemLinks';
import { DEFAULT_SORT, ItemTable } from './ItemTable';
import { flattenItemPages } from './itemCache';
import { focusLastPage, LoadMore, type ItemsResult } from './LoadMore';
import { NoCurrentCycleHint, PlanningLoadError } from './PlanningStates';
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
function ResultsSkeleton({ isCompact }: { readonly isCompact: boolean }) {
  return (
    <div className={styles.skeleton}>
      {!isCompact && <div className={styles.skeletonHead} aria-hidden="true" />}
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
  const createHint = isCompact ? '按「新增項目」建立第一個項目。' : '在上方輸入標題，按「新增」建立第一個項目。';
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

/** 第一次就讀不到：說明與「重試」。 */
function LoadError({ result }: { readonly result: ItemsResult }) {
  const retry = () => {
    void result.refetch();
  };
  return (
    <div className={styles.message}>
      <ErrorState error={result.error} title="項目載入失敗" onRetry={retry} />
    </div>
  );
}

/**
 * 依條件讀項目。這三種情況項目的請求都不送：清單還沒載入（換成編號之前先送，會多打一次沒有結果的請求）、
 * 清單讀不到（結果說不準，不能當成沒有）、現在沒有進行中的週期（結果一定是空的，也不送假編號）。
 */
function useItemResults(filters: ItemFilters) {
  const { detail, todayDate } = useProjectTable();
  const planning = usePlanning(detail.project.key);
  const weekStart = useWeekStart();
  const isWaitingForCycles = filters.cycle === CURRENT_CYCLE && planning.isCyclesPending;
  const isBlocked = isPlanningFilterBlocked(planning, filters);
  const hasNoCurrent = !isWaitingForCycles && !isBlocked && hasNoCurrentCycle(filters.cycle, planning.cycles, todayDate);
  const query = toItemQuery(filters, ITEMS_PAGE_SIZE, todayDate, weekStart, planning.cycles);
  const result = useProjectItems(detail.project.key, query, !isWaitingForCycles && !isBlocked && !hasNoCurrent);
  return { planning, isBlocked, hasNoCurrent, result };
}

/** 點表頭的排序：換成下一個狀態；回到預設（編號遞增）就從條件拿掉。 */
function sortedBy(filters: ItemFilters, field: ItemSortField): ItemFilters {
  const next = nextSort(filters.sort ?? DEFAULT_SORT, field);
  const isDefault = next === null || (next.field === DEFAULT_SORT.field && next.direction === DEFAULT_SORT.direction);
  return { ...filters, sort: isDefault ? null : next };
}

/** 依條件讀項目，處理載入中（骨架）、錯誤、兩種空白，然後交給表格（窄螢幕是清單）。 */
export function ItemResults({ table }: { readonly table: ItemTableState }) {
  const { filters, setFilters, clearFilters, columns, visibility, selection } = table;
  const isCompact = useIsCompact();
  const { planning, isBlocked, hasNoCurrent, result } = useItemResults(filters);
  const items = useMemo(() => flattenItemPages(result.data), [result.data]);
  const gridRef = useRef<RovingGridHandle>(null);
  useSyncSelection(selection, items, isCompact);

  if (isBlocked || hasNoCurrent) {
    return (
      <div className={styles.message}>
        {isBlocked ? <PlanningLoadError planning={planning} /> : <EmptyResults isFiltered isNoCurrentCycle onClear={clearFilters} />}
      </div>
    );
  }
  if (result.isPending) {
    return <ResultsSkeleton isCompact={isCompact} />;
  }
  if (result.data === undefined) {
    return <LoadError result={result} />;
  }
  if (items.length === 0) {
    return (
      <div className={styles.message}>
        <EmptyResults isFiltered={hasActiveFilters(filters)} onClear={clearFilters} />
      </div>
    );
  }
  const onButtonGone = () => {
    focusLastPage(result, items, gridRef);
  };
  const footer = <LoadMore result={result} count={items.length} onButtonGone={onButtonGone} />;
  if (isCompact) {
    return <ProjectItemList items={items} isRefreshing={result.isPlaceholderData} footer={footer} />;
  }
  return (
    <SelectionProvider value={selection}>
      <ItemTable
        items={items}
        columns={columns}
        visibility={visibility}
        sort={filters.sort ?? DEFAULT_SORT}
        onSort={(field) => { setFilters(sortedBy(filters, field)); }}
        isRefreshing={result.isPlaceholderData}
        footer={footer}
        gridRef={gridRef}
        widthStore={table.widths}
      />
    </SelectionProvider>
  );
}
