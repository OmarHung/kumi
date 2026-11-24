import { DndContext, DragOverlay, type CollisionDetection } from '@dnd-kit/core';
import { useCallback, useMemo, useState } from 'react';
import type { StateCategory } from '../../../api/blueprint';
import { useProjectDetails } from '../../../api/queries';
import type { CalendarDate, Item, ItemQuery, ProjectDetail } from '../../../api/types';
import { useCurrentUser } from '../../../auth/CurrentUser';
import { ErrorState, RefetchErrorNotice } from '../../../components/StatusViews/StatusViews';
import { findState, workflowForType } from '../../../domain/blueprint';
import { STATE_CATEGORY_LABELS } from '../../../domain/labels';
import { hasActiveMyWorkFilters, myWorkConditions, STATE_CATEGORIES, type MyWorkFilters } from '../../../domain/myWorkView';
import { cx } from '../../../lib/cx';
import type { FocusTarget } from '../../../lib/updateThenFocus';
import { useIsCompact } from '../../../lib/useMediaQuery';
import { useWeekStart } from '../../../lib/weekStart';
import { BOARD_INSTRUCTIONS, boardAutoScroll, boardDropAnimation, pointerWithinVisibleLanes, useBoardSensors } from '../../board/boardDnd';
import { boardCategories, columnItems, planDrop } from './boardColumns';
import { MyWorkBoardProvider, type MyWorkBoardContextValue, type StatePicker } from './MyWorkBoardContext';
import { MyWorkCardPreview } from './MyWorkCard';
import { MyWorkColumn } from './MyWorkColumn';
import { MyWorkMoveFill } from './MyWorkMoveFill';
import { MyWorkMoveProblems } from './MyWorkMoveProblems';
import { MY_WORK_BOARD_ANNOUNCEMENTS, type CategoryDrop } from './myWorkBoardDnd';
import { useBoardDrag } from './useBoardDrag';
import { useMyWorkBoardItems } from './useMyWorkBoardItems';
import { firstOfLastPage, LoadMore } from '../../item-table/LoadMore';
import { useMyWorkBoardMoves, type MyWorkBoardMoves } from './useMyWorkBoardMoves';
import boardStyles from '../../board/Board.module.css';

const ACCESSIBILITY = { announcements: MY_WORK_BOARD_ANNOUNCEMENTS, screenReaderInstructions: BOARD_INSTRUCTIONS };

interface MyWorkBoardProps {
  readonly filters: MyWorkFilters;
  /** 使用者所在地的今天：到期日的篩選與過期的標示用同一個值。 */
  readonly todayDate: CalendarDate;
  /** 關上說明、卡片都不在畫面上時焦點的去處，照順序（頁面的搜尋框、「篩選」按鈕、標題）。 */
  readonly focusFallback?: readonly FocusTarget[];
}

const NO_FALLBACK: readonly FocusTarget[] = [];

/** 某一個狀態（選了專案）時，欄是那個狀態的類別：從專案的藍圖找；還沒讀到就是空的，欄也還沒有。 */
function categoriesOfState(filters: MyWorkFilters, projects: ReadonlyMap<string, ProjectDetail>): readonly StateCategory[] {
  if (filters.status.kind !== 'state' || filters.project === null) return [];
  const { state } = filters.status;
  const states = projects.get(filters.project)?.blueprint.workflows.flatMap((workflow) => workflow.states) ?? [];
  return STATE_CATEGORIES.filter((category) => states.some((candidate) => candidate.key === state && candidate.category === category));
}

/** 選了專案、讀到它的內容時：它的流程裡有狀態的類別。沒選或還沒讀到是 undefined（照規則畫，內容到了再拿掉空欄）。 */
function categoriesOfProject(filters: MyWorkFilters, projects: ReadonlyMap<string, ProjectDetail>): readonly StateCategory[] | undefined {
  const detail = filters.project === null ? undefined : projects.get(filters.project);
  return detail?.blueprint.workflows.flatMap((workflow) => workflow.states.map((state) => state.category));
}

/** 給螢幕閱讀器（與看得到的一行小字）：正在送出、或剛移好的是哪一張。狀態的名稱是那張卡片自己專案的。 */
function moveStatusText(moves: MyWorkBoardMoves, projects: ReadonlyMap<string, ProjectDetail>): string {
  const name = (item: Item, state: string) => {
    const detail = projects.get(item.projectKey);
    return (detail === undefined ? undefined : findState(detail.blueprint, item.type, state)?.name) ?? STATE_CATEGORY_LABELS[item.stateCategory];
  };
  const latest = moves.pending.at(-1);
  if (latest !== undefined) return `正在把 ${latest.item.key} 移到「${name(latest.item, latest.to)}」…`;
  return moves.landed === null ? '' : `已把 ${moves.landed.key} 移到「${name(moves.landed, moves.landed.state)}」。`;
}

function emptyTextOf(filters: MyWorkFilters, category: StateCategory): string {
  if (filters.status.kind === 'open' && category === 'done') return '把卡片放到這裡，就是完成了。';
  return hasActiveMyWorkFilters(filters) ? '沒有符合條件的項目' : '這一欄沒有項目';
}

/** 拖到類別、有好幾個走得到的狀態時的選單，與其他移動共用的「開始移動就收起選單」。 */
function usePicker() {
  const [picker, setPicker] = useState<StatePicker | null>(null);
  return {
    picker,
    open: (next: Omit<StatePicker, 'id'>) => {
      setPicker((current) => ({ ...next, id: (current?.id ?? 0) + 1 }));
    },
    close: () => {
      setPicker(null);
    },
  };
}

interface DropHandlerArgs {
  readonly projects: ReadonlyMap<string, ProjectDetail>;
  readonly moves: MyWorkBoardMoves;
  readonly openPicker: (picker: Omit<StatePicker, 'id'>) => void;
}

/** 放下之後：依那張卡片自己專案的流程決定直接移、問要哪一個、照樣送出讓後端說明，或說流程沒有那個類別。 */
function useDropHandler({ projects, moves, openPicker }: DropHandlerArgs) {
  return ({ item, category }: CategoryDrop) => {
    const detail = projects.get(item.projectKey);
    if (detail === undefined) return;
    const plan = planDrop(workflowForType(detail.blueprint, item.type), item, category, detail.project.myRole);
    switch (plan.kind) {
      case 'move':
      case 'blocked':
        moves.move(item, plan.to, false);
        break;
      case 'choose':
        openPicker({ item, category, targets: plan.targets });
        break;
      case 'empty': {
        const label = STATE_CATEGORY_LABELS[category];
        moves.explain(item, label, `「${detail.project.name}」的流程沒有「${label}」的狀態。`);
        break;
      }
      case 'ignore':
        break;
    }
  };
}

/** 載完最後一頁、「載入更多」跟著不見時焦點要去的地方：最後一頁的第一張卡片的標題連結（和表格放到新載入的第一列同一個意思）。 */
function focusFirstOfLastPage(result: ReturnType<typeof useMyWorkBoardItems>['result'], items: readonly Item[]): void {
  const key = firstOfLastPage(result.data, items);
  if (key === undefined) return;
  document.querySelector<HTMLElement>(`[data-card="${CSS.escape(key)}"] a`)?.focus();
}

function BoardFooter({ result, items }: { readonly result: ReturnType<typeof useMyWorkBoardItems>['result']; readonly items: readonly Item[] }) {
  return (
    <LoadMore
      result={result}
      count={items.length}
      onButtonGone={() => {
        focusFirstOfLastPage(result, items);
      }}
    />
  );
}

/** 拖曳的設定：感應器、放下的判斷、自動捲動與放下的動畫。 */
function useDndSetup() {
  const sensors = useBoardSensors();
  // 看板（欄的橫向捲動容器）：放下的判斷與自動捲動都要用，放在 state（掛上之後才有），不必在渲染時讀 ref。
  const [lanes, setLanes] = useState<HTMLDivElement | null>(null);
  const collisionDetection = useCallback<CollisionDetection>((args) => pointerWithinVisibleLanes(lanes, args), [lanes]);
  const [dropAnimation] = useState(boardDropAnimation);
  const autoScroll = boardAutoScroll(useIsCompact(), lanes);
  return { sensors, setLanes, collisionDetection, dropAnimation, autoScroll };
}

interface LanesProps {
  readonly filters: MyWorkFilters;
  readonly items: readonly Item[];
  readonly projects: ReadonlyMap<string, ProjectDetail>;
  readonly moves: MyWorkBoardMoves;
  readonly drag: ReturnType<typeof useBoardDrag>;
  readonly data: ReturnType<typeof useMyWorkBoardItems>;
}

/** 拖曳的範圍與每一欄。 */
function Lanes({ filters, items, projects, moves, drag, data }: LanesProps) {
  const { sensors, setLanes, collisionDetection, dropAnimation, autoScroll } = useDndSetup();
  const categoryOfMove = ({ item, to }: { readonly item: Item; readonly to: string }) => {
    const detail = projects.get(item.projectKey);
    return detail === undefined ? undefined : findState(detail.blueprint, item.type, to)?.category;
  };
  // 卡片所在的類別（含正在移過去的）一定要有那一欄，不然移到這一頁沒有那一欄的類別（例如已取消）時卡片就不見了。
  const occupied = [...items.map((item) => item.stateCategory), ...moves.pending.flatMap((move) => categoryOfMove(move) ?? [])];
  const categories = boardCategories(filters.status, categoriesOfState(filters, projects), {
    projectCategories: categoriesOfProject(filters, projects),
    occupied,
  });
  return (
    <DndContext
      sensors={sensors}
      collisionDetection={collisionDetection}
      accessibility={ACCESSIBILITY}
      autoScroll={autoScroll}
      onDragStart={drag.onDragStart}
      onDragEnd={drag.onDragEnd}
      onDragCancel={drag.onDragCancel}
    >
      <div ref={setLanes} className={cx(boardStyles.lanes, drag.activeItem !== null && boardStyles.lanesDragging)}>
        {categories.map((category) => (
          <MyWorkColumn
            key={category}
            category={category}
            items={columnItems(items, category, moves.pending, categoryOfMove)}
            isLoading={data.isLoading}
            hasMore={data.result.hasNextPage}
            emptyText={emptyTextOf(filters, category)}
          />
        ))}
      </div>
      <DragOverlay dropAnimation={dropAnimation}>{drag.activeItem !== null && <MyWorkCardPreview item={drag.activeItem} />}</DragOverlay>
    </DndContext>
  );
}

interface BoardDataProps extends MyWorkBoardProps {
  readonly conditions: ItemQuery | null;
}

function MyWorkBoardData({ filters, todayDate, conditions, focusFallback = NO_FALLBACK }: BoardDataProps) {
  const { personId: me } = useCurrentUser();
  const data = useMyWorkBoardItems(conditions);
  const { result, items } = data;
  // 狀態的名稱、權限與流程在各專案的藍圖裡；拿到之前卡片先寫狀態類別、不能移動，不擋住整個看板。
  const projectKeys = [...new Set([...items.map((item) => item.projectKey), ...(filters.project === null ? [] : [filters.project])])];
  const projects = useProjectDetails(projectKeys);
  const moves = useMyWorkBoardMoves(projects);
  const { picker, open: openPicker, close: closePicker } = usePicker();
  const drag = useBoardDrag(useDropHandler({ projects, moves, openPicker }), closePicker);
  const failedKeys = useMemo(() => new Set(moves.problems.map((problem) => problem.item.key)), [moves.problems]);
  const value: MyWorkBoardContextValue = {
    projects,
    todayDate,
    me,
    pending: moves.pending,
    failedKeys,
    landedKey: moves.landed?.key ?? null,
    activeItem: drag.activeItem,
    picker,
    focusAfterMoveRef: moves.focusAfterMoveRef,
    move: (item, to, keepFocus) => {
      closePicker();
      moves.move(item, to, keepFocus);
    },
  };
  if (result.isError && result.data === undefined) {
    return <ErrorState error={result.error} title="「我的工作」載入失敗" onRetry={() => { void result.refetch(); }} />;
  }
  const fillDetail = moves.fill === null ? undefined : projects.get(moves.fill.item.projectKey);
  return (
    <MyWorkBoardProvider value={value}>
      <div className={boardStyles.view}>
        <MyWorkMoveProblems problems={moves.problems} projects={projects} onDismiss={moves.dismiss} focusFallback={focusFallback} />
        {/* 一次一個，換下一個時重新掛上（對話框的內容從新的那一張開始）。 */}
        {moves.fill !== null && fillDetail !== undefined && (
          <MyWorkMoveFill key={moves.fill.item.key} request={moves.fill} detail={fillDetail} moves={moves} focusFallback={focusFallback} />
        )}
        <p role="status" className={boardStyles.moveStatus}>
          {moveStatusText(moves, projects)}
        </p>
        <RefetchErrorNotice query={result} message="無法更新我的工作，看板上可能是舊的。" />
        <Lanes filters={filters} items={items} projects={projects} moves={moves} drag={drag} data={data} />
        <BoardFooter result={result} items={items} />
      </div>
    </MyWorkBoardProvider>
  );
}

/**
 * 「我的工作」的看板（internal/HANDOFF-my-work-views.md 5.3）：依狀態類別分欄（各專案的流程不同，沒有共同的狀態欄），
 * 卡片跨專案。拖到另一欄：那個類別在那張卡片的流程裡只有一個走得到的狀態就直接移，有好幾個就在卡片旁問要哪一個；
 * 鍵盤用卡片的「移到…」。條件換了（送給後端的條件不同）整個重新開始：這次看的期間記下的卡片是在原本的條件下記的。
 */
export function MyWorkBoard({ filters, todayDate, focusFallback }: MyWorkBoardProps) {
  const weekStart = useWeekStart();
  const conditions = myWorkConditions(filters, todayDate, weekStart);
  return (
    <MyWorkBoardData
      key={JSON.stringify(conditions)}
      filters={filters}
      todayDate={todayDate}
      conditions={conditions}
      focusFallback={focusFallback}
    />
  );
}
