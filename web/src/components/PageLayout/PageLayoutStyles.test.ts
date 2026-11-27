import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）。
const css = readFileSync(resolve(process.cwd(), 'src/components/PageLayout/PageLayout.module.css'), 'utf8');

describe('工具列的上下留白', () => {
  it('觸控時上下各 8px：36px 的控制項和頁列、下面的內容分得開，工具列 52px 高', () => {
    // Arrange
    const touch = css.slice(css.indexOf('@media (pointer: coarse)'));

    // Act / Assert
    expect(touch).toMatch(/\.toolbar \{[^}]*padding-block: var\(--space-2\);/);
  });
});
