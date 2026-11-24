import type { ApiErrorDetail } from '../api/envelope';
import type { ProjectBlueprint } from '../api/blueprint';
import type { RowIssue } from './importRows';

/**
 * 伺服器檢查（乾跑或匯入）回的 422：細項的 path 是 `rows/<送出的第幾筆，從 0 算>/<欄位>`（欄位的寫法和新增項目一樣：
 * 核心欄位是 due_date 這類名字、自訂欄位是 key）。換回檔案的列號，和網頁自己檢查的問題合在一起顯示。
 * 對不到某一列的（整個請求的問題、被截斷的說明）放在 general。
 */
export interface ServerIssues {
  readonly issues: readonly RowIssue[];
  readonly general: readonly string[];
}

const CORE_FIELD_LABELS: Readonly<Record<string, string>> = {
  title: '標題',
  type: '類型',
  state: '狀態',
  assignee: '負責人',
  assignee_id: '負責人',
  priority: '優先度',
  start_date: '開始日',
  due_date: '到期日',
  estimate: '估計',
  parent: '上層項目',
  parent_key: '上層項目',
  parent_id: '上層項目',
  description: '說明',
  cycle: '週期',
  cycle_id: '週期',
  milestone: '里程碑',
  milestone_id: '里程碑',
};

const ROW_PATH = /^rows\/(\d+)(?:\/(.+))?$/;
const FIELDS_PREFIX = /^fields[/.]/;

/** 伺服器的欄位寫法換成畫面上的欄位名稱；認不得的原樣給。 */
export function fieldLabelOf(path: string, blueprint: ProjectBlueprint): string {
  const name = path.replace(FIELDS_PREFIX, '');
  const custom = blueprint.fields.find((field) => field.key === name);
  return custom?.name ?? CORE_FIELD_LABELS[name] ?? path;
}

function lineOf(detail: ApiErrorDetail, lines: readonly number[]): { line: number; rest: string | undefined } | null {
  const match = detail.path === null ? null : ROW_PATH.exec(detail.path);
  const [, index, rest] = match ?? [];
  const line = index === undefined ? undefined : lines[Number(index)];
  return line === undefined ? null : { line, rest };
}

/** lines 是送出的每一筆在檔案裡的列號（第 i 筆對到 lines[i]）。 */
export function serverIssuesOf(details: readonly ApiErrorDetail[], lines: readonly number[], blueprint: ProjectBlueprint): ServerIssues {
  const issues: RowIssue[] = [];
  const general: string[] = [];
  for (const detail of details) {
    const found = lineOf(detail, lines);
    if (found === null) {
      general.push(detail.message);
    } else {
      issues.push({ line: found.line, field: found.rest === undefined ? null : fieldLabelOf(found.rest, blueprint), message: detail.message });
    }
  }
  return { issues, general };
}
