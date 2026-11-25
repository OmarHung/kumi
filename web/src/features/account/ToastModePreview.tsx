import type { Notification } from '../../api/types';
import { CountBadge } from '../../components/CountBadge/CountBadge';
import { Icon, type IconName } from '../../components/Icon/Icon';
import { ToastPreviewCard } from '../notification-toasts/ToastContent';
import type { ToastMode } from '../notification-toasts/toastMode';
import styles from './ToastModePreview.module.css';

/** 示意用的通知（假的人與項目），和真的提示走同一個內容元件。 */
const SAMPLE: Notification = {
  notificationId: 'preview-toast',
  kind: 'mentioned',
  projectKey: 'EXA',
  itemKey: 'EXA-12',
  itemTitle: '首頁改版',
  actor: { personId: 'preview-person', displayName: '範例成員' },
  fromStateName: null,
  toStateName: null,
  excerpt: '設計稿更新了，請看一下第二頁的按鈕位置。',
  commentId: null,
  edited: false,
  createdAt: '2026-11-24T08:00:00Z',
  readAt: null,
};

const UNREAD_SAMPLE = 3;

/** 整張圖是一張圖片（role="img"）：裡面的字只是畫面，報讀軟體唸這一句，說明這個方式會看到什麼。 */
const DESCRIPTIONS: Readonly<Record<ToastMode, string>> = {
  full: '示意圖：畫面右下角跳出提示，有誰做了什麼、哪個項目與留言的內容；側欄的收件匣照樣有未讀的數字。',
  compact: '示意圖：畫面右下角跳出提示，只有誰做了什麼與哪個項目；側欄的收件匣照樣有未讀的數字。',
  off: '示意圖：不跳提示，只有側欄的收件匣有未讀的數字。',
};

const NAV: readonly { readonly icon: IconName; readonly label: string; readonly count?: number }[] = [
  { icon: 'myWork', label: '我的工作' },
  { icon: 'notifications', label: '收件匣', count: UNREAD_SAMPLE },
  { icon: 'projects', label: '專案' },
];

/**
 * 「通知提示」的示意圖：縮小的一個畫面，左邊是側欄（收件匣有未讀的數字，三種方式都有），右下角是跳出來的提示
 * （和真的同一個長相，完整與精簡的差別看得到；不顯示就沒有）。高度固定，切換方式時版面不跳。
 */
export function ToastModePreview({ mode }: { readonly mode: ToastMode }) {
  return (
    <div role="img" aria-label={DESCRIPTIONS[mode]} className={styles.screen}>
      <div className={styles.sidebar}>
        {NAV.map((entry) => (
          <span key={entry.label} className={styles.nav}>
            <Icon name={entry.icon} className={styles.navIcon} />
            <span className={styles.navLabel}>{entry.label}</span>
            {entry.count !== undefined && <CountBadge value={entry.count} />}
          </span>
        ))}
      </div>
      <div className={styles.main}>{mode !== 'off' && <ToastPreviewCard notification={SAMPLE} mode={mode} />}</div>
    </div>
  );
}
