import { Link } from 'react-router';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { itemPagePath } from '../work-items/itemLinks';
import type { RestoreNotice } from './useRestoreDeletedItem';
import styles from './DeletedItems.module.css';

interface RestoreNoticesProps {
  readonly projectKey: string;
  readonly notice: RestoreNotice | null;
  /** 先復原的上層在這一頁：它的編號（捲過去、把焦點放到它的「復原」）；不在這一頁是 undefined。 */
  readonly onGoToParent: (parentKey: string) => void;
  readonly isParentListed: (parentKey: string) => boolean;
  readonly onDismiss: () => void;
}

function FailureNotice({ notice, onGoToParent, isParentListed, onDismiss }: Omit<RestoreNoticesProps, 'projectKey'>) {
  if (notice === null || notice.kind === 'restored') return null;
  if (notice.kind === 'error') {
    return (
      <Notice tone="danger" onDismiss={onDismiss}>
        {notice.message}
      </Notice>
    );
  }
  const { parentKey } = notice;
  if (parentKey === null) {
    return (
      <Notice tone="danger" onDismiss={onDismiss}>
        {notice.message}
      </Notice>
    );
  }
  const parent = isParentListed(parentKey) ? (
    <Button
      size="sm"
      variant="ghost"
      className={styles.parentLink}
      onClick={() => {
        onGoToParent(parentKey);
      }}
    >
      {parentKey}
    </Button>
  ) : (
    parentKey
  );
  return (
    <Notice tone="danger" onDismiss={onDismiss}>
      <span>
        {notice.key} 的上層 {parentKey} 也已刪除：先復原 {parent}。
      </span>
    </Notice>
  );
}

/**
 * 頁面上方的提示。「已復原」那一個一直掛著（沒有內容時看不見），提示晚一點才填進來時報讀軟體才唸得到（和 ArrivalNotice 一樣）；
 * 失敗的是錯誤（alert），出現時才掛上。
 */
export function RestoreNotices({ projectKey, notice, ...rest }: RestoreNoticesProps) {
  const restored = notice?.kind === 'restored' ? notice : null;
  const open =
    restored === null ? undefined : (
      <Link to={itemPagePath(projectKey, restored.key)} className={buttonClassName('secondary', 'sm')}>
        開啟
      </Link>
    );
  return (
    <>
      <Notice tone="success" role="status" actions={open} onDismiss={restored === null ? undefined : rest.onDismiss}>
        {restored === null ? '' : `已復原 ${restored.key}「${restored.title}」。`}
      </Notice>
      <FailureNotice notice={notice} {...rest} />
    </>
  );
}
