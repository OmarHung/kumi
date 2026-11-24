import { EmailNotificationsSection } from './EmailNotificationsSection';
import { NotificationToastsSection } from './NotificationToastsSection';
import { TabBadgeSection } from './TabBadgeSection';
import styles from './AccountPage.module.css';

/** 設定中心的「通知」：Email 通知、畫面角落的通知提示、分頁上的未讀提示。 */
export function NotificationSettingsPage() {
  return (
    <div className={styles.page}>
      <EmailNotificationsSection />
      <NotificationToastsSection />
      <TabBadgeSection />
    </div>
  );
}
