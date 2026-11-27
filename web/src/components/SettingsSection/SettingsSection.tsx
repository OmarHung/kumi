import { useId, type ReactNode } from 'react';
import { useRevealWhenTargeted } from '../../lib/useRevealWhenTargeted';
import styles from './SettingsSection.module.css';

export interface SettingsSectionProps {
  /** 區塊的標題（h2，左欄）。 */
  readonly title: string;
  /** 標題下面一到兩句說明（左欄，小灰字）。要給控制項 aria-describedby 時一併給 descriptionId。 */
  readonly description?: ReactNode;
  /** 說明的 id：區塊裡的按鈕要用 aria-describedby 連到說明時給（我的帳號的「登出其他裝置」）。 */
  readonly descriptionId?: string;
  /** 標題的 id：區塊裡的表單要用 aria-labelledby 取名時給（密碼）；沒給就自己產生。 */
  readonly headingId?: string;
  /** 網頁裡的錨點（命令列工具是 cli，本機代理的第一步連過來）。 */
  readonly id?: string;
  /** 這一頁的第一筆內容（data-content-start，給 scripts/ui-audit.js 量高度預算，docs/DESIGN.md 6.2）。 */
  readonly isContentStart?: boolean;
  /** 不分左右兩欄：標題與說明在上、內容在下用滿寬度（比較用的表格放在右欄會擠成很多行）。 */
  readonly isFullWidth?: boolean;
  /** 右欄：這一塊的控制項、清單或表格（整塊）。 */
  readonly children: ReactNode;
}

/**
 * 設定頁的一塊（docs/design/07-components.md 的 SettingsSection）：左欄是標題與說明，右欄是整塊的內容；
 * 設定中心的內容欄夠寬（容器 46rem 以上）時左右兩欄，不夠時上下排。塊與塊之間一條細線（沒有卡片與陰影，第 1 節）。
 * 是一個有名稱的 section（aria-labelledby 指到 h2），報讀軟體照標題跳得到每一塊。
 */
export function SettingsSection({ title, description, descriptionId, headingId: givenHeadingId, id, isContentStart = false, isFullWidth = false, children }: SettingsSectionProps) {
  const generatedHeadingId = useId();
  const headingId = givenHeadingId ?? generatedHeadingId;
  const { sectionRef, headingRef } = useRevealWhenTargeted(id);
  return (
    <section ref={sectionRef} id={id} className={styles.section} aria-labelledby={headingId} data-content-start={isContentStart || undefined} data-full-width={isFullWidth || undefined}>
      <div className={styles.aside}>
        {/* tabIndex=-1：被錨點指到時程式把焦點放過來（Tab 不會停在標題上）。 */}
        <h2 ref={headingRef} id={headingId} className={styles.title} tabIndex={-1}>
          {title}
        </h2>
        {description !== undefined && (
          <div id={descriptionId} className={styles.description}>
            {description}
          </div>
        )}
      </div>
      <div className={styles.body}>{children}</div>
    </section>
  );
}
