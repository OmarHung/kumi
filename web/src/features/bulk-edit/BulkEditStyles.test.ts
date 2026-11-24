import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（同 ItemToolbarStyles.test.ts）。實際的位置用 scripts/ui-audit.js 與實測確認。
const css = readFileSync(resolve(process.cwd(), 'src/features/bulk-edit/BulkEdit.module.css'), 'utf8');

describe('表格第一欄的勾選框', () => {
  it('點擊範圍至少一列高：表格的格子靠上對齊，比一列矮時框會偏上（2026-11-18 實測上 4px、下 16.5px）', () => {
    // Arrange / Act
    const rule = /\.selectLabel \{([^}]*)\}/.exec(css)?.[1] ?? '';

    // Assert
    expect(rule).toMatch(/min-height: max\(var\(--target-min\), var\(--row-height\)\);/);
  });
});
