import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS（連 ?raw 也是空的模組），直接讀檔案的文字。測試從 web/ 底下執行。
const css = readFileSync(resolve(process.cwd(), 'src/features/planning/Planning.module.css'), 'utf8');

/** 只有這一個選擇器、從行首開始的第一條規則的內容。 */
function ruleBody(selector: string): string {
  const lines = css.split('\n');
  const index = lines.indexOf(`${selector} {`);
  expect(index, `找不到 ${selector}`).toBeGreaterThanOrEqual(0);
  const rest = lines.slice(index + 1);
  return rest.slice(0, rest.findIndex((line) => line.startsWith('}'))).join('\n');
}

// jsdom 沒有版面，量不到點擊範圍；這裡守住約定，實際的高度用 scripts/ui-audit.js 在瀏覽器量（DESIGN 第 14 節 targetsUnder24）。
describe('規劃頁的名稱連結', () => {
  it('是單獨一列的連結（不是內文裡的），點擊範圍至少 --target-min 高（觸控時 44px）', () => {
    // Arrange / Act
    const body = ruleBody('.name');

    // Assert
    expect(body).toMatch(/min-height:\s*var\(--target-min\);/);
    expect(body).toMatch(/display:\s*inline-flex;/);
  });
});
