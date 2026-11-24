/**
 * CSV／TSV 的解析（RFC 4180），給匯入項目用（PM-4b）。純函式、不加套件。
 * 列號是檔案的列號（資料列，不是畫面上的行）：標題是第 1 列；完全空白的列略過，但照樣佔一個號碼，
 * 這樣使用者在 Excel 裡看到的列號和這裡說的一樣。
 */

export type CsvDelimiter = ',' | '\t';

export interface CsvRecord {
  /** 檔案的列號，從 1 開始。 */
  readonly line: number;
  readonly cells: readonly string[];
}

export interface CsvTable {
  readonly delimiter: CsvDelimiter;
  /** 標題列（第一個不是空白的列）。 */
  readonly header: CsvRecord;
  /** 標題之後、不是空白的列。 */
  readonly rows: readonly CsvRecord[];
  /** 檔案結束時有一個引號還沒結束：後面的內容全被吃進最後一格，要請使用者檢查檔案。 */
  readonly hasUnclosedQuote: boolean;
}

const QUOTE = '"';
const BOM = '\uFEFF';

/** 第一行有 tab、沒有逗號就是 TSV（從試算表直接複製貼上存成的檔案）。 */
export function detectDelimiter(text: string): CsvDelimiter {
  const firstLine = text.replace(BOM, '').split(/\r\n|\n|\r/, 1)[0] ?? '';
  return firstLine.includes('\t') && !firstLine.includes(',') ? '\t' : ',';
}

/** 把整份文字切成「列 × 格」；空白列也留著（列號才對得上），由呼叫端決定略過。 */
function splitRecords(text: string, delimiter: CsvDelimiter): { records: string[][]; hasUnclosedQuote: boolean } {
  const records: string[][] = [];
  let cells: string[] = [];
  let field = '';
  let isQuoted = false;
  let isFieldStarted = false;
  let index = 0;
  const endField = () => {
    cells = [...cells, field];
    field = '';
    isFieldStarted = false;
  };
  const endRecord = () => {
    endField();
    records.push(cells);
    cells = [];
  };
  while (index < text.length) {
    const char = text.charAt(index);
    if (isQuoted) {
      if (char === QUOTE && text.charAt(index + 1) === QUOTE) {
        field += QUOTE;
        index += 1;
      } else if (char === QUOTE) {
        isQuoted = false;
      } else {
        field += char;
      }
    } else if (char === QUOTE && !isFieldStarted) {
      isQuoted = true;
      isFieldStarted = true;
    } else if (char === delimiter) {
      endField();
    } else if (char === '\r' || char === '\n') {
      if (char === '\r' && text.charAt(index + 1) === '\n') index += 1;
      endRecord();
    } else {
      field += char;
      isFieldStarted = true;
    }
    index += 1;
  }
  // 檔案結尾沒有換行：最後一列還沒收；結尾剛好是換行時不多出一個空白列。
  if (field !== '' || isFieldStarted || cells.length > 0) endRecord();
  return { records, hasUnclosedQuote: isQuoted };
}

function isBlank(cells: readonly string[]): boolean {
  return cells.every((cell) => cell.trim() === '');
}

/** 解析成表格；沒有任何非空白的列時 header 是 null。 */
export function parseCsvTable(source: string): Omit<CsvTable, 'header'> & { readonly header: CsvRecord | null } {
  const text = source.startsWith(BOM) ? source.slice(BOM.length) : source;
  const delimiter = detectDelimiter(text);
  const { records, hasUnclosedQuote } = splitRecords(text, delimiter);
  const numbered = records
    .map((cells, index): CsvRecord => ({ line: index + 1, cells }))
    .filter((record) => !isBlank(record.cells));
  const [header = null, ...rows] = numbered;
  return { delimiter, header, rows, hasUnclosedQuote };
}
