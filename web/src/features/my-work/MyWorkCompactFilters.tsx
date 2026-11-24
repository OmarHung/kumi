import { Button } from '../../components/Button/Button';
import { hasActiveMyWorkFilters, myWorkFilterEntries, type MyWorkFilters } from '../../domain/myWorkView';
import { FilterPopover, SearchField } from '../item-table/FilterFields';
import { ALL_MY_WORK_CONDITIONS, MyWorkConditionFields } from './MyWorkConditionFields';
import { MyWorkGroupingField } from './MyWorkGroupingField';
import { MyWorkSortField } from './MyWorkSortField';

interface MyWorkCompactFiltersProps {
  readonly filters: MyWorkFilters;
  readonly onChange: (next: MyWorkFilters) => void;
  readonly onClear: () => void;
  /** 「篩選」按鈕的 id：浮層裡的「清除條件」按了跟著不見時，焦點回到這顆按鈕。 */
  readonly buttonId: string;
  /** 表格才有「分組」與「排序」。 */
  readonly isTable: boolean;
}

/**
 * 窄螢幕的篩選：一顆「篩選」，看得到目前有幾個條件；打開是標籤看得到的所有條件（搜尋、類型、狀態、到期、
 * 優先度、專案），表格時最後多「分組」與「排序」。條件一改就套用、浮層留著。
 */
export function MyWorkCompactFilters({ filters, onChange, onClear, buttonId, isTable }: MyWorkCompactFiltersProps) {
  return (
    <FilterPopover count={myWorkFilterEntries(filters).length} buttonId={buttonId} groupLabel="篩選項目">
      <SearchField value={filters.q} onSearch={(q) => { onChange({ ...filters, q }); }} />
      <MyWorkConditionFields filters={filters} keys={ALL_MY_WORK_CONDITIONS} onChange={onChange} />
      {isTable && <MyWorkGroupingField />}
      {isTable && <MyWorkSortField />}
      {hasActiveMyWorkFilters(filters) && (
        <Button size="sm" onClick={onClear}>
          清除條件
        </Button>
      )}
    </FilterPopover>
  );
}
