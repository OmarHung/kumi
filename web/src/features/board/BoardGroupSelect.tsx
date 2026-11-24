import { SelectField } from '../../components/Field/Field';
import { boardGroupParam, parseBoardGroup } from '../../domain/boardGroup';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { toolbarFieldProps } from '../item-table/toolbarField';
import type { BoardFilterState } from './useBoardFilters';

// 這個檔案由頁面的工具列直接匯入：不要匯入拖曳的套件（看板的那些是 lazy 載入的）。

const STATE_VALUE = 'state';

/**
 * 看板依什麼分欄：狀態（預設）、負責人，以及每一個單選欄位。選項的文字是「依…分組」：工具列上標籤只給報讀軟體，
 * 看得到的只有選到的那一項，光寫「負責人」會和負責人的篩選分不開。
 * 是「看哪一種看板」，選了就換（規範 7.3 的馬上生效），和「流程」一樣；分組不算篩選條件，不計入「更多條件」與「篩選」的數字。
 * 值寫在網址的 group（domain/boardGroup.ts）。
 */
export function BoardGroupSelect({ board, isToolbar }: { readonly board: BoardFilterState; readonly isToolbar: boolean }) {
  const { detail } = useProjectTable();
  const { filters, setFilters } = board;
  const fields = detail.blueprint.fields.filter((field) => field.type === 'single_select');
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="分組"
      value={boardGroupParam(filters.group) ?? STATE_VALUE}
      options={[
        { value: STATE_VALUE, label: '依狀態分組' },
        { value: 'assignee', label: '依負責人分組' },
        ...fields.map((field) => ({ value: `field.${field.key}`, label: `依「${field.name}」分組` })),
      ]}
      onChange={(value) => {
        setFilters({ ...filters, group: parseBoardGroup(value === STATE_VALUE ? null : value) });
      }}
    />
  );
}
