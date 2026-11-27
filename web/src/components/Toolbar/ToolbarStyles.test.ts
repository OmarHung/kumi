import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字。測試從 web/ 底下執行。
const css = readFileSync(resolve(process.cwd(), 'src/components/Toolbar/Toolbar.module.css'), 'utf8');
const compact = css.slice(css.indexOf('@media (max-width: 48rem)'));

describe('窄螢幕一行的工具列', () => {
  it('不折行、左右滑、藏捲軸，並且是隱藏文字的定位基準', () => {
    // Arrange / Act / Assert
    expect(compact).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*position: relative;/);
    expect(compact).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*flex-wrap: nowrap;/);
    expect(compact).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*overflow-x: auto;/);
    expect(compact).toMatch(/scrollbar-width: none;/);
    expect(compact).toMatch(/::-webkit-scrollbar \{\s*display: none;/);
  });

  it('焦點外框畫在裡面，捲到邊的控制項不被裁掉', () => {
    // Arrange / Act / Assert
    expect(compact).toMatch(/:focus-visible \{[^}]*outline-offset: -2px;/);
  });

  it('工具列裡的下拉照內容的寬度：最窄 4.5rem，左右內距和旁邊的按鈕一樣', () => {
    // Arrange / Act / Assert
    expect(compact).toMatch(/\.toolbar\[data-scrollable\] \[data-width='content'\] \{[^}]*min-width: 4\.5rem;/);
    expect(compact).toMatch(/\[data-width='content'\] \{[^}]*padding-inline: var\(--space-3\);/);
  });
});
