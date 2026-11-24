import { useRef, type Ref } from 'react';
import { Button } from '../../components/Button/Button';
import { hasActiveMyWorkFilters, type MyWorkFilters } from '../../domain/myWorkView';
import { remInPixels, useElementWidth } from '../../lib/useElementWidth';
import { FilterBar, FilterPopover, SearchField } from '../item-table/FilterFields';
import { COLLAPSE_ORDER, inlineConditionKeys, type CollapsibleCondition } from '../item-table/inlineConditions';
import { isConditionActive, MyWorkConditionFields, useTypeChoices, type MyWorkConditionKey } from './MyWorkConditionFields';

interface MyWorkFilterProps {
  readonly filters: MyWorkFilters;
  readonly onChange: (next: MyWorkFilters) => void;
  /** 「清除條件」：回到預設的條件，焦點到搜尋框（MyWorkPage）。 */
  readonly onClear: () => void;
}

/** 「更多條件」：「專案」一律在這裡，放不下的條件依序也收進來（inlineConditions.ts）。按鈕上的數字是裡面有值的條件。 */
function MoreConditions({ filters, onChange, keys }: Omit<MyWorkFilterProps, 'onClear'> & { readonly keys: ReadonlySet<MyWorkConditionKey> }) {
  const count = [...keys].filter((key) => isConditionActive(filters, key)).length;
  return (
    <FilterPopover label="更多條件" hasIcon={false} count={count} groupLabel="更多條件">
      <MyWorkConditionFields filters={filters} onChange={onChange} keys={keys} />
    </FilterPopover>
  );
}

interface MyWorkFilterBarProps extends MyWorkFilterProps {
  readonly searchRef: Ref<HTMLInputElement>;
}

/**
 * 寬螢幕的篩選：搜尋、類型（多於一種才有）、狀態、到期、優先度排在工具列上（矮一號、標籤視覺上隱藏），
 * 「更多條件」裡有「專案」；有條件時多一顆「清除條件」，按了之後它跟著不見，焦點到搜尋框。
 * 依列實際的寬度（側欄、面板、視窗都會變）決定哪些條件收進「更多條件」（inlineConditions.ts）；「專案」一律在
 * 「更多條件」裡，所以那一顆一直在列上（hasPlanning）。
 */
export function MyWorkFilterBar({ filters, onChange, onClear, searchRef }: MyWorkFilterBarProps) {
  const barRef = useRef<HTMLDivElement>(null);
  const width = useElementWidth(barRef);
  const { hasChoice } = useTypeChoices(filters);
  const present: readonly CollapsibleCondition[] = hasChoice ? ['type', 'state', 'due', 'priority'] : ['state', 'due', 'priority'];
  const inlineKeys = inlineConditionKeys(width === null ? null : width / remInPixels(), { present, fixedCount: 1, hasPlanning: true });
  const moreKeys = new Set<MyWorkConditionKey>([...COLLAPSE_ORDER.filter((key) => present.includes(key) && !inlineKeys.has(key)), 'project']);
  return (
    <FilterBar label="篩選項目" barRef={barRef}>
      <SearchField isToolbar inputRef={searchRef} value={filters.q} onSearch={(q) => { onChange({ ...filters, q }); }} />
      <MyWorkConditionFields isToolbar filters={filters} keys={inlineKeys} onChange={onChange} />
      <MoreConditions filters={filters} onChange={onChange} keys={moreKeys} />
      {hasActiveMyWorkFilters(filters) && (
        <Button variant="ghost" size="sm" onClick={onClear}>
          清除條件
        </Button>
      )}
    </FilterBar>
  );
}
