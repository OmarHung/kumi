import { QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { createQueryClient } from '../app/queryClient';
import { ItemPanelHost } from '../features/item-page/ItemPanelHost';
import { abilitiesOf, ProjectTableProvider } from '../features/item-table/ProjectTableContext';
import { TimelineView } from '../features/timeline/TimelineView';
import type { DateViewFilters } from '../domain/projectView';
import { smkDetail } from './fixtures';
import { STATE_GROUP } from '../domain/boardGroup';

export const TIMELINE_TODAY = '2026-11-18';

export const NO_FILTERS: DateViewFilters = { workflow: null, type: null, state: null, assignee: null, createdBy: null, due: null, priority: null, cycle: null, milestone: null, fields: {}, group: STATE_GROUP, q: '' };

interface RenderTimelineOptions {
  readonly path?: string;
  readonly filters?: DateViewFilters;
}

/** 單獨畫時間軸：專案頁（ProjectWorkspace）給的專案內容、今天、項目面板都在，網址從 path 開始。 */
export function renderTimeline({ path = '/projects/SMK?view=timeline', filters = NO_FILTERS }: RenderTimelineOptions = {}) {
  const detail = smkDetail();
  const queryClient = createQueryClient({ queries: { retry: false } });
  const router = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <ProjectTableProvider value={{ detail, can: abilitiesOf(detail), todayDate: TIMELINE_TODAY, onOpenMembers: () => undefined }}>
            <ItemPanelHost layout="fill" projectKey="SMK">
              <TimelineView projectKey="SMK" filters={filters} todayDate={TIMELINE_TODAY} />
            </ItemPanelHost>
          </ProjectTableProvider>
        ),
      },
    ],
    { initialEntries: [path] },
  );
  const user = userEvent.setup();
  const view = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { ...view, router, user, queryClient };
}

/**
 * 單獨畫「我的工作」的行事曆或時間軸：沒有單一專案，只有頁面給的項目面板（ItemPanelHost，跨專案）。
 * 要先用 signedInAs 提供專案與項目（跨專案清單）。
 */
export function renderMyWorkDateView(ui: ReactElement, path = '/my-work?view=calendar') {
  const queryClient = createQueryClient({ queries: { retry: false } });
  const router = createMemoryRouter(
    [{ path: '*', element: <ItemPanelHost layout="fill">{ui}</ItemPanelHost> }],
    { initialEntries: [path] },
  );
  const user = userEvent.setup();
  const view = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { ...view, router, user, queryClient };
}
