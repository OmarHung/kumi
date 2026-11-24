import { useNavigate, useSearchParams } from 'react-router';
import { SelectField } from '../../components/Field/Field';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { switchMyWorkLayoutParams, type MyWorkLayout } from '../../domain/myWorkView';
import { useIsCompact } from '../../lib/useMediaQuery';
import { panelItemOf, searchWithPanelItem } from '../work-items/itemLinks';

const LAYOUTS: readonly { readonly layout: MyWorkLayout; readonly label: string }[] = [
  { layout: 'table', label: '表格' },
  { layout: 'board', label: '看板' },
  { layout: 'calendar', label: '行事曆' },
  { layout: 'timeline', label: '時間軸' },
];

/**
 * 表格、看板、行事曆與時間軸的切換，和專案頁的 ViewSwitch 同一種做法：寬螢幕是連結的分頁（網址就是狀態，
 * 可以在新分頁打開、上一頁回到原本的版面），48rem 以下是一個下拉（標籤視覺上隱藏，選了就導到同樣的網址）。
 * 條件與分組（group）跟著帶過去，開著的項目面板（item）也是：換版面不該把正在看的項目關掉。
 */
export function MyWorkViewSwitch({ layout }: { readonly layout: MyWorkLayout }) {
  const [searchParams] = useSearchParams();
  const isCompact = useIsCompact();
  const navigate = useNavigate();
  const panelItem = panelItemOf(searchParams);
  const tabs = LAYOUTS.map((option) => {
    const isCurrent = option.layout === layout;
    // 目前的版面連到自己（網址不變）；另一個只帶兩邊都有的條件。
    const params = isCurrent ? searchParams : switchMyWorkLayoutParams(searchParams, option.layout);
    const search = isCurrent ? `?${params.toString()}` : searchWithPanelItem(`?${params.toString()}`, panelItem);
    return { key: option.layout, label: option.label, to: { search }, isCurrent };
  });
  if (!isCompact) {
    return <NavTabs label="版面" tabs={tabs} />;
  }
  return (
    <SelectField
      label="版面"
      size="sm"
      isLabelHidden
      value={layout}
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
