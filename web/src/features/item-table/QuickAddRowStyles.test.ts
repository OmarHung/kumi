import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）；實際的高度用 scripts/ui-audit.js 量。
const css = readFileSync(resolve(process.cwd(), 'src/features/item-table/QuickAddItem.module.css'), 'utf8');

function ruleOf(selector: string): string {
  const escaped = selector.replace(/[.[\]()'*>=:]/g, '\\$&');
  const match = new RegExp(`^${escaped} \\{([^}]*)\\}`, 'm').exec(css);
  expect(match, `找不到 ${selector}`).not.toBeNull();
  return match?.[1] ?? '';
}

describe('快速新增那一列的長相', () => {
  it('高度是一列（--row-height），下緣一條和表格的列一樣的細線', () => {
    // Arrange
    const row = ruleOf('.row');

    // Act / Assert
    // 下緣那條線算在一列的高度裡：內容 --row-height 減 1px，加上線剛好一列。
    expect(row).toMatch(/min-height: calc\(var\(--row-height\) - 1px\);/);
    expect(ruleOf('.form')).toMatch(/border-bottom: 1px solid var\(--color-border\);/);
  });

  it('類型下拉矮到 24px、沒有外框', () => {
    // Arrange
    const trigger = ruleOf(".form .row [role='combobox'][data-size]");

    // Act / Assert
    expect(trigger).toMatch(/height: 1\.5rem;/);
    expect(trigger).toMatch(/border-color: transparent;/);
    expect(trigger).toMatch(/background: transparent;/);
  });

  it('實心按鈕沒有字時藏起來但照留位置（不是剪成 1px 的小目標），有字才看得到', () => {
    // Arrange
    const hidden = ruleOf(".submit[data-shown='false']");

    // Act / Assert
    expect(hidden).toMatch(/visibility: hidden;/);
    expect(hidden).not.toMatch(/clip-path/);
  });
});
