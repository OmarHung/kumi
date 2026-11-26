import { lazy, Suspense } from 'react';
import { useParams } from 'react-router';
import { LoadingState } from '../components/StatusViews/StatusViews';
import { StatusPage } from '../components/StatusPage/StatusPage';

// 設定與變更集只有管理專案設定的人會打開，拆成另一個檔案延後下載，其他頁面不必先載入它們。
// 設定頁的四個區塊都在 SettingsPage 這一個檔案區塊裡（區塊的路由沒有自己的元素），切換區塊不會再下載。
// 下載中也照樣有頁列（StatusPage），外框不會等檔案到了才出現；分頁標題和載入好之後的一樣。
const SettingsPage = lazy(() =>
  import('../features/settings/SettingsPage').then((module) => ({ default: module.SettingsPage })),
);
const ChangeSetPage = lazy(() =>
  import('../features/changesets/ChangeSetPage').then((module) => ({ default: module.ChangeSetPage })),
);
// 收件匣也延後下載：側欄的未讀數是主程式檔裡的小東西，整頁（清單、列、標成已讀）打開時才下載。
const InboxPage = lazy(() => import('../features/inbox/InboxPage').then((module) => ({ default: module.InboxPage })));
const ChangeSetListPage = lazy(() =>
  import('../features/changesets/ChangeSetListPage').then((module) => ({ default: module.ChangeSetListPage })),
);

// 規劃頁（週期與里程碑）也只有規劃的人偶爾打開，和設定一樣延後下載。
const PlanningPage = lazy(() =>
  import('../features/planning/PlanningPage').then((module) => ({ default: module.PlanningPage })),
);

// 資訊頁（專案說明）偶爾才打開，和規劃一樣延後下載。
const ProjectInfoPage = lazy(() =>
  import('../features/project-info/ProjectInfoPage').then((module) => ({ default: module.ProjectInfoPage })),
);

// 說明格式的編輯頁（組織管理員偶爾才改）延後下載；清單頁在下面的設定中心裡。
const DescriptionTemplateEditPage = lazy(() =>
  import('../features/description-templates/DescriptionTemplateEditPage').then((module) => ({ default: module.DescriptionTemplateEditPage })),
);

// 專案的 AI 助理（對話、串流、輸入框）只有要用的人打開，也延後下載。
const OrganizationAssistantPage = lazy(() =>
  import('../features/assistant/OrganizationAssistantPage').then((module) => ({ default: module.OrganizationAssistantPage })),
);
const AssistantPage = lazy(() =>
  import('../features/assistant/AssistantPage').then((module) => ({ default: module.AssistantPage })),
);

// 已刪除的項目頁只有要復原的人偶爾打開，也延後下載。
const DeletedItemsPage = lazy(() =>
  import('../features/deleted-items/DeletedItemsPage').then((module) => ({ default: module.DeletedItemsPage })),
);

// 匯入項目頁（PM-4b）只有要搬資料進來的人偶爾打開，也延後下載。
const ItemImportPage = lazy(() =>
  import('../features/item-import/ItemImportPage').then((module) => ({ default: module.ItemImportPage })),
);

// 知識庫（空間清單、空間與頁面、垃圾桶、空間設定）整組延後下載：有人打開才需要編輯器與頁面樹。
const KnowledgeHomePage = lazy(() =>
  import('../features/knowledge/KnowledgeHomePage').then((module) => ({ default: module.KnowledgeHomePage })),
);
const KnowledgeSpacePage = lazy(() =>
  import('../features/knowledge/space/KnowledgeSpacePage').then((module) => ({ default: module.KnowledgeSpacePage })),
);
const KnowledgeTrashPage = lazy(() =>
  import('../features/knowledge/trash/KnowledgeTrashPage').then((module) => ({ default: module.KnowledgeTrashPage })),
);
const KnowledgeSpaceSettingsPage = lazy(() =>
  import('../features/knowledge/settings/KnowledgeSpaceSettingsPage').then((module) => ({ default: module.KnowledgeSpaceSettingsPage })),
);

export function LazyKnowledgeHomePage() {
  return (
    <Suspense fallback={<StatusPage title="知識庫"><LoadingState label="正在載入知識庫…" /></StatusPage>}>
      <KnowledgeHomePage />
    </Suspense>
  );
}

export function LazyKnowledgeSpacePage() {
  return (
    <Suspense fallback={<StatusPage title="知識庫"><LoadingState label="正在載入知識庫…" /></StatusPage>}>
      <KnowledgeSpacePage />
    </Suspense>
  );
}

export function LazyKnowledgeTrashPage() {
  return (
    <Suspense fallback={<StatusPage title="垃圾桶"><LoadingState label="正在載入垃圾桶…" /></StatusPage>}>
      <KnowledgeTrashPage />
    </Suspense>
  );
}

export function LazyKnowledgeSpaceSettingsPage() {
  return (
    <Suspense fallback={<StatusPage title="空間設定"><LoadingState label="正在載入空間設定…" /></StatusPage>}>
      <KnowledgeSpaceSettingsPage />
    </Suspense>
  );
}

export function LazyItemImportPage() {
  return (
    <Suspense fallback={<StatusPage title="匯入項目"><LoadingState label="正在載入匯入項目…" /></StatusPage>}>
      <ItemImportPage />
    </Suspense>
  );
}

export function LazyDeletedItemsPage() {
  return (
    <Suspense fallback={<StatusPage title="已刪除的項目"><LoadingState label="正在載入已刪除的項目…" /></StatusPage>}>
      <DeletedItemsPage />
    </Suspense>
  );
}

export function LazyPlanningPage() {
  return (
    <Suspense fallback={<StatusPage title="規劃"><LoadingState label="正在載入規劃…" /></StatusPage>}>
      <PlanningPage />
    </Suspense>
  );
}

export function LazyOrganizationAssistantPage() {
  return (
    <Suspense fallback={<StatusPage title="AI 助理"><LoadingState label="正在載入 AI 助理…" /></StatusPage>}>
      <OrganizationAssistantPage />
    </Suspense>
  );
}

export function LazyAssistantPage() {
  return (
    <Suspense fallback={<StatusPage title="AI 助理"><LoadingState label="正在載入 AI 助理…" /></StatusPage>}>
      <AssistantPage />
    </Suspense>
  );
}

export function LazyProjectInfoPage() {
  return (
    <Suspense fallback={<StatusPage title="資訊"><LoadingState label="正在載入資訊…" /></StatusPage>}>
      <ProjectInfoPage />
    </Suspense>
  );
}

export function LazySettingsPage() {
  return (
    <Suspense fallback={<StatusPage title="設定"><LoadingState label="正在載入設定…" /></StatusPage>}>
      <SettingsPage />
    </Suspense>
  );
}

export function LazyChangeSetPage() {
  const { number = '' } = useParams();
  return (
    <Suspense fallback={<StatusPage title={`變更集 #${number}`}><LoadingState label="正在載入變更集…" /></StatusPage>}>
      <ChangeSetPage />
    </Suspense>
  );
}

export function LazyChangeSetListPage() {
  return (
    <Suspense fallback={<StatusPage title="變更集"><LoadingState label="正在載入變更集…" /></StatusPage>}>
      <ChangeSetListPage />
    </Suspense>
  );
}


export function LazyInboxPage() {
  return (
    <Suspense fallback={<StatusPage title="收件匣"><LoadingState label="正在載入收件匣…" /></StatusPage>}>
      <InboxPage />
    </Suspense>
  );
}




export function LazyDescriptionTemplateEditPage() {
  return (
    <Suspense fallback={<StatusPage title="說明格式"><LoadingState label="正在載入說明格式…" /></StatusPage>}>
      <DescriptionTemplateEditPage />
    </Suspense>
  );
}

/*
 * 設定中心的各頁（features/settings-hub）：頁列、次導覽與下載中的「載入中」都在主程式檔的 SettingsHubLayout，
 * 這裡只延後下載內容。每個人偶爾才打開（改密碼、接 AI 帳號、組織管理員改寄信與 AI），不必和主程式一起下載。
 */
export const LazyAccountPage = lazy(() =>
  import('../features/account/AccountPage').then((module) => ({ default: module.AccountPage })),
);
export const LazyNotificationSettingsPage = lazy(() =>
  import('../features/account/NotificationSettingsPage').then((module) => ({ default: module.NotificationSettingsPage })),
);
export const LazyAiAccountsPage = lazy(() =>
  import('../features/account/AiAccountsPage').then((module) => ({ default: module.AiAccountsPage })),
);
export const LazyIntegrationsPage = lazy(() =>
  import('../features/account/IntegrationsPage').then((module) => ({ default: module.IntegrationsPage })),
);
export const LazyDescriptionTemplatesPage = lazy(() =>
  import('../features/description-templates/DescriptionTemplatesPage').then((module) => ({ default: module.DescriptionTemplatesPage })),
);
export const LazyEmailSettingsPage = lazy(() =>
  import('../features/email-settings/EmailSettingsPage').then((module) => ({ default: module.EmailSettingsPage })),
);
export const LazyStorageUsagePage = lazy(() =>
  import('../features/storage-usage/StorageUsagePage').then((module) => ({ default: module.StorageUsagePage })),
);
export const LazyAiSettingsPage = lazy(() =>
  import('../features/ai-settings/AiSettingsPage').then((module) => ({ default: module.AiSettingsPage })),
);
export const LazyHostAgentsPage = lazy(() =>
  import('../features/host-agents/HostAgentsPage').then((module) => ({ default: module.HostAgentsPage })),
);
