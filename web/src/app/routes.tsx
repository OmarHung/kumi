import { Navigate, type RouteObject } from 'react-router';
import { LoginPage } from '../auth/LoginPage';
import { NotFoundPage } from '../features/errors/NotFoundPage';
import { RouteErrorPage } from '../features/errors/RouteErrorPage';
import { ItemPage } from '../features/item-page/ItemPage';
import { OpenRouterCallbackPage } from '../features/ai-login/OpenRouterCallbackPage';
import { CliAuthorizePage } from '../features/cli-login/CliAuthorizePage';
import { OAuthConsentPage } from '../features/oauth/OAuthConsentPage';
import { MyWorkPage } from '../features/my-work/MyWorkPage';
import { OverviewPage } from '../features/overview/OverviewPage';
import { PeoplePage } from '../features/people/PeoplePage';
import { ProjectListPage } from '../features/projects/ProjectListPage';
import { ProjectPage } from '../features/projects/ProjectPage';
import { SettingsHubLayout } from '../features/settings-hub/SettingsHubLayout';
import { AppShell } from '../features/shell/AppShell';
import { SETTINGS_SECTIONS } from '../features/work-items/itemLinks';
import { devRoutes } from './devRoutes';
import {
  LazyAccountPage,
  LazyAiAccountsPage,
  LazyAssistantPage,
  LazyOrganizationAssistantPage,
  LazyChangeSetListPage,
  LazyChangeSetPage,
  LazyDescriptionTemplateEditPage,
  LazyDescriptionTemplatesPage,
  LazyDeletedItemsPage,
  LazyAiSettingsPage,
  LazyEmailSettingsPage,
  LazyStorageUsagePage,
  LazyHostAgentsPage,
  LazyIntegrationsPage,
  LazyItemImportPage,
  LazyInboxPage,
  LazyKnowledgeHomePage,
  LazyKnowledgeSpacePage,
  LazyKnowledgeSpaceSettingsPage,
  LazyKnowledgeTrashPage,
  LazyNotificationSettingsPage,
  LazyPlanningPage,
  LazyProjectInfoPage,
  LazySettingsPage,
} from './lazyPages';
import { RequireAuth } from './RequireAuth';

/**
 * 路由表。正式環境用 createBrowserRouter、測試用 createMemoryRouter，兩邊共用這一份。
 * 找不到的路徑放在登入檢查裡面：沒登入的人先到登入頁，登入後才看到 404。
 */
export const routes: RouteObject[] = [
  ...devRoutes,
  { path: '/login', element: <LoginPage />, errorElement: <RouteErrorPage /> },
  {
    element: <RequireAuth />,
    errorElement: <RouteErrorPage />,
    children: [
      // 同意頁像登入頁：要登入，但沒有側欄，也不掛即時更新與新通知的提示（那些在 AppShell 裡）。
      { path: 'oauth/authorize', element: <OAuthConsentPage /> },
      // kumi 的瀏覽器登入同意頁：同上，終端機裡的 kumi 把使用者帶來按「允許」。
      { path: 'cli/authorize', element: <CliAuthorizePage /> },
      // 用 OpenRouter 登入回來的頁面：同樣不在 AppShell 裡（不被即時更新、新通知的提示、離開確認打擾）。
      { path: 'ai/openrouter/callback', element: <OpenRouterCallbackPage /> },
      {
        element: <AppShell />,
        children: [
          {
            // 頁面出錯時只換掉內容區，側欄還在，使用者可以去別的地方。
            errorElement: <RouteErrorPage />,
            children: [
              { index: true, element: <Navigate to="/my-work" replace /> },
              { path: 'my-work', element: <MyWorkPage /> },
              { path: 'overview', element: <OverviewPage /> },
              { path: 'inbox', element: <LazyInboxPage /> },
              // 知識庫：頁面編號可有可無（空間首頁與一頁是同一個路由，左邊的頁面樹換頁時不重新掛上）；
              // trash、settings 是靜態的片段，比 :pageNumber 優先。
              { path: 'knowledge', element: <LazyKnowledgeHomePage /> },
              { path: 'knowledge/:spaceKey/trash', element: <LazyKnowledgeTrashPage /> },
              { path: 'knowledge/:spaceKey/settings', element: <LazyKnowledgeSpaceSettingsPage /> },
              { path: 'knowledge/:spaceKey/:pageNumber?', element: <LazyKnowledgeSpacePage /> },
              { path: 'projects', element: <ProjectListPage /> },
              { path: 'projects/:projectKey', element: <ProjectPage /> },
              { path: 'projects/:projectKey/items/:itemKey', element: <ItemPage /> },
              {
                // 設定頁是外層的版面路由，四個區塊是它的子路由（不畫自己的元素）：切換區塊時設定頁不會重新掛載，
                // 草稿還在。設定頁本身（index）由 SettingsPage 轉到預設的區塊；不認得的區塊落到下面的 404。
                path: 'projects/:projectKey/settings',
                element: <LazySettingsPage />,
                // element: null 寫明「這一層不畫東西」，React Router 才不會警告找不到要畫的元素。
                children: [{ index: true, element: null }, ...SETTINGS_SECTIONS.map((section) => ({ path: section, element: null }))],
              },
              { path: 'projects/:projectKey/planning', element: <LazyPlanningPage /> },
              { path: 'projects/:projectKey/info', element: <LazyProjectInfoPage /> },
              // 對話編號可有可無：開新對話時網址換成編號，同一個路由，頁面不重新掛上，進行中的串流才不會斷。
              { path: 'projects/:projectKey/assistant/:conversationId?', element: <LazyAssistantPage /> },
              // 不選專案的 AI 助理（組織的對話）；同樣對話編號可有可無。
              { path: 'assistant/:conversationId?', element: <LazyOrganizationAssistantPage /> },
              { path: 'projects/:projectKey/deleted', element: <LazyDeletedItemsPage /> },
              { path: 'projects/:projectKey/import', element: <LazyItemImportPage /> },
              { path: 'projects/:projectKey/changesets', element: <LazyChangeSetListPage /> },
              { path: 'projects/:projectKey/changesets/:number', element: <LazyChangeSetPage /> },
              { path: 'people', element: <PeoplePage /> },
              {
                // 設定中心（features/settings-hub）：外層的版面路由畫頁列與次導覽，換頁時它們不重新掛上（次導覽上的焦點不掉）；
                // 各頁只畫內容。網址沿用改版前的，舊的連結照樣能用。說明格式的新增與編輯頁（填滿畫面的編輯器）不在裡面。
                element: <SettingsHubLayout />,
                children: [
                  { path: 'account', element: <LazyAccountPage /> },
                  { path: 'account/notifications', element: <LazyNotificationSettingsPage /> },
                  { path: 'account/ai', element: <LazyAiAccountsPage /> },
                  { path: 'account/integrations', element: <LazyIntegrationsPage /> },
                  { path: 'description-templates', element: <LazyDescriptionTemplatesPage /> },
                  { path: 'email-settings', element: <LazyEmailSettingsPage /> },
                  { path: 'storage-usage', element: <LazyStorageUsagePage /> },
                  { path: 'ai-settings', element: <LazyAiSettingsPage /> },
                  { path: 'ai-settings/host', element: <LazyHostAgentsPage /> },
                ],
              },
              { path: 'description-templates/new', element: <LazyDescriptionTemplateEditPage /> },
              { path: 'description-templates/:id', element: <LazyDescriptionTemplateEditPage /> },
              { path: '*', element: <NotFoundPage /> },
            ],
          },
        ],
      },
    ],
  },
];
