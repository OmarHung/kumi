import type { SessionUser } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { ORGANIZATION_ROLE_LABELS } from '../../domain/labels';
import { OtherSessionsSection } from './OtherSessionsSection';
import { PasswordSection } from './PasswordSection';
import styles from './AccountPage.module.css';

/**
 * 基本資料只能看：名稱與 Email 現在誰都改不了（連管理員也沒有改的端點），所以不叫人去找誰。資料來自登入狀態，不另外讀。
 * 這一塊是第一筆內容（data-content-start，給 scripts/ui-audit.js 量高度預算，docs/DESIGN.md 6.2）。
 */
function ProfileSection({ user }: { readonly user: SessionUser }) {
  const facts: readonly (readonly [string, string])[] = [
    ['名稱', user.displayName],
    ['Email', user.email],
    ['組織', user.organizationName],
    ['組織角色', ORGANIZATION_ROLE_LABELS[user.role]],
  ];
  return (
    <SettingsSection title="基本資料" description="名稱與 Email 目前還不能修改。" isContentStart>
      <dl className={styles.facts}>
        {facts.map(([term, value]) => (
          <div key={term} className={styles.fact}>
            <dt>{term}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </SettingsSection>
  );
}

/**
 * 我的帳號（設定中心的第一頁，docs/design/08-interaction.md 8.3「我的帳號」）：基本資料、密碼、其他裝置。
 * 從帳號選單的「我的帳號」與側欄的「設定」進來；頁列與次導覽在 SettingsHubLayout。
 */
export function AccountPage() {
  const user = useCurrentUser();
  return (
    <div className={styles.page}>
      <ProfileSection user={user} />
      <PasswordSection />
      <OtherSessionsSection />
    </div>
  );
}
