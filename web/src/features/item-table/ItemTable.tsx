import { FlexRender, useTable, type Cell, type Header, type Row } from '@tanstack/react-table';
import { useId, useImperativeHandle, useRef, type ReactNode, type Ref } from 'react';
import type { Item, ItemSort, ItemSortField } from '../../api/types';
import { ResizableHeader } from '../../components/Table/ColumnResizer';
import { SortButton } from '../../components/Table/SortButton';
import { useColumnWidths, type ColumnWidthStore } from '../../components/Table/columnWidths';
import { TableColumns, TableScroll, tableStyles } from '../../components/Table/Table';
import { useMeasuredWidths } from '../../components/Table/useMeasuredWidths';
import { cx } from '../../lib/cx';
import { useRovingGrid, type RovingGridHandle } from '../../lib/useRovingGrid';
import { cancelOnEscape } from '../bulk-edit/cancelOnEscape';
import { SelectAllCheckbox } from '../bulk-edit/SelectCells';
import { useSelection } from '../bulk-edit/SelectionContext';
import { useItemSequence } from '../item-page/ItemPanelContext';
import type { ColumnVisibility } from './columnVisibility';
import { itemTableFeatures, type ItemColumn, type ItemColumnMeta, type ItemTableFeatures } from './columns';
import { boundsOf, measuredColumnsOf, pinnedVariables, tableColumnOf } from './itemColumnWidths';
import { useProjectTable } from './ProjectTableContext';
import styles from './ItemTable.module.css';
import { tableHint } from './tableHint';

/** 沒有指定排序時，後端依編號遞增。 */
export const DEFAULT_SORT: ItemSort = { field: 'number', direction: 'asc' };

function pinClass(meta: ItemColumnMeta | undefined): string | undefined {
  if (meta?.pin === 'select') return styles.pinSelect;
  if (meta?.pin === 'key') return styles.pinKey;
  if (meta?.pin === 'title') return styles.pinTitle;
  return undefined;
}

interface ColumnHeaderProps {
  readonly header: Header<ItemTableFeatures, Item>;
  readonly sort: ItemSort;
  readonly onSort: (field: ItemSortField) => void;
  readonly widthStore: ColumnWidthStore;
}

/**
 * 欄標頭：能排序的是按鈕，右緣是調整欄寬的把手（components/Table/ColumnResizer.tsx，docs/DESIGN.md 7.1「欄寬」）。
 * 只有表頭跟著欄寬重畫，表身不動。
 */
function ColumnHeader({ header, sort, onSort, widthStore }: ColumnHeaderProps) {
  const meta = header.column.columnDef.meta;
  // 多選的勾選框欄：表頭是「選取全部」的勾選框，不能排序、不能調寬。
  if (meta?.pin === 'select') {
    return (
      <th scope="col" className={pinClass(meta)}>
        <SelectAllCheckbox />
      </th>
    );
  }
  const sortField = meta?.sortField;
  const isSorted = sortField !== undefined && sort.field === sortField;
  const ariaSort = isSorted ? (sort.direction === 'asc' ? 'ascending' : 'descending') : undefined;
  const label = meta?.label ?? header.column.id;
  return (
    <ResizableHeader
      label={label}
      sizing={{ id: header.column.id, store: widthStore, bounds: boundsOf(meta) }}
      ariaSort={ariaSort}
      className={cx(pinClass(meta), meta?.isNumeric && styles.numeric)}
    >
      {sortField === undefined ? undefined : (
        <SortButton
          label={label}
          direction={isSorted ? sort.direction : undefined}
          onClick={() => {
            onSort(sortField);
          }}
        />
      )}
    </ResizableHeader>
  );
}

interface ItemColumnsProps {
  readonly columns: readonly { readonly id: string; readonly meta: ItemColumnMeta | undefined }[];
  readonly widthStore: ColumnWidthStore;
}

/**
 * 欄寬（colgroup）：自己跟著欄寬重畫。沒拉過的欄（固定欄以外）第一次畫出來時照內容量一次（useMeasuredWidths）；
 * 最後一個空的欄吃掉表格比容器窄時剩下的寬度（最後留白）。
 */
function ItemColumns({ columns, widthStore }: ItemColumnsProps) {
  const widths = useColumnWidths(widthStore);
  const colgroupRef = useRef<HTMLTableColElement>(null);
  useMeasuredWidths(widthStore, colgroupRef, measuredColumnsOf(columns));
  return (
    <TableColumns
      ref={colgroupRef}
      columns={columns.map((column) => tableColumnOf(column.id, column.meta, widths))}
      hasFiller
    />
  );
}

/**
 * 捲動區塊：固定欄拉過的寬度是它上面的變數（欄寬、標題的省略號、scroll-padding-left 同一個）。
 * 自己跟著欄寬重畫；裡面的表格是上一層畫好的元素，不跟著重畫。
 */
function ItemTableScroll({ widthStore, hasSelect, children }: { readonly widthStore: ColumnWidthStore; readonly hasSelect: boolean; readonly children: ReactNode }) {
  const widths = useColumnWidths(widthStore);
  return (
    // 格子都停得到、走到哪捲到哪：捲動的區塊不必再多一個停駐點（從表格前面 Tab 一次就進到格子裡）。
    <TableScroll
      label="項目表格"
      layout="fill"
      className={styles.scroll}
      isFocusable={false}
      variables={{ ...pinnedVariables(widths), ...(hasSelect ? { '--item-select-offset': 'var(--item-select-width)' } : {}) }}
    >
      {children}
    </TableScroll>
  );
}

interface ItemTableHeadProps extends Omit<ColumnHeaderProps, 'header'> {
  readonly headers: readonly Header<ItemTableFeatures, Item>[];
}

/** 欄寬（colgroup）與表頭：跟著欄寬重畫的只有這裡與捲動區塊。 */
function ItemTableHead({ headers, sort, onSort, widthStore }: ItemTableHeadProps) {
  return (
    <>
      <ItemColumns
        columns={headers.map((header) => ({ id: header.column.id, meta: header.column.columnDef.meta }))}
        widthStore={widthStore}
      />
      <thead>
        <tr>
          {headers.map((header) => (
            <ColumnHeader key={header.id} header={header} sort={sort} onSort={onSort} widthStore={widthStore} />
          ))}
        </tr>
      </thead>
    </>
  );
}

/** 這個類型沒有的欄位：格子是淡底、不畫符號（7.1），報讀軟體唸「不適用」。 */
function NotApplicable() {
  return <span className="visually-hidden">不適用</span>;
}

function BodyCell({ cell }: { readonly cell: Cell<ItemTableFeatures, Item> }) {
  const meta = cell.column.columnDef.meta;
  const applies = meta?.appliesTo?.(cell.row.original) ?? true;
  const className = cx(
    styles.cell,
    pinClass(meta),
    meta?.isNumeric && styles.numeric,
    !applies && styles.notApplicable,
  );
  const content = applies ? <FlexRender cell={cell} /> : <NotApplicable />;
  // 標題是這一列的列標題，螢幕報讀器在格子間移動時會唸出它，才知道在改哪一個項目。
  return meta?.pin === 'title' ? (
    <th scope="row" className={className}>
      {content}
    </th>
  ) : (
    // data-not-applicable：選取的那一列（components/Table）不蓋掉「不適用」的淡底。
    <td className={className} data-not-applicable={!applies || undefined}>
      {content}
    </td>
  );
}

function rowId(item: Item): string {
  return item.key;
}

/** 表身：每一列一個 tr。選了的列標 data-selected（只掛樣式：表格不是 role="grid"，aria-selected 在 row 上不合法）；意思靠勾選框，底色只是輔助。 */
function ItemTableBody({ rows }: { readonly rows: readonly Row<ItemTableFeatures, Item>[] }) {
  const selection = useSelection();
  return (
    <tbody>
      {rows.map((row, index) => (
        // 第一列標上 data-content-start：給 scripts/ui-audit.js 量第一筆內容離頂端多遠（docs/DESIGN.md 6.2）。
        // data-row-key：停駐點那一列被重抓拿掉時，照原本的順序找到相鄰的一列（lib/useRovingGrid.ts）。
        <tr
          key={row.id}
          data-row-key={row.id}
          data-content-start={index === 0 || undefined}
          data-selected={selection?.isSelected(row.id) === true ? 'true' : undefined}
        >
          {row.getVisibleCells().map((cell) => (
            <BodyCell key={cell.id} cell={cell} />
          ))}
        </tr>
      ))}
    </tbody>
  );
}

interface ItemTableProps {
  readonly items: readonly Item[];
  readonly columns: readonly ItemColumn[];
  readonly visibility: ColumnVisibility;
  readonly sort: ItemSort;
  readonly onSort: (field: ItemSortField) => void;
  /** 換了條件、新的結果還沒回來，暫時顯示舊的結果。 */
  readonly isRefreshing: boolean;
  /** 接在最後一列後面（「已載入 N 個項目」與「載入更多」），在同一個捲動容器裡。 */
  readonly footer?: ReactNode;
  /** 表格外面要把焦點放進某一列時用（「載入更多」不見之後，到新載入的第一列）。 */
  readonly gridRef?: Ref<RovingGridHandle>;
  /** 使用者拉的欄寬（useItemTableState，「顯示的欄位」裡的「重設欄寬」也用它）。 */
  readonly widthStore: ColumnWidthStore;
  /** 快速新增（沒有建立權限或不到寬螢幕時是 null）：畫在表頭下面、第一筆項目上面，見 QuickAddLayer。 */
  readonly quickAdd?: ReactNode;
}

/**
 * 快速新增的那一列：畫在表頭下面、第一筆項目上面，看起來是表身的第一列，但不是 tr。
 * 表格的每一個 tr 都是方向鍵與量欄寬的一列（lib/rovingGrid.ts 的 rowsOf），放一個 tr 進去，方向鍵會停在表單上、
 * 裡面的輸入框被拿掉 Tab 停駐點，點兩下調整欄寬也會量到它。所以這一層是捲動區塊的直接子層（高 0、黏在左邊、寬度是可見的寬度，
 * 橫向捲動時不跟著走），表單絕對定位在表頭下面；表身最前面用 data-has-quick-add 的 tbody::before 空出同高的位置。
 * 放在表格前面：Tab 先到表單，再進表格（不必走過五十列才新增）。
 */
function QuickAddLayer({ children }: { readonly children: ReactNode }) {
  return (
    <div className={styles.quickAddLayer} data-quick-add="row">
      <div className={styles.quickAddRow}>{children}</div>
    </div>
  );
}

/**
 * 專案項目的表格（工作區頁）：容器撐滿剩下的高度、兩個方向自己捲；表頭黏在上方，編號與標題固定在左邊，
 * 橫向捲軸一直在畫面裡（docs/DESIGN.md 6.2、7.1）。欄寬固定（table-layout: fixed），使用者可以調（7.1「欄寬」）。
 * 整張表格（表頭加所有格子）只有一個 Tab 停駐點，方向鍵在格子之間移動（8.3「清單的鍵盤操作」，lib/useRovingGrid.ts）；
 * 取得焦點的格子捲到看得到的地方。
 */
export function ItemTable(props: ItemTableProps) {
  const { items, columns, visibility, sort, onSort, isRefreshing, footer, gridRef, widthStore, quickAdd = null } = props;
  const hintId = useId();
  const { can } = useProjectTable();
  const selection = useSelection();
  const tableRef = useRef<HTMLTableElement>(null);
  // 項目面板的上一個／下一個照表格現在的順序；關上面板時焦點回到這張表上那一列的編號。
  useItemSequence(items.map(rowId), tableRef);
  const { gridProps, handle } = useRovingGrid(tableRef);
  useImperativeHandle(gridRef, () => handle, [handle]);
  const table = useTable({
    features: itemTableFeatures,
    columns,
    data: items,
    getRowId: rowId,
    state: { columnVisibility: visibility },
  });
  const headers = table.getHeaderGroups().flatMap((group) => group.headers);

  return (
    <ItemTableScroll widthStore={widthStore} hasSelect={columns.some((column) => column.meta?.pin === 'select')}>
      {quickAdd !== null && <QuickAddLayer>{quickAdd}</QuickAddLayer>}
      <table
        ref={tableRef}
        className={cx(tableStyles.table, styles.table)}
        data-has-quick-add={quickAdd !== null || undefined}
        aria-busy={isRefreshing}
        aria-describedby={hintId}
        {...gridProps}
        onKeyDown={(event) => {
          gridProps.onKeyDown(event);
          if (selection !== null && selection.count > 0) cancelOnEscape(event, selection.cancel);
        }}
      >
        <caption className="visually-hidden">項目</caption>
        <ItemTableHead headers={headers} sort={sort} onSort={onSort} widthStore={widthStore} />
        <ItemTableBody rows={table.getRowModel().rows} />
      </table>
      {/* 進到表格時唸一次（表格的說明），不在每一格重複。hidden：只給 aria-describedby 引用，報讀軟體逐字瀏覽時不會再唸一次。 */}
      <p id={hintId} hidden>
        {tableHint(can.update, { isResizable: true })}
      </p>
      {footer}
    </ItemTableScroll>
  );
}
