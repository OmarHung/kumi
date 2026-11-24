import type { ReactNode } from 'react';
import { useParams } from 'react-router';
import { useProject } from '../../api/queries';
import type { ProjectDetail } from '../../api/types';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { ProjectDescriptionSection } from './ProjectDescriptionSection';

// 封存的專案，專案說明照樣能改，提示不能再說「不能修改」。
const ARCHIVED_EDITABLE = '這個專案已經封存，項目只能看；專案說明還是可以改。';

function ProjectInfoContent({ detail, notice }: { readonly detail: ProjectDetail; readonly notice: ReactNode }) {
  const projectKey = detail.project.key;
  const crumbs = [{ label: detail.project.name, to: useProjectPageLink(projectKey) }];
  // 封存的專案也能改說明：網站上線之後還在跑，主機照樣會換（後端 project.manage 在封存時還在）。
  const canEdit = detail.myPermissions.includes('project.manage');
  return (
    <PageLayout variant="reading" bar={<PageBar title="資訊" crumbs={crumbs} />}>
      {notice}
      <ArchivedNotice detail={detail} message={canEdit ? ARCHIVED_EDITABLE : undefined} />
      <ProjectDescriptionSection projectKey={projectKey} canEdit={canEdit} />
    </PageLayout>
  );
}

/** 資訊頁（/projects/:projectKey/info）：專案說明，網址、部署環境、主機這類同事要找的資訊。看得到專案的人都能看，改要 project.manage。 */
export function ProjectInfoPage() {
  const { projectKey = '' } = useParams();
  const project = useProject(projectKey);
  if (project.isPending) {
    return (
      <StatusPage title="資訊">
        <LoadingState label="正在載入資訊…" />
      </StatusPage>
    );
  }
  if (project.isLoadingError) {
    return <ProjectLoadError projectKey={projectKey} error={project.error} onRetry={() => { void project.refetch(); }} />;
  }
  const notice = <RefetchErrorNotice query={project} message="無法更新專案的最新資料，畫面上可能是舊的。" />;
  return <ProjectInfoContent key={projectKey} detail={project.data} notice={notice} />;
}
