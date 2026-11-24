import { describe, expect, it } from 'vitest';
import { agencyBlueprint } from '../test/blueprints';
import { formatFileSize, importButtonLabel, importedNotice, issueSentence, summaryText } from './importSummary';
import { fieldLabelOf, serverIssuesOf } from './importServerIssues';
import { templateCsv } from './importTemplate';

describe('serverIssuesOf', () => {
  it('rows/i/欄位換回檔案的列號與欄位名稱', () => {
    // Arrange
    const details = [
      { code: 'item.invalid', path: 'rows/0/due_date', message: '到期日不能早於開始日。' },
      { code: 'item.invalid', path: 'rows/2/quote_amount', message: '金額不能是負的。' },
    ];

    // Act
    const { issues, general } = serverIssuesOf(details, [2, 5, 9], agencyBlueprint);

    // Assert
    expect(issues).toEqual([
      { line: 2, field: '到期日', message: '到期日不能早於開始日。' },
      { line: 9, field: '報價金額', message: '金額不能是負的。' },
    ]);
    expect(general).toEqual([]);
  });

  it('只有列的問題（沒有欄位）、對不到列的問題與沒有 path 的說明', () => {
    // Arrange
    const details = [
      { code: 'a', path: 'rows/1', message: '這一列不能建立。' },
      { code: 'b', path: 'rows', message: '最多 1,000 列。' },
      { code: 'c', path: 'rows/99/title', message: '超出範圍。' },
      { code: 'd', path: null, message: '還有 3 筆問題沒有列出。' },
    ];

    // Act
    const { issues, general } = serverIssuesOf(details, [2, 3], agencyBlueprint);

    // Assert
    expect(issues).toEqual([{ line: 3, field: null, message: '這一列不能建立。' }]);
    expect(general).toEqual(['最多 1,000 列。', '超出範圍。', '還有 3 筆問題沒有列出。']);
  });

  it('認不得的欄位原樣給，fields/ 開頭的路徑去掉前綴', () => {
    expect(fieldLabelOf('mystery', agencyBlueprint)).toBe('mystery');
    expect(fieldLabelOf('fields/impact', agencyBlueprint)).toBe('影響程度');
  });
});

describe('文字', () => {
  it('摘要：全部可以與有問題的列數', () => {
    expect(summaryText(120, 118)).toBe('共 120 列；118 列可以匯入，2 列有問題');
    expect(summaryText(5, 5)).toBe('共 5 列；全部可以匯入');
  });

  it('按鈕：全部可以、略過幾列、沒有可以匯入的', () => {
    expect(importButtonLabel(118, 0)).toBe('匯入 118 個項目');
    expect(importButtonLabel(118, 2)).toBe('匯入沒問題的 118 個項目（略過 2 列）');
    expect(importButtonLabel(0, 3)).toBe('匯入');
  });

  it('成功的提示帶第一個與最後一個編號', () => {
    expect(importedNotice(118, ['AGY-12', 'AGY-50', 'AGY-129'])).toBe('已匯入 118 個項目（AGY-12～AGY-129）。');
    expect(importedNotice(1, ['AGY-12'])).toBe('已匯入 1 個項目（AGY-12）。');
    expect(importedNotice(3, [])).toBe('已匯入 3 個項目。');
  });

  it('檔案大小與問題的句子', () => {
    expect(formatFileSize(200)).toBe('1 KB');
    expect(formatFileSize(2048)).toBe('2 KB');
    expect(formatFileSize(6 * 1024 * 1024)).toBe('6.0 MB');
    expect(issueSentence({ line: 5, field: '負責人', message: '找不到「王小明」這個成員' })).toBe('第 5 列「負責人」：找不到「王小明」這個成員');
    expect(issueSentence({ line: 7, field: null, message: '重複了' })).toBe('第 7 列：重複了');
  });
});

describe('templateCsv', () => {
  it('UTF-8 加 BOM，一列標題：核心欄位加自訂欄位', () => {
    // Arrange & Act
    const text = templateCsv(agencyBlueprint, true);

    // Assert
    expect(text.startsWith('\uFEFF標題,類型,狀態,負責人,優先度,開始日,到期日,估計,上層項目（編號）,說明,週期,里程碑,報價金額,')).toBe(true);
    expect(text.trimEnd().split(/\r?\n/)).toHaveLength(1);
    expect(text).toContain('影響程度');
  });

  it('沒有 project.manage 時不放「狀態」', () => {
    expect(templateCsv(agencyBlueprint, false)).not.toContain('狀態');
  });
});
