import { lazy, Suspense } from 'react';
import type { RouteObject } from 'react-router';
import { LoadingState } from '../components/StatusViews/StatusViews';
import { RouteErrorPage } from '../features/errors/RouteErrorPage';

// 只在開發模式有的頁面：/design 是每個共用元件的每種狀態（給審查的人淺色與深色各看一遍）。
// 正式建置時 import.meta.env.DEV 是 false，這一段與頁面的程式一起被拿掉，dist 裡沒有它。
// 不放在登入檢查裡：沒有後端也看得到（npm run dev）。
const DesignPage = import.meta.env.DEV
  ? lazy(() => import('../features/design/DesignPage').then((module) => ({ default: module.DesignPage })))
  : null;

export const devRoutes: RouteObject[] =
  DesignPage === null
    ? []
    : [
        {
          path: '/design',
          errorElement: <RouteErrorPage />,
          element: (
            <Suspense fallback={<LoadingState label="正在載入元件…" />}>
              <DesignPage />
            </Suspense>
          ),
        },
      ];
