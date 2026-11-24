import { Link, useParams } from 'react-router';
import { useChangeSets, useProject } from '../../api/queries';
import type { ChangeSet, ProjectDetail } from '../../api/types';
import { buttonClassName } from '../../components/Button/Button';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { EmptyState, ErrorState, LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { formatInstant } from '../../domain/time';
import { useIsCompact } from '../../lib/useMediaQuery';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { changeSetPagePath, projectSettingsPath } from '../work-items/itemLinks';
import { ChangeSetCompactList } from './ChangeSetCompactList';
import { NoPermission } from './ChangeSetErrors';
import { ChangeSetOriginBadge, ChangeSetStatusBadge } from './ChangeSetSections';
import styles from './ChangeSets.module.css';

interface ChangeSetRowProps {
  readonly projectKey: string;
  readonly changeSet: ChangeSet;
  /** 第一列：給 scripts/ui-audit.js 量第一筆內容離頂端多遠（docs/DESIGN.md 6.2）。 */
  readonly isFirst: boolean;
}

function ChangeSetRow({ projectKey, changeSet, isFirst }: ChangeSetRowProps) {
  const number = `#${String(changeSet.number)}`;
  return (
    <tr data-content-start={isFirst || undefined}>
      <th scope="row">
        <Link to={changeSetPagePath(projectKey, changeSet.number)} className={styles.number}>
          {number}
        </Link>
      </th>
      <td className={changeSet.summary === null ? tableStyles.muted : tableStyles.primaryCell}>{changeSet.summary ?? '沒有摘要'}</td>
      <td className={tableStyles.nowrap}>
        <ChangeSetStatusBadge status={changeSet.status} />
        {changeSet.appliedVersion !== null && `（第 ${String(changeSet.appliedVersion)} 版）`}
      </td>
      <td className={tableStyles.nowrap}>
        {changeSet.proposedBy.displayName}<ChangeSetOriginBadge origin={changeSet.origin} />
      </td>
      <td className={tableStyles.nowrap}>
        <time dateTime={changeSet.proposedAt}>{formatInstant(changeSet.proposedAt)}</time>
      </td>
    </tr>
  );
}

function ChangeSetTable({ projectKey, changeSets }: { readonly projectKey: string; readonly changeSets: readonly ChangeSet[] }) {
  // 窄螢幕換成一筆兩行的清單（docs/DESIGN.md 7.1），內容一樣；測試環境沒有 matchMedia，看到的是表格。
  const isCompact = useIsCompact();
  if (changeSets.length === 0) {
    const action = (
      <Link to={projectSettingsPath(projectKey)} className={buttonClassName('secondary', 'sm')}>
        到設定頁修改
      </Link>
    );
    return (
      <EmptyState title="還沒有變更集" action={action}>
        在設定頁修改狀態或欄位、按「預覽變更」，就會產生一份等待核准的變更集。
      </EmptyState>
    );
  }
  if (isCompact) {
    return <ChangeSetCompactList projectKey={projectKey} changeSets={changeSets} />;
  }
  return (
    <TableScroll label="變更集" layout="page">
      <table className={tableStyles.table}>
        <caption className="visually-hidden">變更集</caption>
        <thead>
          <tr>
            <th scope="col">編號</th>
            <th scope="col">摘要</th>
            <th scope="col">狀態</th>
            <th scope="col">提出的人</th>
            <th scope="col">提出時間</th>
          </tr>
        </thead>
        <tbody>
          {changeSets.map((changeSet, index) => (
            <ChangeSetRow key={changeSet.number} projectKey={projectKey} changeSet={changeSet} isFirst={index === 0} />
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}

function ChangeSetList({ project }: { readonly project: ProjectDetail }) {
  const projectKey = project.project.key;
  const changeSets = useChangeSets(projectKey);
  const actions = (
    <Link to={projectSettingsPath(projectKey)} className={buttonClassName('secondary')}>
      到設定頁修改
    </Link>
  );
  // 專案的連結照這個專案上次用的檢視開（和側欄、項目頁的麵包屑一樣）。
  const crumbs = [
    { label: project.project.name, to: useProjectPageLink(projectKey) },
    { label: '設定', to: projectSettingsPath(projectKey) },
  ];
  // 清單頁：說明文字不放頁列（docs/DESIGN.md 6.1）；沒有變更集時，空白狀態說明它們怎麼來。
  return (
    <PageLayout variant="list" bar={<PageBar title="變更集" crumbs={crumbs} actions={actions} />}>
      <ArchivedNotice detail={project} />
      <RefetchErrorNotice query={changeSets} message="無法更新變更集清單，畫面上可能是舊的。" />
      {changeSets.isPending && <LoadingState label="正在載入變更集…" />}
      {changeSets.isLoadingError && (
        <ErrorState error={changeSets.error} title="變更集載入失敗" onRetry={() => { void changeSets.refetch(); }} />
      )}
      {changeSets.data !== undefined && <ChangeSetTable projectKey={projectKey} changeSets={changeSets.data} />}
    </PageLayout>
  );
}

/** 變更集清單（/projects/:projectKey/changesets）：編號、摘要、狀態、提出的人、時間，連到預覽頁。 */
export function ChangeSetListPage() {
  const { projectKey = '' } = useParams();
  const project = useProject(projectKey);
  if (project.isPending) {
    return (
      <StatusPage title="變更集">
        <LoadingState label="正在載入變更集…" />
      </StatusPage>
    );
  }
  if (project.isLoadingError) {
    return <ProjectLoadError projectKey={projectKey} error={project.error} onRetry={() => { void project.refetch(); }} />;
  }
  if (!project.data.myPermissions.includes('blueprint.read')) {
    return <NoPermission projectKey={projectKey} />;
  }
  return <ChangeSetList key={projectKey} project={project.data} />;
}
