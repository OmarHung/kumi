import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS（連 ?raw 也是空的模組），直接讀檔案的文字。測試從 web/ 底下執行。
const css = readFileSync(resolve(process.cwd(), 'src/features/item-table/ItemToolbar.module.css'), 'utf8');

/** 寬螢幕（min-width: 80rem）那一段的內容。 */
function wideBlock(): string {
  const start = css.indexOf('@media (min-width: 80rem)');
  expect(start, '找不到 @media (min-width: 80rem)').toBeGreaterThanOrEqual(0);
  return css.slice(start, css.indexOf('\n}\n', start));
}

// jsdom 沒有版面；這裡守住約定，實際排成幾列用 scripts/ui-audit.js 量（DESIGN 第 14 節）。
describe('工具列在 80rem 以下省寬度', () => {
  it('「顯示的欄位」80rem 以下只留圖示（字給報讀軟體），80rem 以上照舊有字', () => {
    // Arrange / Act
    const wide = wideBlock();

    // Assert
    expect(css).toMatch(/\.pickerLabel \{[^}]*clip-path: inset\(50%\);/);
    expect(wide).toMatch(/\.pickerLabel \{[^}]*position: static;/);
  });

  it('搜尋框 80rem 以下從 6rem 開始排，80rem 以上照舊 8rem', () => {
    // Arrange / Act
    const wide = wideBlock();

    // Assert
    expect(css).toMatch(/--toolbar-search-start: 6rem;/);
    expect(wide).toMatch(/--toolbar-search-start: 8rem;/);
  });
});
