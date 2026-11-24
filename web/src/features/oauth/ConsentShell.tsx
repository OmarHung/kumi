import type { ReactNode } from 'react';
import { DocumentTitle } from '../../components/DocumentTitle';
import { PRODUCT_NAME } from '../../config/product';
import styles from './OAuthConsentPage.module.css';

interface ConsentShellProps {
  /** 瀏覽器分頁上的標題。 */
  readonly documentTitle: string;
  /** 還在確認請求時：區塊用 aria-label 取名（標題還沒有），之後改用 headingId 指到 h1。 */
  readonly isPending: boolean;
  readonly headingId: string;
  readonly children: ReactNode;
}

/**
 * 同意頁的外框（OAuth 與 kumi 的瀏覽器登入共用）：沒有側欄、不在 AppShell 底下，
 * 即時更新、新通知的提示、離開確認都是 AppShell／各編輯頁掛的，這種頁不會被它們打擾。
 */
export function ConsentShell({ documentTitle, isPending, headingId, children }: ConsentShellProps) {
  return (
    <main className={styles.page}>
      <DocumentTitle title={documentTitle} />
      <section
        className={styles.panel}
        aria-labelledby={isPending ? undefined : headingId}
        aria-label={isPending ? documentTitle : undefined}
      >
        <p className={styles.brand}>{PRODUCT_NAME}</p>
        {children}
      </section>
    </main>
  );
}

export function ConsentHeading({ id, children }: { readonly id: string; readonly children: ReactNode }) {
  return (
    <h1 id={id} className={styles.heading}>
      {children}
    </h1>
  );
}

/** 標題裡不斷行的片語（產品名）。 */
export function NoWrap({ children }: { readonly children: ReactNode }) {
  return <span className={styles.nowrap}>{children}</span>;
}

/** 同意頁的灰字事實（帳號、導回哪裡…）。 */
export function ConsentFacts({ children }: { readonly children: ReactNode }) {
  return <p className={styles.facts}>{children}</p>;
}

export function ConsentActions({ children }: { readonly children: ReactNode }) {
  return <div className={styles.actions}>{children}</div>;
}

/** 永遠留著一行高的提示（「至少要勾一項」）：出現與消失都不推動版面。 */
export function ConsentHint({ id, children }: { readonly id: string; readonly children?: ReactNode }) {
  return (
    <p id={id} className={styles.hint}>
      {children}
    </p>
  );
}
