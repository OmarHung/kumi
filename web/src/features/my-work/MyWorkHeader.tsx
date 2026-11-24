import { createContext, use, type ReactNode, type RefObject } from 'react';
import { ResizableHeader } from '../../components/Table/ColumnResizer';
import type { ColumnWidthStore } from '../../components/Table/columnWidths';
import { SortButton } from '../../components/Table/SortButton';
import { useColumnDrag } from '../../components/Table/useColumnDrag';
import { cx } from '../../lib/cx';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { dropColumn, resolveDropSlot, useColumnOrder, type ColumnOrderStore } from './myWorkColumnOrder';
import type { MyWorkColumn, MyWorkColumnId } from './myWorkColumns';
import { DEFAULT_MY_WORK_SORT, SORT_FIELDS, type MyWorkSort, type MyWorkSortField } from './myWorkSort';
import styles from './MyWork.module.css';

/*
 * 我的工作的表頭（docs/DESIGN.md 8.3「我的工作」）：每一欄的欄名是排序按鈕（點一下遞增、再點遞減、再點回到預設），
 * 按住往左右拖是換欄的順序（components/Table/useColumnDrag.ts），右緣是調欄寬的把手。所有組的表頭是同一組欄：
 * 換順序、排序、欄寬都是所有組一起變。
 */

/** 固定在左邊時每一欄的位置（MyWork.module.css 的 --my-work-<欄>-left）；只有編號、專案、標題放得到標題左邊。 */
const PIN_CLASSES: Readonly<Partial<Record<MyWorkColumnId, string>>> = {
  key: styles.pinKey,
  project: styles.pinProject,
  title: styles.pinTitle,
};

/** 固定的欄的 class：固定（sticky）加上它的位置。 */
export function pinClass(id: MyWorkColumnId, pinned: readonly MyWorkColumnId[]): string | undefined {
  return pinned.includes(id) ? cx(styles.pinned, PIN_CLASSES[id]) : undefined;
}

type ColumnDrag = ReturnType<typeof useColumnDrag<MyWorkColumnId>>;

const DragContext = createContext<ColumnDrag | null>(null);

interface DragScopeProps {
  readonly orderStore: ColumnOrderStore;
  /** 畫面上的欄（依專案分組時沒有專案）。 */
  readonly visible: readonly MyWorkColumnId[];
  readonly children: ReactNode;
}

/**
 * 拖表頭換順序：所有組的表頭共用一次拖曳（拖著的欄與放下去的位置每一組都畫）。自己跟著拖曳重畫，
 * 裡面的組是上一層畫好的元素，不跟著重畫——只有讀拖曳狀態的表頭會重畫。
 * 放開之後焦點放回原本的地方：換順序時表頭的格子在 DOM 裡搬家，焦點在搬家的那一格上會掉回頁面本體。
 */
export function MyWorkColumnDragScope({ orderStore, visible, children }: DragScopeProps) {
  const { order } = useColumnOrder(orderStore);
  const drag = useColumnDrag<MyWorkColumnId>({
    resolve: (id, slot) => resolveDropSlot(order, visible, id, slot),
    onDrop: (id, slot) => {
      const next = dropColumn(order, visible, id, slot);
      if (next === null) return;
      const active = document.activeElement;
      const focused = active instanceof HTMLElement && active.closest('thead') !== null ? active : null;
      updateThenFocus(() => {
        orderStore.set(next);
      }, focused === null ? [] : [{ current: focused }]);
    },
  });
  return <DragContext value={drag}>{children}</DragContext>;
}

interface HeaderCellProps {
  readonly column: MyWorkColumn;
  /** 第幾欄（畫面上的順序）與總共幾欄：放下去的位置畫在哪一格的哪一邊。 */
  readonly index: number;
  readonly count: number;
  readonly pinned: readonly MyWorkColumnId[];
  readonly sort: MyWorkSort | null;
  readonly onSort: (field: MyWorkSortField) => void;
  readonly widthStore: ColumnWidthStore;
  readonly scopeRef: RefObject<HTMLElement | null>;
}

function dropSideOf(slot: number | null | undefined, index: number, count: number): 'before' | 'after' | undefined {
  if (slot === index) return 'before';
  return slot === count && index === count - 1 ? 'after' : undefined;
}

function MyWorkHeaderCell({ column, index, count, pinned, sort, onSort, widthStore, scopeRef }: HeaderCellProps) {
  const drag = use(DragContext);
  const field = SORT_FIELDS[column.id];
  // 沒有排序時的順序就是到期日遞增：到期日的表頭照樣標出來（aria-sort），點了是遞減。
  const shown = sort ?? DEFAULT_MY_WORK_SORT;
  const direction = shown.field === field ? shown.direction : undefined;
  const dropSide = dropSideOf(drag?.state?.slot, index, count);
  return (
    <ResizableHeader
      label={column.label}
      sizing={{ id: column.id, store: widthStore, bounds: column.bounds, scopeRef }}
      ariaSort={direction === undefined ? undefined : direction === 'asc' ? 'ascending' : 'descending'}
      className={pinClass(column.id, pinned)}
    >
      <SortButton
        label={column.label}
        direction={direction}
        data-dragging={drag?.state?.id === column.id || undefined}
        onClick={() => {
          onSort(field);
        }}
        {...drag?.handlersFor(column.id)}
      />
      {/* 拖曳中放下去的位置：交界上一條線（功能的指示，不是裝飾，第 9 節）。 */}
      {dropSide !== undefined && <span className={styles.dropMarker} data-side={dropSide} aria-hidden="true" />}
    </ResizableHeader>
  );
}

type HeaderRowProps = Omit<HeaderCellProps, 'column' | 'index' | 'count'> & {
  readonly columns: readonly MyWorkColumn[];
};

/** 一組表格的表頭。 */
export function MyWorkHeaderRow({ columns, ...rest }: HeaderRowProps) {
  return (
    <tr>
      {columns.map((column, index) => (
        <MyWorkHeaderCell key={column.id} column={column} index={index} count={columns.length} {...rest} />
      ))}
    </tr>
  );
}
