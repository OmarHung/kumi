import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../api/http';
import { flushPending } from '../../test/flush';
import { server } from '../../test/server';
import { RouteErrorPage } from './RouteErrorPage';

function Broken({ error }: { readonly error: unknown }): never {
  throw error;
}

/** 一個一定會出錯的頁面，錯誤頁是 RouteErrorPage；回傳收到的回報。 */
function renderBrokenPage(error: unknown, path = '/projects/WEB') {
  const reports: unknown[] = [];
  server.use(
    http.post('/v1/client-errors', async ({ request }) => {
      reports.push(await request.json());
      return HttpResponse.json({ success: true, data: null, error: null });
    }),
  );
  const router = createMemoryRouter(
    [{ path: '*', element: <Broken error={error} />, errorElement: <RouteErrorPage /> }],
    { initialEntries: [path] },
  );
  render(<RouterProvider router={router} />);
  return reports;
}

describe('路由的錯誤頁', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('畫面出錯時請使用者重新整理，並把錯誤回報到伺服器（kind: render，路徑不帶查詢字串）', async () => {
    // Arrange
    // React 與 React Router 會在主控台印出被錯誤頁接住的例外；這裡是刻意的，不讓它洗版。
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const error = new TypeError('cannot read properties of undefined');
    window.history.replaceState(null, '', '/projects/WEB?q=secret');

    // Act
    const reports = renderBrokenPage(error);

    // Assert
    expect(await screen.findByRole('heading', { name: '畫面出了問題' })).toBeInTheDocument();
    await waitFor(() => {
      expect(reports).toHaveLength(1);
    });
    expect(reports[0]).toMatchObject({
      kind: 'render',
      message: 'TypeError: cannot read properties of undefined',
      path: '/projects/WEB',
    });
    window.history.replaceState(null, '', '/');
  });

  it('API 的失敗不回報', async () => {
    // Arrange
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const error = new ApiError({ status: 404, code: 'project.not_found', message: '找不到這個專案。' });

    // Act
    const reports = renderBrokenPage(error);

    // Assert
    expect(await screen.findByRole('heading', { name: '畫面出了問題' })).toBeInTheDocument();
    await flushPending();
    expect(reports).toEqual([]);
  });
});
