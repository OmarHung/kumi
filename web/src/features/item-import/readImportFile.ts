import { IMPORT_MAX_ROWS } from '../../api/itemImport';
import { parseCsvTable, type CsvDelimiter, type CsvRecord } from '../../domain/csv';
import { decodeCsvBytes, type CsvEncoding } from '../../domain/csvDecode';
import { formatFileSize } from '../../domain/importSummary';

/** 檔案最大 5 MB（internal/HANDOFF-pm4b-projects-import.md 4.2）。 */
export const IMPORT_MAX_BYTES = 5 * 1024 * 1024;

export interface LoadedImportFile {
  readonly name: string;
  readonly size: number;
  readonly encoding: CsvEncoding;
  readonly delimiter: CsvDelimiter;
  readonly header: CsvRecord;
  readonly rows: readonly CsvRecord[];
}

export type ReadOutcome =
  | { readonly ok: true; readonly file: LoadedImportFile }
  | { readonly ok: false; readonly message: string };

const SPREADSHEET_EXTENSION = /\.(xlsx|xls|xlsm|ods|numbers)$/i;
const TEXT_EXTENSION = /\.(csv|tsv|txt)$/i;

function fail(message: string): ReadOutcome {
  return { ok: false, message };
}

/** 不必讀內容就能說的問題（副檔名、大小）；沒問題是 null。 */
function checkBeforeReading(file: File): string | null {
  if (SPREADSHEET_EXTENSION.test(file.name)) {
    return '這是試算表檔案，請先在 Excel 裡另存成 CSV，再選那個檔案。';
  }
  const isText = TEXT_EXTENSION.test(file.name) || file.type.startsWith('text/') || file.type === 'application/vnd.ms-excel';
  if (!isText) return '只能匯入 CSV 或 TSV 檔。';
  if (file.size > IMPORT_MAX_BYTES) return `檔案是 ${formatFileSize(file.size)}，超過上限 5 MB。請分成幾個檔案。`;
  if (file.size === 0) return '這個檔案是空的。';
  return null;
}

/** 讀檔、解碼（UTF-8，失敗退回 Big5）、解析；任何一步不行都回一句給使用者看的說明。 */
export async function readImportFile(file: File): Promise<ReadOutcome> {
  const problem = checkBeforeReading(file);
  if (problem !== null) return fail(problem);
  let decoded: ReturnType<typeof decodeCsvBytes>;
  try {
    decoded = decodeCsvBytes(await file.arrayBuffer());
  } catch {
    return fail('讀不到這個檔案的內容，請確認檔案沒有被其他程式鎖住，再選一次。');
  }
  const table = parseCsvTable(decoded.text);
  if (table.hasUnclosedQuote) return fail('檔案裡有一個引號沒有結束，後面的資料會被當成同一格。請檢查檔案再選一次。');
  if (table.header === null) return fail('這個檔案沒有內容。');
  if (table.rows.length === 0) return fail('這個檔案只有標題列，沒有可以匯入的資料。');
  if (table.rows.length > IMPORT_MAX_ROWS) {
    const total = table.rows.length.toLocaleString('en-US');
    return fail(`這個檔案有 ${total} 列資料，一次最多匯入 ${IMPORT_MAX_ROWS.toLocaleString('en-US')} 列。請分成幾個檔案。`);
  }
  return {
    ok: true,
    file: { name: file.name, size: file.size, encoding: decoded.encoding, delimiter: table.delimiter, header: table.header, rows: table.rows },
  };
}
