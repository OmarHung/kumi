import { useEmailSettings } from '../../api/emailSettings';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { isApiError } from '../../api/http';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { EmailSettingsForm } from './EmailSettingsForm';

const SKELETON_ROWS = 4;
const HTTP_FORBIDDEN = 403;
const NO_PERMISSION = '只有組織的擁有者與管理員可以設定寄信。';

function SettingsContent({ organizationName }: { readonly organizationName: string }) {
  const settings = useEmailSettings();
  if (settings.isPending) return <Skeleton label="正在載入寄信設定…" rows={SKELETON_ROWS} />;
  if (settings.isLoadingError && isApiError(settings.error) && settings.error.status === HTTP_FORBIDDEN) {
    return <NoPermission message={NO_PERMISSION} />;
  }
  // 只有第一次就讀不到才換掉整張表單；已經有設定（正在填的內容）時背景重抓失敗，表單照留著、上面說明。
  if (settings.isLoadingError) {
    return (
      <ErrorState
        error={settings.error}
        title="寄信設定載入失敗"
        onRetry={() => {
          void settings.refetch();
        }}
      />
    );
  }
  return (
    <>
      <RefetchErrorNotice query={settings} message="無法更新寄信設定，畫面上可能是舊的。" />
      <EmailSettingsForm settings={settings.data} organizationName={organizationName} />
    </>
  );
}

/**
 * 組織管理員設定 Email 通知怎麼寄：Gmail（只填地址與應用程式密碼）或其他 SMTP。
 * 不是管理員打了網址：照說明格式頁說沒有權限，也不去讀設定（真正的檢查在後端）。
 */
export function EmailSettingsPage() {
  const user = useCurrentUser();
  if (!isOrganizationAdmin(user)) return <NoPermission message={NO_PERMISSION} />;
  return <SettingsContent organizationName={user.organizationName} />;
}
