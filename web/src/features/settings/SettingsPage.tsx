import { Navigate, useLocation, useParams } from 'react-router';
import { useProject } from '../../api/queries';
import { LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { NotFoundPage } from '../errors/NotFoundPage';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { projectPageLink } from '../projects/projectViewPreference';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import {
  DEFAULT_SETTINGS_SECTION,
  settingsPlaceOf,
  settingsSectionPath,
  type SettingsSection,
} from '../work-items/itemLinks';
import { SettingsEditor } from './SettingsEditor';

function SettingsContent({ projectKey, section }: { readonly projectKey: string; readonly section: SettingsSection }) {
  const project = useProject(projectKey);
  if (project.isPending) {
    return (
      <StatusPage title="設定">
        <LoadingState label="正在載入設定…" />
      </StatusPage>
    );
  }
  if (project.isLoadingError) {
    const retry = () => {
      void project.refetch();
    };
    return <ProjectLoadError projectKey={projectKey} error={project.error} onRetry={retry} />;
  }
  if (!project.data.myPermissions.includes('blueprint.read')) {
    const message = '你在這個專案的角色不能看設定。需要的話，請專案的管理員調整你的角色。';
    return (
      <NotFoundPage code="403" title="沒有權限看這個專案的設定" message={message} backTo={projectPageLink(projectKey)} backLabel="回到專案" />
    );
  }
  // 以專案代號為 key：在側欄換專案時草稿重新開始，不會把別的專案的修改帶過去。
  // 提示放在內容最上面固定的位置，出現或消失都不會讓 SettingsEditor 重新掛載（草稿不會不見）。
  const notice = <RefetchErrorNotice query={project} message="無法更新專案的最新設定，畫面上可能是舊的。" />;
  return <SettingsEditor key={projectKey} detail={project.data} section={section} notice={notice} />;
}

/**
 * 專案的設定頁（/projects/:projectKey/settings/<區塊>）：載入專案、處理找不到與沒有權限，內容交給 SettingsEditor。
 * 這是外層的版面路由：區塊換了，這個元素與草稿都還在。設定頁本身（沒有區塊）轉到預設的區塊，換頁時帶的提示（state）跟著帶過去；
 * 轉過去的同時照樣畫著預設的區塊，從區塊連到設定頁本身時 SettingsEditor 不會被換掉。
 * 沒有 blueprint.read 的人看不到入口；直接打網址時說明沒有權限，也不去讀變更集（後端會回 403）。
 */
export function SettingsPage() {
  const { projectKey = '' } = useParams();
  const location = useLocation();
  const requested = settingsPlaceOf(location.pathname)?.section ?? null;
  return (
    <>
      {requested === null && (
        <Navigate to={settingsSectionPath(projectKey, DEFAULT_SETTINGS_SECTION)} replace state={location.state as unknown} />
      )}
      <SettingsContent projectKey={projectKey} section={requested ?? DEFAULT_SETTINGS_SECTION} />
    </>
  );
}
