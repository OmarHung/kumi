import { useNavigate, useSearchParams } from 'react-router';
import { SelectField } from '../../components/Field/Field';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { myWorkFiltersFromParams, myWorkMineOf, type MyWorkMine } from '../../domain/myWorkView';
import { useIsCompact } from '../../lib/useMediaQuery';
import { withMyWorkFilters } from './myWorkUrl';

const OPTIONS: readonly { readonly value: MyWorkMine; readonly label: string }[] = [
  { value: 'assigned', label: '我負責的' },
  { value: 'created', label: '我建立的' },
];

/**
 * 「我負責的｜我建立的」：這一頁在看哪一份（網址的 mine=created，預設我負責的不寫）。
 * 它不是篩選條件，所以放在版面與「檢視」旁邊，不收進「篩選」；PM 用「我建立的」追自己開出去的項目。
 * 範圍在網址裡，所以和版面一樣是連結的分頁（規範 7.3：狀態在網址裡用 NavTabs），可以在新分頁打開、上一頁回到原本的範圍；
 * 連結只換 mine，其他參數（條件、版面、分組、排序、開著的面板）照舊帶著，只適用於某個範圍的排序由 myWorkSortOf 依範圍忽略。
 * 48rem 以下換成下拉（和版面一樣），放在工具列的第一顆（2026-11-27 使用者回報手機版空間比例不協調：
 * 放在 48px 的頁列裡，44px 的下拉框和標題不協調）；工具列在窄螢幕是一行可以左右滑，多一顆不會換行。
 */
export function MyWorkMineSwitch() {
  const [searchParams] = useSearchParams();
  const mine = myWorkMineOf(searchParams);
  // 網址上的條件原樣帶著（不用畫面上照選的專案清過的那一份），只換 mine。
  const urlFilters = myWorkFiltersFromParams(searchParams);
  const isCompact = useIsCompact();
  const navigate = useNavigate();
  const tabs = OPTIONS.map((option) => {
    const params = withMyWorkFilters(searchParams, { ...urlFilters, mine: option.value });
    return { key: option.value, label: option.label, to: { search: `?${params.toString()}` }, isCurrent: option.value === mine };
  });
  if (!isCompact) {
    return <NavTabs label="範圍" tabs={tabs} />;
  }
  return (
    <SelectField
      label="範圍"
      size="sm"
      isLabelHidden
      value={mine}
      options={tabs.map((tab) => ({ value: tab.key, label: tab.label }))}
      onChange={(next) => {
        const target = tabs.find((tab) => tab.key === next);
        if (target !== undefined && !target.isCurrent) {
          void navigate(target.to);
        }
      }}
    />
  );
}
