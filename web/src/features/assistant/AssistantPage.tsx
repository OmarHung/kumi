import type { ReactNode } from 'react';
import { Link, useParams } from 'react-router';
import { useAiOptions } from '../../api/assistant';
import { useProject } from '../../api/queries';
import type { ProjectDetail } from '../../api/types';
import { buttonClassName } from '../../components/Button/Button';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { EmptyState, ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { projectPagePath } from '../work-items/itemLinks';
import { AssistantWorkspace } from './AssistantWorkspace';

const TITLE = 'AI 助理';

function Reading({ children }: { readonly children: ReactNode }) {
  return (
    <PageLayout variant="reading" bar={<PageBar title={TITLE} />}>
      {children}
    </PageLayout>
  );
}

type ProjectQuery = ReturnType<typeof useProject>;
type OptionsQuery = ReturnType<typeof useAiOptions>;

/** 還不能進工作區的原因：沒有權限、組織沒開 AI、選項讀不到。可以進了是 null。 */
function blockedView(projectKey: string, project: ProjectDetail, options: OptionsQuery): ReactNode {
  if (options.isLoadingError) {
    return (
      <ErrorState
        error={options.error}
        title="AI 助理載入失敗"
        onRetry={() => {
          void options.refetch();
        }}
      />
    );
  }
  if (!project.myPermissions.includes('blueprint.read')) {
    return <NoPermission message="你沒有讀取這個專案設定的權限，不能使用 AI 助理。" />;
  }
  if (options.data?.available !== true) {
    const back = (
      <Link to={projectPagePath(projectKey)} className={buttonClassName('secondary')}>
        回到專案
      </Link>
    );
    return (
      <EmptyState title="AI 助理還不能用" action={back}>
        組織還沒開放可以用的 AI。請組織管理員到「AI 設定」開放組織的 API 金鑰，或允許成員使用自己的 AI 帳號；也可以請組織擁有者設定主機 AI 代理。
      </EmptyState>
    );
  }
  return null;
}

function Ready({
  projectKey,
  project,
  options,
}: {
  readonly projectKey: string;
  readonly project: ProjectQuery;
  readonly options: OptionsQuery;
}) {
  const blocked = project.data === undefined ? null : blockedView(projectKey, project.data, options);
  if (blocked !== null) return <Reading>{blocked}</Reading>;
  if (project.data === undefined || options.data === undefined) return null;
  return <AssistantWorkspace key={projectKey} detail={project.data} options={options.data} />;
}

/**
 * 專案的 AI 助理（/projects/:projectKey/assistant 與 …/assistant/:conversationId，同一個路由、對話編號可有可無）。
 * 組織沒開 AI、沒有 blueprint.read 時說明原因（真正的檢查在後端）。
 */
export function AssistantPage() {
  const { projectKey = '' } = useParams();
  const project = useProject(projectKey);
  const options = useAiOptions();
  if (project.isPending || options.isPending) {
    return (
      <StatusPage title={TITLE}>
        <LoadingState label="正在載入 AI 助理…" />
      </StatusPage>
    );
  }
  if (project.isLoadingError) {
    return (
      <ProjectLoadError
        projectKey={projectKey}
        error={project.error}
        onRetry={() => {
          void project.refetch();
        }}
      />
    );
  }
  return <Ready projectKey={projectKey} project={project} options={options} />;
}
