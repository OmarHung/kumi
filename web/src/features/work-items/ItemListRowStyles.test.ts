import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）；版面的實際樣子在瀏覽器看。
const css = readFileSync(resolve(process.cwd(), 'src/features/work-items/ItemListRow.module.css'), 'utf8');

function ruleOf(selector: string): string {
  const match = new RegExp(`^${selector.replace(/[.[\]]/g, '\\$&')} \\{([^}]*)\\}`, 'm').exec(css);
  expect(match, `找不到 ${selector}`).not.toBeNull();
  return match?.[1] ?? '';
}

describe('窄螢幕項目清單一筆的排法', () => {
  it('第一行的屬性一行不折：放不下時裁掉最後的，每一列一樣高', () => {
    // Arrange
    const meta = ruleOf('.meta');

    // Act / Assert
    expect(meta).toMatch(/white-space: nowrap;/);
    expect(meta).toMatch(/overflow: hidden;/);
    expect(meta).not.toMatch(/flex-wrap: wrap;/);
  });

  it('第一行的高度固定，頭像（20px）不把它撐高', () => {
    // Arrange / Act / Assert
    expect(ruleOf('.link')).toMatch(/grid-template-rows: minmax\(1\.25rem, auto\)/);
  });

  it('專案名稱在標題下面自己一行，一行省略號', () => {
    // Arrange
    const project = ruleOf('.project');

    // Act / Assert
    expect(project).toMatch(/grid-area: project;/);
    expect(project).toMatch(/text-overflow: ellipsis;/);
    expect(project).toMatch(/white-space: nowrap;/);
  });

  it('行距是 4px（--space-1），上下內距是 --space-3', () => {
    // Arrange
    const link = ruleOf('.link');

    // Act / Assert
    expect(link).toMatch(/gap: var\(--space-1\) var\(--space-2\);/);
    expect(link).toMatch(/padding: var\(--space-3\) var\(--space-4\);/);
  });
});
