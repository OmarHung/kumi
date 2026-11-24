import { useParams } from 'react-router';
import { useProject } from '../../api/queries';
import { LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { NotFoundPage } from '../errors/NotFoundPage';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { projectPageLink } from '../projects/projectViewPreference';
import { DeletedItemsContent } from './DeletedItemsContent';
import { canReadItems } from '../../domain/projectPermissions';

/**
 * 已刪除的項目頁（/projects/:projectKey/deleted）：看得到項目的人（item.read）都能開；
 * 「復原」要 item.delete，沒有就不畫（入口在專案頁的頁列，也只給有 item.delete 的人）。
 */
export function DeletedItemsPage() {
  const { projectKey = '' } = useParams();
  const project = useProject(projectKey);
  if (project.isPending) {
    return (
      <StatusPage title="已刪除的項目">
        <LoadingState label="正在載入已刪除的項目…" />
      </StatusPage>
    );
  }
  if (project.isLoadingError) {
    return <ProjectLoadError projectKey={projectKey} error={project.error} onRetry={() => { void project.refetch(); }} />;
  }
  if (!canReadItems(project.data.myPermissions)) {
    const message = '你在這個專案的角色不能看項目。需要的話，請專案的管理員調整你的角色。';
    return (
      <NotFoundPage code="403" title="沒有權限看這個專案的已刪除項目" message={message} backTo={projectPageLink(projectKey)} backLabel="回到專案" />
    );
  }
  const notice = <RefetchErrorNotice query={project} message="無法更新專案的最新資料，畫面上可能是舊的。" />;
  return <DeletedItemsContent key={projectKey} detail={project.data} notice={notice} />;
}
