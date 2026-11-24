import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { AppProviders } from './app/AppProviders';
import { createQueryClient } from './app/queryClient';
import { routes } from './app/routes';
import { queryKeys } from './api/queryKeys';
import { createSignalRConnection } from './features/live/signalRConnection';
import { installErrorReporting, pageErrorReporter, setSignedOutCheck } from './lib/errorReporting';
import '@fontsource-variable/plus-jakarta-sans';
import '@fontsource-variable/geist-mono';
import './styles/tokens.css';
import './styles/global.css';

// 最先掛上：之後任何一段程式（包括畫面第一次渲染）沒接住的例外都回報到伺服器（lib/errorReporting.ts）。
installErrorReporting(window, pageErrorReporter());

const container = document.getElementById('root');
if (container === null) {
  throw new Error('index.html 裡少了 id 是 root 的元素。');
}

const queryClient = createQueryClient();
// 登入狀態是 null 就是知道沒登入（登入頁、被登出之後）：那時回報只會是 401，不送。還在確認（undefined）時照樣送。
setSignedOutCheck(() => queryClient.getQueryData(queryKeys.session) === null);
const router = createBrowserRouter(routes);

createRoot(container).render(
  <StrictMode>
    <AppProviders queryClient={queryClient} liveConnection={createSignalRConnection}>
      <RouterProvider router={router} />
    </AppProviders>
  </StrictMode>,
);
