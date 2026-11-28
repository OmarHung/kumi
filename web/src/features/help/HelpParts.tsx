import type { ReactNode } from 'react';
import { useRevealWhenTargeted } from '../../lib/useRevealWhenTargeted';
import styles from './Help.module.css';

interface HelpSectionProps {
  /** 網址的錨點（`/help/quick-start#notifications`），別的地方可以直接連到這一節。 */
  readonly id: string;
  readonly title: string;
  /** 一章的第一節標 data-content-start（給 scripts/ui-audit.js 量高度預算）。 */
  readonly isContentStart?: boolean;
  readonly children: ReactNode;
}

/** 一章裡的一節：有名稱的 section（指到自己的 h2），報讀軟體用地標或標題都跳得到；網址帶著它的錨點時捲過來。 */
export function HelpSection({ id, title, isContentStart = false, children }: HelpSectionProps) {
  const headingId = `${id}-heading`;
  const { sectionRef, headingRef } = useRevealWhenTargeted(id);
  return (
    <section ref={sectionRef} id={id} className={styles.section} aria-labelledby={headingId} data-content-start={isContentStart ? '' : undefined}>
      {/* tabIndex=-1：被錨點指到時程式把焦點放過來（Tab 不會停在標題上）。 */}
      <h2 ref={headingRef} id={headingId} tabIndex={-1}>
        {title}
      </h2>
      {children}
    </section>
  );
}

/** 章名下面的一兩句引言。 */
export function HelpLead({ children }: { readonly children: ReactNode }) {
  return <p className={styles.lead}>{children}</p>;
}

/** 順帶一提的提醒：灰字加縮排（不是 Notice：這裡沒有要使用者注意的狀態，只是旁註）。 */
export function HelpNote({ children }: { readonly children: ReactNode }) {
  return <p className={styles.note}>{children}</p>;
}
