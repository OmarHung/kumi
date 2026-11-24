import type { ReactNode } from 'react';
import { useParams } from 'react-router';
import { useProject } from '../../api/queries';
import type { ProjectDetail } from '../../api/types';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { NotFoundPage } from '../errors/NotFoundPage';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { projectPageLink, useProjectPageLink } from '../projects/projectViewPreference';
import { CycleSection } from './CycleSection';
import { MilestoneSection } from './MilestoneSection';
import styles from './Planning.module.css';
import { canReadItems, readsAllItems } from '../../domain/projectPermissions';

function PlanningContent({ detail, notice }: { readonly detail: ProjectDetail; readonly notice: ReactNode }) {
  const projectKey = detail.project.key;
  // 只看得到自己項目的人（執行者）有 item.update 也不能改週期與里程碑：那是整個專案的事，後端一樣擋。
  const canEdit = detail.myPermissions.includes('item.update') && readsAllItems(detail.myPermissions);
  const crumbs = [{ label: detail.project.name, to: useProjectPageLink(projectKey) }];
  // 閱讀頁：說明放在標題下面、跟著內容捲走（docs/DESIGN.md 6.2）。
  return (
    <PageLayout variant="reading" bar={<PageBar title="規劃" crumbs={crumbs} />}>
      {notice}
      <ArchivedNotice detail={detail} />
      <div className={styles.page}>
        <p className={styles.intro}>週期是一段有開始日與結束日的時間，里程碑是有到期日的目標。項目可以排進週期、歸到里程碑，這裡看得到各自的進度。</p>
        <CycleSection projectKey={projectKey} canEdit={canEdit} />
        <MilestoneSection projectKey={projectKey} canEdit={canEdit} />
      </div>
    </PageLayout>
  );
}

/** 規劃頁（/projects/:projectKey/planning）：專案的週期與里程碑與它們的進度。看得到項目的人都能看，改要 item.update。 */
export function PlanningPage() {
  const { projectKey = '' } = useParams();
  const project = useProject(projectKey);
  if (project.isPending) {
    return (
      <StatusPage title="規劃">
        <LoadingState label="正在載入規劃…" />
      </StatusPage>
    );
  }
  if (project.isLoadingError) {
    return <ProjectLoadError projectKey={projectKey} error={project.error} onRetry={() => { void project.refetch(); }} />;
  }
  if (!canReadItems(project.data.myPermissions)) {
    const message = '你在這個專案的角色不能看項目。需要的話，請專案的管理員調整你的角色。';
    return <NotFoundPage code="403" title="沒有權限看這個專案的規劃" message={message} backTo={projectPageLink(projectKey)} backLabel="回到專案" />;
  }
  const notice = <RefetchErrorNotice query={project} message="無法更新專案的最新資料，畫面上可能是舊的。" />;
  return <PlanningContent key={projectKey} detail={project.data} notice={notice} />;
}
