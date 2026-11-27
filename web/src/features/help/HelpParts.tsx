import { useId, type ReactNode } from 'react';
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

interface HelpTableProps {
  /** 捲動區塊的名稱（表格在裡面，不再重複一次名稱）。 */
  readonly label: string;
  readonly headers: readonly [string, string];
  readonly rows: readonly HelpTableRow[];
}

export interface HelpTableRow {
  readonly key: string;
  readonly name: ReactNode;
  readonly description: ReactNode;
}

/** 兩欄的對照表：太寬時在框裡橫向捲動（和 Markdown 的表格一樣是有名稱、鍵盤捲得動的區塊，docs/design/07-components.md 7.4）。 */
export function HelpTable({ label, headers, rows }: HelpTableProps) {
  return (
    <div className={styles.scroll} role="region" aria-label={label} tabIndex={0}>
      <table>
        <thead>
          <tr>
            <th scope="col">{headers[0]}</th>
            <th scope="col">{headers[1]}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key}>
              <td>{row.name}</td>
              <td>{row.description}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** 常見問題的一題：問題是粗體的一行，答案接在下面；問題不是標題（一長串小標題會讓標題的清單變吵）。 */
export function HelpFaq({ question, children }: { readonly question: string; readonly children: ReactNode }) {
  const questionId = useId();
  return (
    <div role="group" aria-labelledby={questionId}>
      <p id={questionId} className={styles.question}>
        {question}
      </p>
      <p className={styles.answer}>{children}</p>
    </div>
  );
}
