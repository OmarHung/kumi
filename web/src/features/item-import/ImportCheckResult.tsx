import type { ProjectBlueprint } from '../../api/blueprint';
import { Notice } from '../../components/Notice/Notice';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import type { ConvertedRow, RowIssue } from '../../domain/importRows';
import { issueSentence, summaryText } from '../../domain/importSummary';
import { importTargets, type ColumnMapping } from '../../domain/importTargets';
import styles from './ItemImport.module.css';

/** 有問題的列最多列到這麼多列，其餘只說還有幾列。 */
export const MAX_ISSUE_ROWS = 50;
/** 預覽前幾列可以匯入的內容。 */
export const PREVIEW_ROWS = 10;

interface ImportCheckResultProps {
  readonly blueprint: ProjectBlueprint;
  readonly mapping: ColumnMapping;
  readonly totalRows: number;
  readonly importable: readonly ConvertedRow[];
  readonly issues: readonly RowIssue[];
  readonly general: readonly string[];
  readonly isChecking: boolean;
}

function IssueList({ issues }: { readonly issues: readonly RowIssue[] }) {
  const sorted = [...issues].sort((a, b) => a.line - b.line);
  const lines = [...new Set(sorted.map((issue) => issue.line))];
  const shownLines = new Set(lines.slice(0, MAX_ISSUE_ROWS));
  const shown = sorted.filter((issue) => shownLines.has(issue.line));
  const hidden = lines.length - shownLines.size;
  return (
    <div>
      <h3 className={styles.subheading}>有問題的列</h3>
      <ul className={styles.issues}>
        {shown.map((issue, index) => (
          <li key={`${String(issue.line)}-${String(index)}`}>{issueSentence(issue)}</li>
        ))}
      </ul>
      {hidden > 0 && <p className={styles.hint}>還有 {hidden} 列有問題沒有列出。</p>}
    </div>
  );
}

function Preview({ blueprint, mapping, rows }: Pick<ImportCheckResultProps, 'blueprint' | 'mapping'> & { readonly rows: readonly ConvertedRow[] }) {
  const labels = new Map(importTargets(blueprint).map((option) => [option.id, option.label]));
  const targets = mapping.flatMap((target) => (target === null ? [] : [target]));
  return (
    <div>
      <h3 className={styles.subheading}>預覽（前 {Math.min(PREVIEW_ROWS, rows.length)} 列）</h3>
      <TableScroll label="匯入內容的預覽" layout="inline">
        <table className={tableStyles.table}>
          <caption className="visually-hidden">匯入內容的預覽</caption>
          <thead>
            <tr>
              <th scope="col">列</th>
              {targets.map((target) => (
                <th key={target} scope="col">{labels.get(target) ?? target}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.slice(0, PREVIEW_ROWS).map((row) => (
              <tr key={row.line}>
                <th scope="row">{row.line}</th>
                {targets.map((target) => (
                  <td key={target} className={styles.previewCell}>{row.preview.get(target) ?? ''}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </TableScroll>
    </div>
  );
}

/** 檢查的結果：一行摘要、有問題的列、可以匯入的前幾列預覽。 */
export function ImportCheckResult(props: ImportCheckResultProps) {
  const { totalRows, importable, issues, general, isChecking } = props;
  const problemLines = new Set(issues.map((issue) => issue.line));
  const invalidCount = totalRows - importable.length;
  return (
    <div className={styles.result}>
      <p className={styles.summary} role="status">
        {isChecking ? '正在檢查…' : summaryText(totalRows, importable.length)}
      </p>
      {invalidCount > 0 && problemLines.size > 0 && <IssueList issues={issues} />}
      {general.map((message, index) => (
        <Notice key={`${String(index)}-${message}`} tone="warning">{message}</Notice>
      ))}
      {importable.length > 0 && <Preview blueprint={props.blueprint} mapping={props.mapping} rows={importable} />}
    </div>
  );
}
