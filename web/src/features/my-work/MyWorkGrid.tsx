import { useId, useImperativeHandle, useRef, type ReactNode, type Ref, type RefObject } from 'react';
import type { CalendarDate, Item, ProjectDetail } from '../../api/types';
import { useColumnWidths, type ColumnWidthStore } from '../../components/Table/columnWidths';
import { TableColumns, TableScroll, tableStyles, type TableColumn } from '../../components/Table/Table';
import { cx } from '../../lib/cx';
import { useRovingGrid, type RovingGridHandle } from '../../lib/useRovingGrid';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { tableHint } from '../item-table/tableHint';
import { pinnedColumnsOf, useColumnOrder, type ColumnOrderStore } from './myWorkColumnOrder';
import { myWorkColumns, myWorkVariables, type MyWorkColumn, type MyWorkColumnId } from './myWorkColumns';
import { MyWorkColumnDragScope, MyWorkHeaderRow } from './MyWorkHeader';
import { MyWorkGroupSection } from './MyWorkGroupSection';
import type { MyWorkGroup } from './myWorkGroups';
import { MyWorkRowView } from './MyWorkRow';
import type { MyWorkSort, MyWorkSortField } from './myWorkSort';
import type { MyWorkRow } from './useMyWorkRows';
import styles from './MyWork.module.css';

export interface MyWorkViewProps {
  readonly groups: readonly MyWorkGroup<MyWorkRow>[];
  readonly projects: ReadonlyMap<string, ProjectDetail>;
  readonly todayDate: CalendarDate;
  /** 依專案分組時組名就是專案，不再每一列重複。 */
  readonly showProject: boolean;
  /** 這個項目是不是剛改成完成、取消而不再符合條件（myWorkStatus.ts 的 IsLeaving）。 */
  readonly isLeaving: (item: Item) => boolean;
}

/** 欄的預設寬度（MyWork.module.css）。標題欄沒有：沒拉過時吃掉剩下的寬度。 */
const COLUMN_CLASSES: Readonly<Record<MyWorkColumnId, string | undefined>> = {
  key: styles.colKey,
  title: undefined,
  state: styles.colState,
  priority: styles.colPriority,
  due: styles.colDue,
  project: styles.colProject,
};

/** 畫面上的欄（照使用者的順序）與其中固定在左邊的（標題和它左邊的）。 */
interface ColumnLayout {
  readonly columns: readonly MyWorkColumn[];
  readonly pinned: readonly MyWorkColumnId[];
}

interface WidthProps {
  /** 使用者拉的欄寬（MyWorkPage，所有組共用一份）。 */
  readonly widthStore: ColumnWidthStore;
  /** 照內容調整時量所有組的格子。 */
  readonly scopeRef: RefObject<HTMLElement | null>;
}

/**
 * 一組表格的 colgroup：寬度都是捲動區塊上的變數（myWorkVariables）。標題欄拉過之後是那個寬度，最後多一個空的欄
 * 吃掉剩下的寬度（最後留白）；沒拉過時標題欄吃掉剩下的寬度（原本的樣子）。自己跟著欄寬重畫，表身不動。
 */
function MyWorkColumns({ columns, widthStore }: { readonly columns: readonly MyWorkColumn[]; readonly widthStore: ColumnWidthStore }) {
  const isTitleSized = useColumnWidths(widthStore).title !== undefined;
  const cols: TableColumn[] = columns.map(({ id }) => ({
    key: id,
    className: id === 'title' && isTitleSized ? styles.colTitle : COLUMN_CLASSES[id],
  }));
  return <TableColumns columns={cols} hasFiller={isTitleSized} />;
}

interface SortProps {
  /** 網址上的排序；null 是預設（到期日遞增）。 */
  readonly sort: MyWorkSort | null;
  /** 點表頭（MyWorkTable：換成下一個排序，寫進網址）。 */
  readonly onSort: (field: MyWorkSortField) => void;
}

interface GroupTableProps extends Omit<MyWorkViewProps, 'groups' | 'showProject'>, WidthProps, SortProps {
  readonly group: MyWorkGroup<MyWorkRow>;
  readonly layout: ColumnLayout;
  readonly firstKey: string | undefined;
  /** 操作說明（所有組共用一份）。 */
  readonly hintId: string;
}

/**
 * 一組一張表格，名稱是組名。欄寬固定（MyWork.module.css 的區域變數，使用者可以調），每一組的欄才對得齊。
 * 欄的順序是使用者調的（預設編號、專案、標題、狀態、優先度、到期日）；放不下時捲走的是標題右邊的欄，
 * 標題和它左邊的欄固定在左邊。
 */
function GroupTable({ group, layout, projects, todayDate, isLeaving, firstKey, hintId, widthStore, scopeRef, sort, onSort }: GroupTableProps) {
  const ids = layout.columns.map((column) => column.id);
  return (
    <table className={cx(tableStyles.table, styles.table)} aria-describedby={hintId}>
      <caption className="visually-hidden">{group.label}</caption>
      <MyWorkColumns columns={layout.columns} widthStore={widthStore} />
      <thead>
        <MyWorkHeaderRow
          columns={layout.columns}
          pinned={layout.pinned}
          sort={sort}
          onSort={onSort}
          widthStore={widthStore}
          scopeRef={scopeRef}
        />
      </thead>
      <tbody>
        {group.items.map(({ item }) => (
          <MyWorkRowView
            key={item.key}
            item={item}
            project={projects.get(item.projectKey)}
            todayDate={todayDate}
            columns={ids}
            pinned={layout.pinned}
            isSettled={isLeaving(item)}
            isFirst={item.key === firstKey}
          />
        ))}
      </tbody>
    </table>
  );
}

/** 有沒有任何一個專案能改狀態（狀態格是這一頁唯一能編輯的格子）：操作說明要不要提編輯。 */
function canTransitionAny(projects: ReadonlyMap<string, ProjectDetail>): boolean {
  return [...projects.values()].some((project) => project.myPermissions.includes('item.transition'));
}

interface MyWorkGridProps extends MyWorkViewProps, SortProps {
  readonly widthStore: ColumnWidthStore;
  /** 欄的順序（MyWorkPage，所有組與工具列的「欄的順序」共用一份）。 */
  readonly orderStore: ColumnOrderStore;
  /** 接在最後一組後面（「已載入 N 個項目」與「載入更多」），在同一個捲動區塊裡。 */
  readonly footer?: ReactNode;
  /** 表格外面要把焦點放進某一列時用（「載入更多」不見之後，到新載入的第一列）。 */
  readonly gridRef?: Ref<RovingGridHandle>;
}

/**
 * 寬螢幕：每一組一個標題與一張表格，放在清單頁上（整頁捲動、表頭黏在頁列下面）。
 * 所有的組放在同一個 TableScroll 裡：放不下時（項目面板開著）一起退回橫向捲動，只有一條捲軸、各組的欄照樣對齊；
 * 每一組各自捲的話，開著面板時會有好幾條捲軸、各捲各的。依專案分組沒有專案欄，最小寬度跟著少一欄（data-show-project）。
 * 所有組的表格合起來只有一個 Tab 停駐點（docs/DESIGN.md 8.3「清單的鍵盤操作」，lib/useRovingGrid.ts 掛在包住所有組的容器上）：
 * 方向鍵在格子之間移動、跨過組時走過下一組的表頭；捲動的區塊不再多一個停駐點。
 */
export function MyWorkGrid({ groups, widthStore, orderStore, sort, onSort, footer, gridRef, showProject, ...rest }: MyWorkGridProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const hintId = useId();
  // 項目面板的上一個／下一個照畫面的順序：一組接著一組。關上面板時在這裡面找那一列放焦點。
  useItemSequence(
    groups.flatMap((group) => group.items.map((row) => row.key)),
    containerRef,
  );
  const { gridProps, handle } = useRovingGrid(containerRef);
  useImperativeHandle(gridRef, () => handle, [handle]);
  const { order } = useColumnOrder(orderStore);
  const columns = myWorkColumns(order, showProject);
  const ids = columns.map((column) => column.id);
  const layout: ColumnLayout = { columns, pinned: pinnedColumnsOf(ids) };
  const firstKey = groups[0]?.items[0]?.key;
  return (
    <GroupsScroll widthStore={widthStore} pinned={layout.pinned}>
      <MyWorkColumnDragScope orderStore={orderStore} visible={ids}>
        {/* TableScroll 量的是第一個子元素有沒有比它寬：最小寬度設在這一層（MyWork.module.css 的 .gridGroups）。 */}
        <div ref={containerRef} className={cx(styles.groups, styles.gridGroups)} data-show-project={showProject} {...gridProps}>
          {groups.map((group) => (
            <MyWorkGroupSection key={group.key} group={group}>
              <GroupTable
                group={group}
                layout={layout}
                firstKey={firstKey}
                hintId={hintId}
                widthStore={widthStore}
                scopeRef={containerRef}
                sort={sort}
                onSort={onSort}
                {...rest}
              />
            </MyWorkGroupSection>
          ))}
        </div>
      </MyWorkColumnDragScope>
      {/* hidden：只給 aria-describedby 引用，報讀軟體逐字瀏覽時不會再唸一次。 */}
      <p id={hintId} hidden>
        {tableHint(canTransitionAny(rest.projects), { isResizable: true })}
      </p>
      {footer}
    </GroupsScroll>
  );
}

interface GroupsScrollProps {
  readonly widthStore: ColumnWidthStore;
  readonly pinned: readonly MyWorkColumnId[];
  readonly children: ReactNode;
}

/**
 * 包住所有組的捲動區塊：拉過的欄寬與固定欄的位置是它上面的變數（myWorkVariables），每一組一起變。自己跟著欄寬重畫，
 * 裡面的組是上一層畫好的元素，不跟著重畫。
 */
function GroupsScroll({ widthStore, pinned, children }: GroupsScrollProps) {
  const widths = useColumnWidths(widthStore);
  return (
    // 量表格寬度的容器（container-type）：拉寬的標題欄夾在它的寬度之內（myWorkVariables）。不放在捲動區塊自己身上：
    // 容器查詢的單位看的是祖先，捲動區塊自己的 scroll-padding-left 也要用到它。
    <div className={styles.gridFrame}>
      <TableScroll
        label="我的工作"
        layout="page"
        frame={false}
        isFocusable={false}
        className={styles.groupsScroll}
        variables={myWorkVariables(widths, pinned)}
      >
        {children}
      </TableScroll>
    </div>
  );
}
