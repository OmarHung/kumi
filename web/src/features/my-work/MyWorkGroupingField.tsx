import { useSearchParams } from 'react-router';
import { SelectField } from '../../components/Field/Field';
import type { SelectOption } from '../../components/Select/Select';
import type { ToolbarFieldProps } from '../item-table/toolbarField';
import { toolbarFieldProps } from '../item-table/toolbarField';
import { groupingOf, groupingSearch, type MyWorkGrouping } from './myWorkGroups';

const OPTIONS: readonly SelectOption[] = [
  { value: 'urgency', label: '依急迫程度' },
  { value: 'project', label: '依專案' },
];

/**
 * 表格的分組：依急迫程度（預設）或依專案，記在網址的 group（重新整理、分享、上一頁都對；開著的面板 item 照樣帶著）。
 * 版面已經是工具列上的分頁，這一組不能也是分頁（一列兩組分頁分不清楚），所以是下拉。
 */
export function MyWorkGroupingField({ isToolbar }: ToolbarFieldProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="分組"
      value={groupingOf(searchParams)}
      options={OPTIONS}
      onChange={(next) => {
        const grouping: MyWorkGrouping = next === 'project' ? 'project' : 'urgency';
        setSearchParams(new URLSearchParams(groupingSearch(searchParams, grouping)));
      }}
    />
  );
}
