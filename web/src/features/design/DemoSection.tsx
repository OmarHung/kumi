import type { ReactNode } from 'react';
import styles from './Design.module.css';

/** 展示頁的一個區塊：h2 加一組組的範例。 */
export function DemoSection({ title, children }: { readonly title: string; readonly children: ReactNode }) {
  return (
    <section aria-labelledby={`design-${title}`} className={styles.section}>
      <h2 id={`design-${title}`} className={styles.sectionTitle}>
        {title}
      </h2>
      {children}
    </section>
  );
}

/** 一個元件的一組狀態：名稱（等寬字）加範例。 */
export function Demo({ name, children }: { readonly name: string; readonly children: ReactNode }) {
  return (
    <div className={styles.demo}>
      <h3 className={styles.demoName}>{name}</h3>
      <div className={styles.demoBody}>{children}</div>
    </div>
  );
}
