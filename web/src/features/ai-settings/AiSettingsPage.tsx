import { useAiSettings } from '../../api/aiSettings';
import { isApiError } from '../../api/http';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { AiSettingsForm } from './AiSettingsForm';

const SKELETON_ROWS = 4;
const HTTP_FORBIDDEN = 403;
const NO_PERMISSION = '只有組織的擁有者與管理員可以設定 AI。';

function SettingsContent() {
  const settings = useAiSettings();
  if (settings.isPending) return <Skeleton label="正在載入 AI 設定…" rows={SKELETON_ROWS} />;
  if (settings.isLoadingError && isApiError(settings.error) && settings.error.status === HTTP_FORBIDDEN) {
    return <NoPermission message={NO_PERMISSION} />;
  }
  // 只有第一次就讀不到才換掉整張表單；已經有設定（正在填的內容）時背景重抓失敗，表單照留著、上面說明。
  if (settings.isLoadingError) {
    return (
      <ErrorState
        error={settings.error}
        title="AI 設定載入失敗"
        onRetry={() => {
          void settings.refetch();
        }}
      />
    );
  }
  return (
    <>
      <RefetchErrorNotice query={settings} message="無法更新 AI 設定，畫面上可能是舊的。" />
      <AiSettingsForm settings={settings.data} />
    </>
  );
}

/**
 * 組織管理員接上 AI 供應商、開放模型、設定預設。
 * 不是管理員打了網址：說沒有權限，也不去讀設定（真正的檢查在後端）。
 */
export function AiSettingsPage() {
  const user = useCurrentUser();
  return isOrganizationAdmin(user) ? <SettingsContent /> : <NoPermission message={NO_PERMISSION} />;
}
