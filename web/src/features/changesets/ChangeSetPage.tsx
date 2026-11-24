import { useParams } from 'react-router';
import { isApiError } from '../../api/http';
import { useChangeSet, useProject } from '../../api/queries';
import { ErrorState, LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { ChangeSetMissing, NoPermission } from './ChangeSetErrors';
import { ChangeSetView } from './ChangeSetView';

const HTTP_FORBIDDEN = 403;
const HTTP_NOT_FOUND = 404;
const NUMBER = /^[1-9]\d{0,15}$/;

/** 載入中、讀不到與載入好的分頁標題一致（ChangeSetView 的標題也是這個）。 */
function changeSetTitle(number: number): string {
  return `變更集 #${String(number)}`;
}

function ChangeSetLoader({ projectKey, number }: { readonly projectKey: string; readonly number: number }) {
  const project = useProject(projectKey);
  const canRead = project.data?.myPermissions.includes('blueprint.read') ?? false;
  const changeSet = useChangeSet(projectKey, number, canRead);
  if (project.isLoadingError) {
    return <ProjectLoadError projectKey={projectKey} error={project.error} onRetry={() => { void project.refetch(); }} />;
  }
  if (project.data !== undefined && !canRead) {
    return <NoPermission projectKey={projectKey} />;
  }
  if (changeSet.isLoadingError) {
    const status = isApiError(changeSet.error) ? changeSet.error.status : 0;
    if (status === HTTP_NOT_FOUND) return <ChangeSetMissing projectKey={projectKey} number={String(number)} />;
    if (status === HTTP_FORBIDDEN) return <NoPermission projectKey={projectKey} />;
    return (
      <StatusPage title={changeSetTitle(number)}>
        <ErrorState error={changeSet.error} title="變更集載入失敗" onRetry={() => { void changeSet.refetch(); }} />
      </StatusPage>
    );
  }
  if (project.data === undefined || changeSet.data === undefined) {
    return (
      <StatusPage title={changeSetTitle(number)}>
        <LoadingState label="正在載入變更集…" />
      </StatusPage>
    );
  }
  // 提示放在內容最上面固定的位置，出現或消失都不會讓 ChangeSetView 重新掛載。
  const notice = <RefetchErrorNotice query={changeSet} message="無法更新這份變更集的最新內容，畫面上可能是舊的。" />;
  return <ChangeSetView project={project.data} detail={changeSet.data} notice={notice} />;
}

/** 變更集的預覽頁（/projects/:projectKey/changesets/:number）：差異、對既有資料的影響、擋住的原因，與套用、放棄。 */
export function ChangeSetPage() {
  const { projectKey = '', number = '' } = useParams();
  if (!NUMBER.test(number)) {
    return <ChangeSetMissing projectKey={projectKey} number={number} />;
  }
  return <ChangeSetLoader key={`${projectKey}-${number}`} projectKey={projectKey} number={Number(number)} />;
}
