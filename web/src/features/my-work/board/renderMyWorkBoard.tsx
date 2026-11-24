import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { AppProviders } from '../../../app/AppProviders';
import { createQueryClient } from '../../../app/queryClient';
import { CurrentUserProvider } from '../../../auth/CurrentUser';
import { today } from '../../../domain/dates';
import { DEFAULT_MY_WORK_FILTERS, type MyWorkFilters } from '../../../domain/myWorkView';
import { exampleOwner } from '../../../test/fixtures';
import { MyWorkBoard } from './MyWorkBoard';

const FALLBACK_ID = 'page-search';

/** 測試用：把看板放進登入後的畫面（目前使用者、查詢快取、路由），條件可以中途換掉。 */
export function renderMyWorkBoard(initial: MyWorkFilters = DEFAULT_MY_WORK_FILTERS, { hasFallback = false } = {}) {
  const queryClient = createQueryClient({ queries: { retry: false } });
  const setters: { current: (filters: MyWorkFilters) => void } = { current: () => undefined };
  const todayDate = today();
  function Host() {
    const [filters, setFilters] = useState(initial);
    setters.current = setFilters;
    return (
      <>
        {/* 頁面上的搜尋框：卡片都不在畫面上時，焦點的去處（由頁面傳進來）。 */}
        {hasFallback && <input id={FALLBACK_ID} aria-label="頁面的搜尋框" />}
        <MyWorkBoard filters={filters} todayDate={todayDate} focusFallback={hasFallback ? [FALLBACK_ID] : undefined} />
      </>
    );
  }
  const router = createMemoryRouter([{ path: '*', element: <Host /> }], { initialEntries: ['/my-work?view=board'] });
  const user = userEvent.setup();
  const view = render(
    <AppProviders queryClient={queryClient}>
      <CurrentUserProvider user={exampleOwner}>
        <RouterProvider router={router} />
      </CurrentUserProvider>
    </AppProviders>,
  );
  return {
    ...view,
    user,
    queryClient,
    /** 換條件（和頁面的工具列一樣，由上一層重新給 filters）。 */
    setFilters: (filters: MyWorkFilters) => {
      setters.current(filters);
    },
  };
}
