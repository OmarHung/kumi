import { useNavigate, useSearchParams } from 'react-router';
import type { OverviewScope } from '../../api/overview';
import { SelectField } from '../../components/Field/Field';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { Toolbar } from '../../components/Toolbar/Toolbar';
import { useIsCompact } from '../../lib/useMediaQuery';
import {
  OVERVIEW_SORT_LABELS,
  OVERVIEW_SORTS,
  overviewScopeOf,
  overviewSortOf,
  withOverviewView,
  type OverviewSort,
} from './overviewView';

const SCOPES: readonly { readonly value: OverviewScope; readonly label: string }[] = [
  { value: 'managed', label: '只看我管理的' },
  { value: 'visible', label: '全部看得到的' },
];

const SORT_OPTIONS = OVERVIEW_SORTS.map((sort) => ({ value: sort, label: OVERVIEW_SORT_LABELS[sort] }));

function isSort(value: string): value is OverviewSort {
  return OVERVIEW_SORTS.some((sort) => sort === value);
}

/**
 * 範圍（「只看我管理的」「全部看得到的」）與排序，都記在網址（scope、sort；預設的不寫）。
 * 範圍是連結的分頁（規範 7.3：狀態在網址裡用 NavTabs），窄螢幕換成下拉；排序是下拉，選了就換。
 */
export function OverviewToolbar() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const isCompact = useIsCompact();
  const scope = overviewScopeOf(params);
  const sort = overviewSortOf(params);
  const search = (next: { readonly scope: OverviewScope; readonly sort: OverviewSort }) => {
    const text = withOverviewView(params, next).toString();
    return { search: text === '' ? '' : `?${text}` };
  };
  const tabs = SCOPES.map((option) => ({
    key: option.value,
    label: option.label,
    to: search({ scope: option.value, sort }),
    isCurrent: option.value === scope,
  }));
  return (
    <Toolbar>
      {isCompact ? (
        <SelectField
          label="範圍"
          size="sm"
          isLabelHidden
          value={scope}
          options={SCOPES}
          onChange={(next) => {
            const target = tabs.find((tab) => tab.key === next);
            if (target !== undefined && !target.isCurrent) void navigate(target.to, { replace: true });
          }}
        />
      ) : (
        <NavTabs label="範圍" tabs={tabs} />
      )}
      <SelectField
        label="排序"
        size="sm"
        value={sort}
        options={SORT_OPTIONS}
        onChange={(next) => {
          if (isSort(next)) void navigate(search({ scope, sort: next }), { replace: true });
        }}
      />
    </Toolbar>
  );
}
