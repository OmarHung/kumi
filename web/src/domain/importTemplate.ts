import type { ProjectBlueprint } from '../api/blueprint';
import { importTargets } from './importTargets';

/** 範本的檔名與開頭的 BOM：Excel 靠 BOM 認出 UTF-8，打開才不會亂碼。 */
export const TEMPLATE_FILE_NAME = '匯入範本.csv';
const BOM = '\uFEFF';

/** 試算表會把開頭是這幾個字元的格子當公式執行：前面補一個 '，格子就只是文字。 */
const FORMULA_START = /^[=+\-@\t\r]/;

function escapeCell(cell: string): string {
  const safe = FORMULA_START.test(cell) ? `'${cell}` : cell;
  return /[",\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}

/**
 * 這個專案的匯入範本：一列標題，欄位是核心欄位加藍圖的自訂欄位。沒有 project.manage 的人不能匯入狀態，範本就不放。
 */
export function templateCsv(blueprint: ProjectBlueprint, canManage: boolean): string {
  const headers = importTargets(blueprint)
    .filter((option) => canManage || option.id !== 'state')
    .map((option) => escapeCell(option.label));
  return `${BOM}${headers.join(',')}\r\n`;
}
