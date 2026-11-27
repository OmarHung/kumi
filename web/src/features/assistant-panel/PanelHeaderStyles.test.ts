import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）；實際的長相在瀏覽器量。
function read(path: string): string {
  return readFileSync(resolve(process.cwd(), path), 'utf8');
}

describe('AI 助理頁首在窄螢幕的輕重', () => {
  const css = read('src/features/assistant-panel/PanelHeader.module.css');

  it('專案下拉沒有外框與底色，滑過才有底色；一行的按鈕間距一致（--space-1）', () => {
    // Assert
    expect(css).toMatch(/button\.projectSelect\[data-width='content'\] \{\s+border: 0;\s+background: transparent;/);
    expect(css).toContain(":hover:not(:disabled, [aria-disabled='true'])");
    expect(css).toMatch(/\.row \{[^}]*gap: var\(--space-1\)/);
  });

  it('選的項目是小膠囊（--text-sm、只佔自己的寬度），44px 的觸控範圍用 ::before 補', () => {
    // Assert
    expect(css).toMatch(/\.itemRow > button \{[^}]*font-size: var\(--text-sm\)/);
    expect(css).toMatch(/\.itemRow > button \{[^}]*height: 1\.75rem/);
    expect(css).toContain('.itemRow > button::before');
    expect(css).toContain('var(--target-min)');
  });
});
