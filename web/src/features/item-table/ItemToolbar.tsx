import { Button } from '../../components/Button/Button';
import { hasActiveFilters, type ItemFilters } from '../../domain/itemFilters';
import { useLayoutTier } from '../../lib/useLayoutTier';
import { ColumnPicker } from './ColumnPicker';
import { hideableColumns } from './columns';
import { CompactFilters } from './CompactFilters';
import { ConditionFields, FilterBar, MoreConditions, SearchField } from './FilterFields';
import { useInlineConditions, type CollapsibleCondition } from './inlineConditions';
import { useProjectTable } from './ProjectTableContext';
import type { ItemTableState } from './useItemTableState';

/**
 * 寬螢幕的篩選：搜尋、類型、狀態、負責人排在工具列上（矮一號、標籤視覺上隱藏），有條件時多一顆「清除條件」；
 * 按了之後它跟著不見，焦點到搜尋框（useItemTableState 的 clearFilters）。
 */
function ItemFilterBar({ table }: { readonly table: ItemTableState }) {
  const { filters, setFilters, clearFilters } = table;
  const { detail } = useProjectTable();
  // 搜尋一律在列上；其餘依序收進「更多條件」（inlineConditions.ts）。多於一種類型才有「類型」。週期與里程碑一律在「更多條件」裡。
  const present: readonly CollapsibleCondition[] = detail.blueprint.itemTypes.length > 1 ? ['type', 'state', 'assignee', 'createdBy', 'due', 'priority'] : ['state', 'assignee', 'createdBy', 'due', 'priority'];
  const { barRef, inlineKeys, moreKeys, hasMore } = useInlineConditions({ present, fixedCount: 1 }, filters);
  const patchFilters = (patch: Partial<ItemFilters>) => {
    setFilters({ ...filters, ...patch });
  };
  return (
    <FilterBar label="篩選項目" barRef={barRef}>
      <SearchField isToolbar inputRef={table.searchRef} value={filters.q} onSearch={(q) => { setFilters({ ...filters, q }); }} />
      <ConditionFields isToolbar filters={filters} keys={inlineKeys} onChange={patchFilters} />
      {hasMore && <MoreConditions filters={filters} keys={moreKeys} onChange={patchFilters} />}
      {hasActiveFilters(filters) && (
        <Button variant="ghost" size="sm" onClick={clearFilters}>
          清除條件
        </Button>
      )}
    </FilterBar>
  );
}

/**
 * 工具列上、檢視分頁後面的控制項：64rem 以上是整排篩選；以下收進「篩選」，
 * 其中平板（48～64rem）的搜尋框留在列上（最窄 10rem，有空間才長大，樣式在 Toolbar），手機的搜尋跟著收進浮層。
 */
export function ItemToolbarControls({ table }: { readonly table: ItemTableState }) {
  const tier = useLayoutTier();
  if (tier === 'wide') return <ItemFilterBar table={table} />;
  if (tier === 'phone') return <CompactFilters table={table} />;
  const { filters, setFilters } = table;
  return (
    <>
      <SearchField isToolbar inputRef={table.searchRef} value={filters.q} onSearch={(q) => { setFilters({ ...filters, q }); }} />
      <CompactFilters table={table} hasSearch={false} />
    </>
  );
}

/**
 * 工具列靠右（只有寬螢幕）：「顯示的欄位」，裡面也有「重設欄寬」。窄螢幕的新增是右下角的圓形按鈕（NewItemFab），沒有靠右的東西。
 * 對話框本身由專案頁（ProjectWorkspace）依 table.quickAdd 畫：視窗變寬、按鈕不見了，對話框照樣開著。
 */
export function ItemToolbarEnd({ table }: { readonly table: ItemTableState }) {
  return (
    <ColumnPicker
      columns={hideableColumns(table.columns)}
      visibility={table.visibility}
      onChange={table.setVisibility}
      onResetWidths={table.widths.reset}
    />
  );
}
