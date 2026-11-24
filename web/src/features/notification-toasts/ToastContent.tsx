import type { Notification } from '../../api/types';
import { Icon } from '../../components/Icon/Icon';
import { notificationSentence } from '../inbox/notificationText';
import { toastExcerpt } from './toastText';
import styles from './NotificationToasts.module.css';

interface ToastContentProps {
  readonly notification: Notification;
  readonly mode: 'full' | 'compact';
}

/** 一則提示裡面的內容：鈴鐺、誰做了什麼、項目、（完整時）內容的摘錄。真的提示與設定頁的示意圖共用，長相只有一份。 */
export function ToastContent({ notification, mode }: ToastContentProps) {
  const excerpt = toastExcerpt(notification, mode);
  const { actor, itemKey, itemTitle } = notification;
  return (
    <>
      <Icon name="notifications" className={styles.icon} />
      <span className={styles.body}>
        <span className={styles.headline}>
          <span className={styles.actor}>{actor.displayName}</span> {notificationSentence(notification)}
        </span>
        <span className={styles.itemLine}>
          <span className={styles.itemKey}>{itemKey}</span> {itemTitle}
        </span>
        {excerpt !== null && <span className={styles.excerpt}>{excerpt}</span>}
      </span>
    </>
  );
}

/**
 * 示意用的一則提示（設定頁的「通知提示」）：和真的提示同一個外框與內容，不是連結、沒有關閉按鈕，不會自己消失。
 * 右邊照樣留出關閉按鈕的位置，換行的地方和真的一樣。
 */
export function ToastPreviewCard({ notification, mode }: ToastContentProps) {
  return (
    <div className={styles.toast}>
      <div className={styles.content}>
        <ToastContent notification={notification} mode={mode} />
      </div>
    </div>
  );
}
