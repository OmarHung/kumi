import { StrictMode, type ReactNode } from 'react';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { AppProviders } from '../app/AppProviders';
import { createQueryClient } from '../app/queryClient';
import { routes } from '../app/routes';
import type { LiveConnectionFactory } from '../features/live/liveTypes';

function PassThrough({ children }: { readonly children: ReactNode }) {
  return children;
}

/** 用正式的路由與 Provider 渲染整個應用程式，從指定的網址開始。 */
export function renderApp(
  path = '/',
  { strict = false, liveConnection }: { readonly strict?: boolean; readonly liveConnection?: LiveConnectionFactory } = {},
) {
  // 測試裡不重試，失敗的情境才不會等重試的延遲。
  const queryClient = createQueryClient({ queries: { retry: false } });
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  const user = userEvent.setup();
  // 開發模式的 StrictMode 會把 effect 掛上、清理、再掛上：要測這種情況時 strict: true。
  const Wrapper = strict ? StrictMode : PassThrough;
  const view = render(
    <Wrapper>
      <AppProviders queryClient={queryClient} liveConnection={liveConnection}>
        <RouterProvider router={router} />
      </AppProviders>
    </Wrapper>,
  );
  return { ...view, user, router, queryClient };
}

export function currentPath(router: ReturnType<typeof createMemoryRouter>): string {
  const { pathname, search } = router.state.location;
  return `${pathname}${search}`;
}
