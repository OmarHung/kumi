import { useQuery } from '@tanstack/react-query';
import { isApiError } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import { fetchStorageUsage, type StorageAreaUsage, type StorageUsage } from '../../api/storage';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { formatBytes, formatLimit } from './formatBytes';
import styles from './StorageUsage.module.css';

const SKELETON_ROWS = 4;
const HTTP_FORBIDDEN = 403;
const NO_PERMISSION = '只有組織的擁有者與管理員可以看儲存空間的用量。';
const AREA_TITLES: Readonly<Record<string, string>> = {
  item_attachments: '項目的附件',
  knowledge_attachments: '知識庫的附件',
  assistant_attachments: 'AI 助理的附件',
};

function OrganizationSection({ usage }: { readonly usage: StorageUsage }) {
  const limit = usage.organizationLimitBytes;
  // 等於上限時伺服器就擋新的上傳，所以「達到」也要警告。
  const isFull = limit !== null && usage.totalBytes >= limit;
  const isOver = limit !== null && usage.totalBytes > limit;
  return (
    <SettingsSection title="組織的附件" description="項目、知識庫與 AI 助理的附件加起來，刪除的項目與垃圾桶裡的頁面也照算。" isContentStart>
      <div className={styles.stack}>
        <p className={styles.figure}>
          {limit === null ? `已用 ${formatBytes(usage.totalBytes)}，不限制` : `已用 ${formatBytes(usage.totalBytes)}／${formatLimit(limit)}`}
        </p>
        {limit !== null && (
          <progress
            className={isFull ? `${styles.track} ${styles.trackOver}` : styles.track}
            aria-label="組織的附件用量"
            aria-valuetext={`已用 ${formatBytes(usage.totalBytes)}，上限 ${formatLimit(limit)}`}
            max={limit}
            value={Math.min(usage.totalBytes, limit)}
          />
        )}
        {isFull && (
          <Notice tone="warning" role="none">
            {isOver
              ? `已經超過上限 ${formatBytes(usage.totalBytes - limit)}：新的上傳會被擋下，下載與刪除照常。請刪掉不用的附件，或調高上限。`
              : '已經達到上限：新的上傳會被擋下，下載與刪除照常。請刪掉不用的附件，或調高上限。'}
          </Notice>
        )}
      </div>
    </SettingsSection>
  );
}

function AreaSection({ area }: { readonly area: StorageAreaUsage }) {
  return (
    <SettingsSection title={AREA_TITLES[area.area] ?? area.area}>
      <p className={styles.figure}>
        {formatBytes(area.bytes)}
        <span className={styles.note}>{`・${area.files.toLocaleString('en-US')} 個檔案`}</span>
      </p>
    </SettingsSection>
  );
}

function DailySection({ usage }: { readonly usage: StorageUsage }) {
  const limit = usage.personDailyLimitBytes;
  return (
    <SettingsSection title="每人每日的上傳量" description="每個人過去 24 小時最多能上傳的量；歷史匯入只算組織的總量。">
      <div className={styles.stack}>
        <p className={styles.figure}>{limit === null ? '不限制' : formatLimit(limit)}</p>
        <p className={styles.note}>上限在部署的設定（Attachments:Quota:*）調整。</p>
      </div>
    </SettingsSection>
  );
}

function UsageContent() {
  const usage = useQuery({ queryKey: queryKeys.storageUsage, queryFn: ({ signal }) => fetchStorageUsage(signal) });
  if (usage.isPending) return <Skeleton label="正在載入儲存空間的用量…" rows={SKELETON_ROWS} />;
  if (usage.isLoadingError && isApiError(usage.error) && usage.error.status === HTTP_FORBIDDEN) {
    return <NoPermission message={NO_PERMISSION} />;
  }
  if (usage.isLoadingError) {
    return (
      <ErrorState
        error={usage.error}
        title="用量載入失敗"
        onRetry={() => {
          void usage.refetch();
        }}
      />
    );
  }
  return (
    <>
      <RefetchErrorNotice query={usage} message="無法更新用量，畫面上可能是舊的。" />
      <OrganizationSection usage={usage.data} />
      {usage.data.areas.map((area) => (
        <AreaSection key={area.area} area={area} />
      ))}
      <DailySection usage={usage.data} />
    </>
  );
}

/**
 * 儲存空間（設定中心，只有組織管理員）：附件用了多少、上限多少。只讀：上限在部署的設定，網頁不能改。
 * 不是管理員打了網址：說沒有權限，也不去讀用量（真正的檢查在後端）。
 */
export function StorageUsagePage() {
  const user = useCurrentUser();
  if (!isOrganizationAdmin(user)) return <NoPermission message={NO_PERMISSION} />;
  return <UsageContent />;
}
