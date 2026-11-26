import type { ItemSort } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { SelectField } from '../../components/Field/Field';
import { hasActiveFilters, type ItemFilters } from '../../domain/itemFilters';
import type { ItemColumn } from './columns';
import { ALL_CONDITIONS, ConditionFields, FilterPopover, SearchField } from './FilterFields';
import { DEFAULT_SORT } from './ItemTable';
import type { ItemTableState } from './useItemTableState';

/** 有幾個條件在篩選（排序不算：只換順序，項目還是全部）。 */
export function activeFilterCount(filters: ItemFilters): number {
  return [filters.type !== null, filters.state !== null, filters.assignee !== null, filters.createdBy !== null, filters.due !== null, filters.priority !== null, filters.cycle !== null, filters.milestone !== null, filters.q !== ''].filter(Boolean).length + Object.keys(filters.fields).length;
}

interface SortChoice {
  readonly value: string;
  readonly label: string;
  readonly sort: ItemSort;
}

const DIRECTIONS = [
  { direction: 'asc', word: '遞增' },
  { direction: 'desc', word: '遞減' },
] as const;

function sortValue(sort: ItemSort): string {
  return `${sort.field}:${sort.direction}`;
}

const DEFAULT_VALUE = sortValue(DEFAULT_SORT);

/** 表格上可以排序的欄（和表頭的排序按鈕同一組），每一欄遞增、遞減各一個選項。 */
function sortChoices(columns: readonly ItemColumn[]): SortChoice[] {
  const fields = columns.flatMap((column) =>
    column.meta?.sortField === undefined ? [] : [{ field: column.meta.sortField, label: column.meta.label }],
  );
  return fields.flatMap(({ field, label }) =>
    DIRECTIONS.map(({ direction, word }) => {
      const sort: ItemSort = { field, direction };
      return { value: sortValue(sort), label: `${label}（${word}）`, sort };
    }),
  );
}

function parseSortValue(value: string, choices: readonly SortChoice[]): ItemSort | null {
  return value === DEFAULT_VALUE ? null : (choices.find((choice) => choice.value === value)?.sort ?? null);
}

/**
 * 窄螢幕沒有表頭可以點：排序是一個下拉，選回預設（編號遞增）就從網址拿掉。
 * 選了才換（規範 7.3 的馬上生效）：方向鍵與打字只移動反白，不會一路重抓清單。
 */
function SortField({ table }: { readonly table: ItemTableState }) {
  const { filters, setFilters, columns } = table;
  const choices = sortChoices(columns);
  return (
    <SelectField
      label="排序"
      value={sortValue(filters.sort ?? DEFAULT_SORT)}
      options={choices}
      onChange={(value) => {
        setFilters({ ...filters, sort: parseSortValue(value, choices) });
      }}
    />
  );
}

/**
 * 窄螢幕的篩選：一顆「篩選」（FilterPopover，看板也用），看得到目前有幾個條件；
 * 打開是搜尋、類型、狀態、負責人與排序（標籤看得到）。
 * 「清除條件」按了跟著不見時，焦點回到「篩選」（useItemTableState 的 clearFilters）。
 */
export function CompactFilters({ table }: { readonly table: ItemTableState }) {
  const { filters, setFilters, clearFilters } = table;
  return (
    <FilterPopover count={activeFilterCount(filters)} buttonId={table.filterButtonId} groupLabel="篩選項目">
      <SearchField value={filters.q} onSearch={(q) => { setFilters({ ...filters, q }); }} />
      <ConditionFields filters={filters} keys={ALL_CONDITIONS} onChange={(patch) => { setFilters({ ...filters, ...patch }); }} />
      <SortField table={table} />
      {hasActiveFilters(filters) && (
        <Button size="sm" onClick={clearFilters}>
          清除條件
        </Button>
      )}
    </FilterPopover>
  );
}
