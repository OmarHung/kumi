import { useEffect, useRef } from 'react';
import { Link } from 'react-router';
import { useDescriptionTemplates } from '../../api/projectDescription';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { buttonClassName } from '../../components/Button/Button';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { ArrivalNotice, ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { useArrivalNotice } from '../../lib/useArrivalNotice';
import { useIsCompact } from '../../lib/useMediaQuery';
import { SettingsHubActions, useSettingsHubHeading } from '../settings-hub/SettingsHubContext';
import { DeleteTemplateDialog } from './DeleteTemplateDialog';
import { DescriptionTemplateCompactList, DescriptionTemplateTable } from './DescriptionTemplateList';
import { newTemplatePath } from './templatePaths';
import { useTemplateAdmin } from './useTemplateAdmin';
import styles from './DescriptionTemplates.module.css';

const SKELETON_ROWS = 3;

function TemplatesContent({ admin }: { readonly admin: ReturnType<typeof useTemplateAdmin> }) {
  const templates = useDescriptionTemplates();
  const isCompact = useIsCompact();
  if (templates.isPending) return <Skeleton label="正在載入說明格式…" rows={SKELETON_ROWS} />;
  if (templates.isLoadingError) {
    return (
      <ErrorState
        error={templates.error}
        title="說明格式載入失敗"
        onRetry={() => {
          void templates.refetch();
        }}
      />
    );
  }
  const handlers = { ...admin.handlers, isBusy: admin.isBusy };
  const View = isCompact ? DescriptionTemplateCompactList : DescriptionTemplateTable;
  return (
    <>
      <RefetchErrorNotice query={templates} message="無法更新說明格式，畫面上可能是舊的。" />
      <View templates={templates.data} handlers={handlers} />
    </>
  );
}

/**
 * 組織管理員管理說明格式：開始寫專案說明、開新專案時預先填進去的 Markdown 架構。
 * 讀取誰都可以（資訊頁要用），但管理只有擁有者與管理員；不是管理員打了網址，照人員頁說沒有權限（真正的檢查在後端）。
 */
export function DescriptionTemplatesPage() {
  const user = useCurrentUser();
  const headingRef = useSettingsHubHeading();
  const arrival = useArrivalNotice(headingRef);
  const noticeRef = useRef<HTMLDivElement>(null);
  const admin = useTemplateAdmin();
  // 刪除成功：按的那一列已經不見，焦點放到提示（和刪除項目之後一樣）。
  useEffect(() => {
    if (admin.focusRequest > 0) noticeRef.current?.focus();
  }, [admin.focusRequest]);
  if (!isOrganizationAdmin(user)) {
    return <NoPermission message="只有組織的擁有者與管理員可以管理說明格式。" />;
  }
  const createLink = (
    <Link to={newTemplatePath()} className={buttonClassName('primary')}>
      新增格式
    </Link>
  );
  return (
    <>
      <SettingsHubActions>{createLink}</SettingsHubActions>
      {/* 一直掛著（沒有訊息時看不見、不佔位置）：之後才填進來的訊息，報讀軟體才唸得到。 */}
      <ArrivalNotice message={admin.message === '' ? arrival : admin.message} noticeRef={noticeRef} />
      {admin.failure !== null && (
        <div className={styles.status}>
          <FormAlert message={admin.failure} />
        </div>
      )}
      <p className={styles.intro}>開始寫專案說明、開新專案時預先填進去的架構。改了不會影響已經寫好的說明。</p>
      <TemplatesContent admin={admin} />
      {admin.deleting !== null && (
        <DeleteTemplateDialog
          template={admin.deleting}
          isPending={admin.isDeleting}
          error={admin.deleteError}
          onConfirm={() => {
            if (admin.deleting !== null) admin.confirmDelete(admin.deleting);
          }}
          onCancel={admin.cancelDelete}
        />
      )}
    </>
  );
}
