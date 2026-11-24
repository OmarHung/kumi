import { useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useAiOptions } from '../../api/assistant';
import { useProject } from '../../api/queries';
import type { ProjectDetail } from '../../api/types';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { PageBar } from '../../components/PageBar/PageBar';
import { LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { projectRoleLabel } from '../../domain/labels';
import { useArrivalNotice } from '../../lib/useArrivalNotice';
import { UndoableArrival } from '../bulk-edit/UndoableArrival';
import { useIsCompact } from '../../lib/useMediaQuery';
import { usePlanning } from '../item-table/usePlanning';
import { MembersDialog } from '../members/MembersDialog';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { projectAssistantPath, projectDeletedPath, projectImportPath, projectInfoPath, projectPlanningPath, projectSettingsPath } from '../work-items/itemLinks';
import { ArchivedNotice } from './ArchivedNotice';
import { ProjectLoadError } from './ProjectLoadError';
import { projectMoreItems } from './projectMenu';
import { useProjectMenuActions } from './useProjectMenuActions';
import { ProjectWorkspace } from './ProjectWorkspace';
import styles from './ProjectPage.module.css';

/**
 * 專案頁的外殼：載入專案（與週期、里程碑的清單）、處理找不到，內容交給 ProjectWorkspace。
 * 週期與里程碑的清單一起等：專案有沒有這兩樣東西決定工具列的欄位與表格的欄，載入後才知道會讓版面跳動。
 * 清單讀不到不擋（isPending 在失敗後就是 false），由工作區說明（PlanningLoadError）。
 * 只有第一次就讀不到才換成錯誤畫面；已經有資料時重抓失敗，留著表格與編輯中的格子，只多一行提示。
 */
export function ProjectPage() {
  const { projectKey = '' } = useParams();
  const project = useProject(projectKey);
  const planning = usePlanning(projectKey);

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
  if (project.isPending || planning.isPending) {
    return (
      <StatusPage title={projectKey}>
        <LoadingState label="正在載入專案…" />
      </StatusPage>
    );
  }

  // 提示放在內容最上面固定的位置，出現或消失都不會讓 LoadedProject 重新掛載。
  const notice = <RefetchErrorNotice query={project} message="無法更新專案的最新資料，畫面上可能是舊的。" />;
  return <LoadedProject key={projectKey} detail={project.data} notice={notice} />;
}

interface ProjectActionsProps {
  readonly detail: ProjectDetail;
  readonly onOpenMembers: () => void;
  readonly onRename: () => void;
  readonly onArchive: () => void;
  readonly onUnarchive: () => void;
}

/** 選單項目的動作：導頁、改名、封存、取消封存。 */
function useMoreItems(detail: ProjectDetail, { onRename, onArchive, onUnarchive }: ProjectActionsProps): readonly MenuItem[] {
  const navigate = useNavigate();
  const { key } = detail.project;
  return projectMoreItems(
    detail,
    { import: projectImportPath(key), deleted: projectDeletedPath(key) },
    { goTo: (path) => void navigate(path), onRename, onArchive, onUnarchive },
  );
}

/** 組織開了 AI、而且看得到藍圖（助理讀的是設定）才有「AI 助理」的入口；選項讀不到時當作沒有，頁列不因此壞掉。 */
function useCanUseAssistant(detail: ProjectDetail): boolean {
  const options = useAiOptions();
  return options.data?.available === true && detail.myPermissions.includes('blueprint.read');
}

/**
 * 窄螢幕（48rem 以下）：「資訊」「設定」「規劃」「成員」與「更多」的項目收進一顆「專案的動作」，用分隔線分組；
 * 選了就導過去（成員是打開對話框，改名是打開對話框）。
 */
function CompactProjectActions(props: ProjectActionsProps) {
  const { detail, onOpenMembers } = props;
  const navigate = useNavigate();
  const { key } = detail.project;
  const more = useMoreItems(detail, props);
  const canReadSettings = detail.myPermissions.includes('blueprint.read');
  const canUseAssistant = useCanUseAssistant(detail);
  const items: readonly MenuItem[] = [
    ...(canUseAssistant
      ? [{ key: 'assistant', label: 'AI 助理', icon: 'sparkles', onSelect: () => void navigate(projectAssistantPath(key)) } satisfies MenuItem]
      : []),
    // 資訊（專案說明）：看得到專案的人都有。
    { key: 'info', label: '資訊', icon: 'info', onSelect: () => void navigate(projectInfoPath(key)) },
    ...(canReadSettings
      ? [{ key: 'settings', label: '設定', icon: 'settings', onSelect: () => void navigate(projectSettingsPath(key)) } satisfies MenuItem]
      : []),
    { key: 'planning', label: '規劃', icon: 'calendar', onSelect: () => void navigate(projectPlanningPath(key)) },
    { key: 'members', label: '成員', icon: 'members', onSelect: onOpenMembers },
    ...more,
  ];
  return (
    <Menu
      menuLabel="專案的動作"
      align="end"
      items={items}
      renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label="專案的動作" />}
    />
  );
}

/** 寬螢幕頁列最後的「更多」：匯入、已刪除、改名、封存；沒有任何一項（沒有對應的權限）時不畫按鈕。 */
function MoreActions(props: ProjectActionsProps) {
  const items = useMoreItems(props.detail, props);
  if (items.length === 0) return null;
  return (
    <Menu
      menuLabel="專案的更多動作"
      align="end"
      items={items}
      renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label="專案的更多動作" />}
    />
  );
}

/**
 * 頁列右邊：我的角色（窄螢幕只給報讀軟體）、「資訊」、「設定」（看得到藍圖的人才有）、「規劃」、「成員」、「更多」。
 * 窄螢幕的動作收進一顆「專案的動作」：390px 寬時三顆按鈕把專案名稱擠到只剩「Scru…」、代號只剩一個字。
 */
function ProjectActions(props: ProjectActionsProps) {
  const isCompact = useIsCompact();
  const { detail, onOpenMembers } = props;
  const { project: summary, blueprint } = detail;
  // 設定是另一頁（網址記得住、可以分享）；只有看得到藍圖的人才有入口，真正的檢查在後端。
  const canReadSettings = detail.myPermissions.includes('blueprint.read');
  const canUseAssistant = useCanUseAssistant(detail);
  return (
    <>
      <span className={styles.role}>{`我的角色：${projectRoleLabel(summary.myRole, blueprint.roles)}`}</span>
      {isCompact ? (
        <CompactProjectActions {...props} />
      ) : (
        <>
          {canUseAssistant && (
            <Link to={projectAssistantPath(summary.key)} className={buttonClassName('secondary')}>
              <Icon name="sparkles" />
              AI 助理
            </Link>
          )}
          <Link to={projectInfoPath(summary.key)} className={buttonClassName('secondary')}>
            資訊
          </Link>
          {canReadSettings && (
            <Link to={projectSettingsPath(summary.key)} className={buttonClassName('secondary')}>
              設定
            </Link>
          )}
          {/* 規劃（週期與里程碑）：看得到項目的人都能看，改的入口在頁面裡依 item.update 決定。 */}
          <Link to={projectPlanningPath(summary.key)} className={buttonClassName('secondary')}>
            規劃
          </Link>
          <Button onClick={onOpenMembers}>成員</Button>
          <MoreActions {...props} />
        </>
      )}
    </>
  );
}

/**
 * 以專案代號為 key：在側欄換專案時，表格的編輯狀態、搜尋框與對話框都重新開始。
 * 刪除項目之後回到這裡時（navigate 的 state），焦點到頁面的標題、提示填進一直掛著的 status。
 */
function LoadedProject({ detail, notice }: { readonly detail: ProjectDetail; readonly notice: ReactNode }) {
  const [isMembersOpen, setIsMembersOpen] = useState(false);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const arrival = useArrivalNotice(headingRef);
  const summary = detail.project;
  const lifecycle = useProjectMenuActions(summary);
  const openMembers = () => {
    setIsMembersOpen(true);
  };
  const bar = (
    <PageBar
      crumbs={[{ label: summary.key, mono: true }]}
      title={summary.name}
      headingRef={headingRef}
      actions={
        <ProjectActions
          detail={detail}
          onOpenMembers={openMembers}
          onRename={lifecycle.onRename}
          onArchive={lifecycle.onArchive}
          onUnarchive={lifecycle.onUnarchive}
        />
      }
    />
  );
  const notices = (
    <>
      {notice}
      <ArchivedNotice detail={detail} />
      {lifecycle.errors}
      <UndoableArrival message={arrival} />
    </>
  );
  return (
    <>
      <ProjectWorkspace detail={detail} bar={bar} notices={notices} onOpenMembers={openMembers} />
      {lifecycle.dialog}
      {isMembersOpen && (
        <MembersDialog
          projectKey={summary.key}
          onClose={() => {
            setIsMembersOpen(false);
          }}
        />
      )}
    </>
  );
}
