import { useSearchParams } from 'react-router';
import { myWorkMineOf } from '../../domain/myWorkView';
import { SelectField } from '../../components/Field/Field';
import type { SelectOption } from '../../components/Select/Select';
import { DEFAULT_COLUMN_ORDER } from './myWorkColumnOrder';
import { myWorkColumns } from './myWorkColumns';
import { groupingOf } from './myWorkGroups';
import { DEFAULT_MY_WORK_SORT, myWorkSortOf, myWorkSortValue, parseMyWorkSort, SORT_FIELDS, withMyWorkSort } from './myWorkSort';

const DIRECTIONS = [
  { direction: 'asc', word: '遞增' },
  { direction: 'desc', word: '遞減' },
] as const;

/**
 * 窄螢幕的排序（收在「篩選」裡）：窄螢幕是清單，沒有表頭可以點，和專案的清單一樣是一個下拉——每一欄遞增、遞減各一個選項，
 * 欄照預設的順序（清單沒有欄的順序）。選回預設（到期日遞增）就從網址拿掉；選了才換（規範 7.3）。
 */
export function MyWorkSortField() {
  const [searchParams, setSearchParams] = useSearchParams();
  const columns = myWorkColumns(DEFAULT_COLUMN_ORDER, groupingOf(searchParams) !== 'project', myWorkMineOf(searchParams) === 'created');
  const options: SelectOption[] = columns.flatMap((column) =>
    DIRECTIONS.map(({ direction, word }) => ({
      value: myWorkSortValue({ field: SORT_FIELDS[column.id], direction }),
      label: `${column.label}（${word}）`,
    })),
  );
  return (
    <SelectField
      label="排序"
      value={myWorkSortValue(myWorkSortOf(searchParams) ?? DEFAULT_MY_WORK_SORT)}
      options={options}
      onChange={(value) => {
        setSearchParams((previous) => withMyWorkSort(previous, parseMyWorkSort(value)), { replace: true });
      }}
    />
  );
}
