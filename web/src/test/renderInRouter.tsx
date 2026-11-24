import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';

/** 單獨測一個用到連結的元件：放進只有一條路由（任何網址都畫它）的 memory router。 */
export function renderInRouter(ui: ReactElement, path = '/') {
  const router = createMemoryRouter([{ path: '*', element: ui }], { initialEntries: [path] });
  const user = userEvent.setup();
  return { ...render(<RouterProvider router={router} />), router, user };
}
