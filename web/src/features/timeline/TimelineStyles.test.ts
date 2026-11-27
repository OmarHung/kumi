import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）。
const css = readFileSync(resolve(process.cwd(), 'src/features/timeline/Timeline.module.css'), 'utf8');

/** 取出一條規則（選擇器那一行到第一個行首的 }）的內容。 */
function ruleOf(selectorLine: string): string {
  const lines = css.split('\n');
  const index = lines.indexOf(`${selectorLine} {`);
  expect(index, `找不到 ${selectorLine}`).toBeGreaterThanOrEqual(0);
  const rest = lines.slice(index + 1);
  return rest.slice(0, rest.findIndex((line) => line.startsWith('}'))).join('\n');
}

describe('條的命中範圍', () => {
  it('條的連結本身至少 --target-min 寬（腳本量的是連結的外框），看得到的條用內層畫', () => {
    // Arrange & Act
    const link = ruleOf('.markLink');
    const body = ruleOf('.markBody');

    // Assert
    expect(link).toMatch(/min-width:\s*var\(--target-min\)/);
    expect(link).not.toMatch(/border:/);
    expect(body).toMatch(/width:\s*calc\(var\(--span, 1\) \* var\(--day-width\)\)/);
  });
});

describe('窄螢幕時間軸的高度', () => {
  it('控制列上下各留 12px，和工具列、下面的內容分得開（2026-11-28 使用者回報三塊黏在一起）', () => {
    // Arrange & Act
    const bar = ruleOf('.compactBar');

    // Assert
    expect(bar).toMatch(/padding-block: var\(--space-3\);/);
  });
});
