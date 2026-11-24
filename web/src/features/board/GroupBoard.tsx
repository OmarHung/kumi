import { DndContext, DragOverlay, type CollisionDetection } from '@dnd-kit/core';
import { useCallback, useMemo, useState } from 'react';
import { errorMessage } from '../../api/http';
import { useBoardColumnItems } from '../../api/queries';
import { queryKeys } from '../../api/queryKeys';
import type { Item } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import type { BoardGroup } from '../../domain/boardGroup';
import {
  boardGroupQuery,
  columnKeyOf,
  groupColumns,
  groupName,
  groupPatch,
  movedItem,
  type GroupColumn as GroupColumnModel,
} from '../../domain/boardGroupColumns';
import { typeHasField } from '../../domain/fields';
import { CURRENT_CYCLE } from '../../domain/planningFilter';
import type { BoardFilters } from '../../domain/projectView';
import { cx } from '../../lib/cx';
import { useIsCompact } from '../../lib/useMediaQuery';
import type { FocusTarget } from '../../lib/updateThenFocus';
import { useWeekStart } from '../../lib/weekStart';
import { flattenItemPages } from '../item-table/itemCache';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { usePlanning } from '../item-table/usePlanning';
import { CardPreview, CardSkeleton } from './BoardCard';
import { BoardProvider, type BoardContextValue } from './BoardContext';
import { boardAutoScroll, boardDropAnimation, pointerWithinVisibleLanes, useBoardSensors } from './boardDnd';
import type { ColumnSeed } from './BoardQuickAdd';
import { GroupColumn } from './GroupColumn';
import { groupAnnouncements, groupDropTarget, groupInstructions } from './groupDnd';
import { MoveProblems } from './MoveProblems';
import { movesOfGroup, type GroupMove, type GroupMoves } from './useGroupMoves';
import { useBoardDrag } from './useBoardDrag';
import styles from './Board.module.css';

type GroupMode = Exclude<BoardGroup, { readonly kind: 'state' }>;

interface GroupBoardProps {
  readonly group: GroupMode;
  readonly filters: BoardFilters;
  /** 修改項目的狀態放在 BoardView：換分組時這個元件會重建。 */
  readonly moves: GroupMoves;
  readonly focusFallback: readonly FocusTarget[];
}

/** 送出中的卡片先畫在新的那一欄（伺服器的結果回來之前），其餘照伺服器的結果。 */
function withPendingMoves(items: readonly Item[], pending: readonly GroupMove[], group: GroupMode, members: Parameters<typeof movedItem>[3]): readonly Item[] {
  if (pending.length === 0) return items;
  const byKey = new Map(pending.map((move) => [move.item.key, move.to]));
  return items.map((item) => {
    const to = byKey.get(item.key);
    return to === undefined ? item : movedItem(item, group, to, members);
  });
}

/** 這一欄新增項目時帶的值與能選的類型；放不進來的欄（「沒有…」、不能當落點）沒有新增。 */
function useColumnSeed(group: GroupMode): (column: GroupColumnModel) => { readonly seed: ColumnSeed | null; readonly types: BoardContextValue['types'] } {
  const { detail, can } = useProjectTable();
  return (column) => {
    const none = { seed: null, types: [] };
    if (!can.create || column.isEmpty || !column.isTarget) return none;
    if (group.kind === 'assignee') return { seed: { assigneeId: column.key }, types: detail.blueprint.itemTypes };
    const types = detail.blueprint.itemTypes.filter((itemType) => typeHasField(detail.blueprint, itemType.key, group.key));
    return types.length === 0 ? none : { seed: { fields: { [group.key]: column.key } }, types };
  };
}

/** 給螢幕閱讀器（與看得到的一行小字）：正在送出、或剛移好的是哪一張，移到哪一欄。 */
function moveStatus(moves: GroupMoves, group: GroupMode, nameOf: (columnKey: string) => string): string {
  const latest = moves.pending.at(-1);
  if (latest !== undefined) {
    return `正在把 ${latest.item.key} 移到「${nameOf(latest.to)}」…`;
  }
  const { landed } = moves;
  return landed === null ? '' : `已把 ${landed.key} 移到「${nameOf(columnKeyOf(landed, group))}」。`;
}

function LoadMore({ result }: { readonly result: ReturnType<typeof useBoardColumnItems> }) {
  return (
    <div className={styles.groupFooter}>
      {result.hasNextPage && (
        <Button
          size="sm"
          variant="ghost"
          // 重抓中（例如剛移走一張）時，手上的游標可能已經對不上，等重抓完再往下讀。
          disabled={result.isFetching}
          onClick={() => {
            void result.fetchNextPage();
          }}
        >
          {result.isFetchingNextPage ? '載入中…' : '載入更多'}
        </Button>
      )}
      {result.isFetchNextPageError && <Notice tone="danger">{`載入更多失敗：${errorMessage(result.error)}`}</Notice>}
    </div>
  );
}

/** 一次讀進來的還沒結束的項目、送出中的移動先畫在新的欄，以及分好的欄。 */
function useGroupItems(group: GroupMode, filters: BoardFilters, moves: GroupMoves) {
  const { detail, todayDate } = useProjectTable();
  const { cycles, isCyclesPending } = usePlanning(detail.project.key);
  const weekStart = useWeekStart();
  const query = boardGroupQuery(filters, todayDate, weekStart, cycles);
  // 篩「目前的週期」要等週期清單：換成編號之前先送，會多打一次沒有結果的請求。
  const result = useBoardColumnItems(detail.project.key, query, !(filters.cycle === CURRENT_CYCLE && isCyclesPending));
  const base = useMemo(() => flattenItemPages(result.data), [result.data]);
  const { pending } = moves;
  const items = useMemo(() => withPendingMoves(base, pending, group, detail.members), [base, pending, group, detail.members]);
  const columns = useMemo(() => groupColumns(group, detail.blueprint, detail.members, items), [group, detail.blueprint, detail.members, items]);
  return { result, items, columns, listKey: queryKeys.boardColumn(detail.project.key, query) };
}

interface GroupLanesProps {
  readonly group: GroupMode;
  readonly items: readonly Item[];
  readonly columns: readonly GroupColumnModel[];
  readonly hasMore: boolean;
  readonly isBusy: boolean;
  readonly listKey: readonly unknown[];
  readonly activeItem: Item | null;
  readonly dragHandlers: Omit<ReturnType<typeof useBoardDrag>, 'activeItem'>;
}

/** 拖曳的範圍與欄：DndContext 和依狀態的看板（WorkflowBoard）同一套感應器與自動捲動。 */
function GroupLanes({ group, items, columns, hasMore, isBusy, listKey, activeItem, dragHandlers }: GroupLanesProps) {
  const { detail } = useProjectTable();
  const sensors = useBoardSensors();
  const [lanes, setLanes] = useState<HTMLDivElement | null>(null);
  const collisionDetection = useCallback<CollisionDetection>((args) => pointerWithinVisibleLanes(lanes, args), [lanes]);
  const [dropAnimation] = useState(boardDropAnimation);
  const autoScroll = boardAutoScroll(useIsCompact(), lanes);
  const name = groupName(group, detail.blueprint);
  const accessibility = useMemo(
    () => ({
      announcements: groupAnnouncements(name, (active, over) => groupDropTarget((item) => columnKeyOf(item, group), active, over) !== null),
      screenReaderInstructions: groupInstructions(name),
    }),
    [name, group],
  );
  const seedOf = useColumnSeed(group);
  return (
    <DndContext sensors={sensors} collisionDetection={collisionDetection} accessibility={accessibility} autoScroll={autoScroll} {...dragHandlers}>
      <div ref={setLanes} className={cx(styles.lanes, activeItem !== null && styles.lanesDragging)} aria-busy={isBusy || undefined}>
        {columns.map((column) => {
          const { seed, types } = seedOf(column);
          return (
            <GroupColumn
              key={column.key}
              column={column}
              items={items.filter((item) => columnKeyOf(item, group) === column.key)}
              hasMore={hasMore}
              seed={seed}
              types={types}
              listKey={listKey}
            />
          );
        })}
      </div>
      <DragOverlay dropAnimation={dropAnimation}>{activeItem !== null && <CardPreview item={activeItem} />}</DragOverlay>
    </DndContext>
  );
}

function LoadingLanes() {
  return (
    <div className={styles.lanes}>
      <section className={styles.column} aria-label="載入中">
        <div className={styles.laneScroll}>
          <div className={styles.laneBody}>
            <CardSkeleton />
          </div>
        </div>
      </section>
    </div>
  );
}

interface GroupContextInput {
  readonly group: GroupMode;
  readonly filters: BoardFilters;
  readonly types: BoardContextValue['types'];
  readonly moves: GroupMoves;
  readonly move: BoardContextValue['move'];
  readonly activeItem: Item | null;
}

/** 卡片與欄共用的看板資訊（BoardContext）：分組時不分流程、類型是全部。 */
function useGroupContext({ group, filters, types, moves, move, activeItem }: GroupContextInput): BoardContextValue {
  const failedKeys = useMemo(() => new Set(moves.problems.map((problem) => problem.item.key)), [moves.problems]);
  return {
    workflow: null,
    group,
    types,
    filters,
    pending: moves.pending,
    failedKeys,
    landedKey: moves.landed?.key ?? null,
    activeItem,
    focusAfterMoveRef: moves.focusAfterMoveRef,
    move,
  };
}

/**
 * 換分組時 GroupBoard 重建、送出中的移動與說明還在（放在 BoardView）：只留這個分組自己的，
 * 另一種分組的 to（例如人員編號）在這裡不是欄的 key。
 */
function useScopedMoves(moves: GroupMoves, group: GroupMode): GroupMoves {
  const { pending, problems } = moves;
  const scopedPending = useMemo(() => movesOfGroup(pending, group), [pending, group]);
  const scopedProblems = useMemo(() => movesOfGroup(problems, group), [problems, group]);
  return useMemo(() => ({ ...moves, pending: scopedPending, problems: scopedProblems }), [moves, scopedPending, scopedProblems]);
}

/** 拖曳與「移到…」共用的移動：落在原本的欄不算移動。 */
function useGroupDrag(group: GroupMode, moves: GroupMoves) {
  const move = useCallback<BoardContextValue['move']>(
    (item, to, keepFocus) => {
      if (columnKeyOf(item, group) !== to) moves.move(item, to, groupPatch(group, to), group, keepFocus);
    },
    [group, moves],
  );
  const { activeItem, ...dragHandlers } = useBoardDrag(move, (active, over) => groupDropTarget((item) => columnKeyOf(item, group), active, over));
  return { move, activeItem, dragHandlers };
}

/** 欄的名稱：移動說明與失敗說明用（找不到的欄用 key）。 */
function useColumnLabels(columns: readonly GroupColumnModel[], group: GroupMode) {
  const nameOf = (key: string) => columns.find((column) => column.key === key)?.name ?? key;
  const labelsOf = ({ item, to }: { readonly item: Item; readonly to: string }) => ({ from: nameOf(columnKeyOf(item, group)), target: nameOf(to) });
  return { nameOf, labelsOf };
}

/**
 * 依負責人、依單選欄位分欄的看板：不分流程，一次讀一份還沒結束的項目（`boardGroupQuery`），在前端分欄；
 * 拖曳與「移到…」是修改項目（useGroupMoves）。欄、移動與快速新增的規則在 docs/DESIGN.md 7.2「分組」。
 * 每張卡片自己帶狀態（BoardCard），因為欄不是狀態了。
 */
export function GroupBoard({ group, filters, moves: allMoves, focusFallback }: GroupBoardProps) {
  const moves = useScopedMoves(allMoves, group);
  const { result, items, columns, listKey } = useGroupItems(group, filters, moves);
  const { move, activeItem, dragHandlers } = useGroupDrag(group, moves);
  const { detail } = useProjectTable();
  const value = useGroupContext({ group, filters, types: detail.blueprint.itemTypes, moves, move, activeItem });
  const { nameOf, labelsOf } = useColumnLabels(columns, group);
  return (
    <BoardProvider value={value}>
      <MoveProblems problems={moves.problems} onDismiss={moves.dismiss} focusFallback={focusFallback} labelsOf={labelsOf} />
      <p role="status" className={styles.moveStatus}>
        {moveStatus(moves, group, nameOf)}
      </p>
      {result.isPending ? (
        <LoadingLanes />
      ) : result.data === undefined ? (
        <ErrorState error={result.error} title="看板載入失敗" onRetry={() => { void result.refetch(); }} />
      ) : (
        <>
          <GroupLanes
            group={group}
            items={items}
            columns={columns}
            hasMore={result.hasNextPage}
            isBusy={result.isPlaceholderData}
            listKey={listKey}
            activeItem={activeItem}
            dragHandlers={dragHandlers}
          />
          <LoadMore result={result} />
        </>
      )}
    </BoardProvider>
  );
}
