import { useParams } from 'react-router';
import { useProject } from '../../api/queries';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { NotFoundPage } from '../errors/NotFoundPage';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { projectPageLink } from '../projects/projectViewPreference';
import { ItemImportContent } from './ItemImportContent';

/**
 * 匯入項目頁（/projects/:projectKey/import）：要有 item.create；封存的專案不能匯入。
 * 入口是專案頁頁列「更多」選單的「匯入項目…」。
 */
export function ItemImportPage() {
  const { projectKey = '' } = useParams();
  const project = useProject(projectKey);
  if (project.isPending) {
    return (
      <StatusPage title="匯入項目">
        <LoadingState label="正在載入專案…" />
      </StatusPage>
    );
  }
  if (project.isLoadingError) {
    return <ProjectLoadError projectKey={projectKey} error={project.error} onRetry={() => { void project.refetch(); }} />;
  }
  if (project.data.project.archivedAt !== null) {
    return (
      <PageLayout variant="reading" bar={<PageBar title="匯入項目" crumbs={[{ label: projectKey, mono: true, to: projectPageLink(projectKey) }]} />}>
        <ArchivedNotice detail={project.data} />
      </PageLayout>
    );
  }
  if (!project.data.myPermissions.includes('item.create')) {
    const message = '你在這個專案的角色不能新增項目。需要的話，請專案的管理員調整你的角色。';
    return (
      <NotFoundPage code="403" title="沒有權限匯入項目" message={message} backTo={projectPageLink(projectKey)} backLabel="回到專案" />
    );
  }
  return <ItemImportContent key={projectKey} detail={project.data} />;
}
