import { describe, expect, it } from 'vitest';
import { IMPORT_MAX_BYTES, readImportFile } from './readImportFile';

function csvFile(content: string | Uint8Array<ArrayBuffer>, name = '匯入.csv', type = 'text/csv'): File {
  return new File([content], name, { type });
}

describe('readImportFile', () => {
  it('UTF-8 的 CSV 讀成標題與資料列', async () => {
    // Arrange
    const file = csvFile('標題,類型\n登入頁,任務\n');

    // Act
    const outcome = await readImportFile(file);

    // Assert
    expect(outcome.ok && outcome.file.header.cells).toEqual(['標題', '類型']);
    expect(outcome.ok && outcome.file.rows).toHaveLength(1);
    expect(outcome.ok && outcome.file.encoding).toBe('utf-8');
  });

  it('Big5 的檔案退回 Big5 讀', async () => {
    // Arrange：「中文」的 Big5
    const file = csvFile(new Uint8Array([0xa4, 0xa4, 0xa4, 0xe5, 0x0a, 0xa4, 0xa4]));

    // Act
    const outcome = await readImportFile(file);

    // Assert
    expect(outcome.ok && outcome.file.encoding).toBe('big5');
    expect(outcome.ok && outcome.file.header.cells).toEqual(['中文']);
  });

  it('超過 5 MB 時說明大小，不讀內容', async () => {
    // Arrange
    const file = csvFile(new Uint8Array(IMPORT_MAX_BYTES + 1));

    // Act
    const outcome = await readImportFile(file);

    // Assert
    expect(outcome).toEqual({ ok: false, message: expect.stringContaining('超過上限 5 MB') as string });
  });

  it('超過 1,000 列時說明列數', async () => {
    // Arrange
    const rows = Array.from({ length: 1001 }, (_, index) => `項目 ${String(index)}`).join('\n');

    // Act
    const outcome = await readImportFile(csvFile(`標題\n${rows}`));

    // Assert
    expect(outcome).toEqual({ ok: false, message: expect.stringContaining('1,001 列') as string });
  });

  it('剛好 1,000 列可以', async () => {
    // Arrange
    const rows = Array.from({ length: 1000 }, (_, index) => `項目 ${String(index)}`).join('\n');

    // Act
    const outcome = await readImportFile(csvFile(`標題\n${rows}`));

    // Assert
    expect(outcome.ok).toBe(true);
  });

  it.each([
    ['試算表檔案請先另存成 CSV', csvFile('x', '資料.xlsx', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'), 'Excel'],
    ['不是文字檔', csvFile('x', '圖.png', 'image/png'), '只能匯入 CSV'],
    ['空檔案', csvFile('', '空.csv'), '是空的'],
    ['只有標題', csvFile('標題\n', '標題.csv'), '只有標題列'],
    ['只有空白列', csvFile('\n\n', '空白.csv'), '沒有內容'],
    ['引號沒結束', csvFile('a\n"x', '壞.csv'), '引號'],
  ])('%s：說明原因', async (_name, file, expected) => {
    // Arrange & Act
    const outcome = await readImportFile(file);

    // Assert
    expect(outcome).toEqual({ ok: false, message: expect.stringContaining(expected) as string });
  });
});
