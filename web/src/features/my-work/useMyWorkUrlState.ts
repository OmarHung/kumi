import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { myWorkFiltersFromParams, myWorkLayoutOf, type MyWorkFilters, type MyWorkLayout } from '../../domain/myWorkView';
import { withMyWorkFilters } from './myWorkUrl';

export interface MyWorkUrlState {
  readonly layout: MyWorkLayout;
  /** 網址上的條件，原樣（還沒照選的專案清掉不屬於它的類型、狀態）。 */
  readonly urlFilters: MyWorkFilters;
  /** 換上新的條件。不留歷史紀錄：上一頁應該回到上一個畫面，不是上一個篩選；版面、分組、開著的面板留著。 */
  readonly setFilters: (next: MyWorkFilters) => void;
}

/** 「我的工作」的版面與條件都放在網址（domain/myWorkView.ts）：重新整理、分享、上一頁都對。 */
export function useMyWorkUrlState(): MyWorkUrlState {
  const [searchParams, setSearchParams] = useSearchParams();
  const layout = myWorkLayoutOf(searchParams);
  const urlFilters = useMemo(() => myWorkFiltersFromParams(searchParams), [searchParams]);
  const setFilters = useCallback(
    (next: MyWorkFilters) => {
      setSearchParams((previous) => withMyWorkFilters(previous, next), { replace: true });
    },
    [setSearchParams],
  );
  return { layout, urlFilters, setFilters };
}
