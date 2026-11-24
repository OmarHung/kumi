/** 匯入頁上的文字：摘要、按鈕、成功的提示、檔案大小（PM-4b）。純函式，方便測。 */

export function summaryText(total: number, importable: number): string {
  const invalid = total - importable;
  if (invalid === 0) return `共 ${String(total)} 列；全部可以匯入`;
  return `共 ${String(total)} 列；${String(importable)} 列可以匯入，${String(invalid)} 列有問題`;
}

/** 主要按鈕的文字：全部可以、略過幾列、沒有可以匯入的（按鈕停用，原因另外寫）。 */
export function importButtonLabel(importable: number, skipped: number): string {
  if (importable === 0) return '匯入';
  if (skipped === 0) return `匯入 ${String(importable)} 個項目`;
  return `匯入沒問題的 ${String(importable)} 個項目（略過 ${String(skipped)} 列）`;
}

/** 成功後專案頁上的提示；編號是匯入的第一個與最後一個（照列的順序）。 */
export function importedNotice(count: number, createdKeys: readonly string[]): string {
  const first = createdKeys[0];
  const last = createdKeys[createdKeys.length - 1];
  if (first === undefined || last === undefined) return `已匯入 ${String(count)} 個項目。`;
  const range = first === last ? first : `${first}～${last}`;
  return `已匯入 ${String(count)} 個項目（${range}）。`;
}

const KILOBYTE = 1024;
const MEGABYTE = KILOBYTE * 1024;

export function formatFileSize(bytes: number): string {
  if (bytes >= MEGABYTE) return `${(bytes / MEGABYTE).toFixed(1)} MB`;
  return `${String(Math.max(1, Math.round(bytes / KILOBYTE)))} KB`;
}

/** 一句話：第 5 列「負責人」：找不到…；整列的問題沒有欄位名稱。 */
export function issueSentence(issue: { readonly line: number; readonly field: string | null; readonly message: string }): string {
  const where = issue.field === null ? `第 ${String(issue.line)} 列` : `第 ${String(issue.line)} 列「${issue.field}」`;
  return `${where}：${issue.message}`;
}
