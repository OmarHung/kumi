import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字。測試從 web/ 底下執行。
const css = readFileSync(resolve(process.cwd(), 'src/components/Toolbar/Toolbar.module.css'), 'utf8');
const wideStart = css.indexOf('@media (min-width: 64rem)');
const phoneStart = css.indexOf('@media (max-width: 48rem)');
// 64rem 以下的預設（平板與手機）：第一個 @media 之前。
const belowWide = css.slice(0, Math.min(wideStart, phoneStart));
const phone = css.slice(phoneStart, wideStart > phoneStart ? wideStart : undefined);
const wide = css.slice(wideStart, phoneStart > wideStart ? phoneStart : undefined);

describe('64rem 以下一行的工具列', () => {
  it('不折行、左右滑、藏捲軸，並且是隱藏文字的定位基準（平板與手機都一樣）', () => {
    // Arrange / Act / Assert
    expect(belowWide).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*position: relative;/);
    expect(belowWide).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*flex-wrap: nowrap;/);
    expect(belowWide).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*overflow-x: auto;/);
    expect(belowWide).toMatch(/scrollbar-width: none;/);
    expect(belowWide).toMatch(/::-webkit-scrollbar \{\s*display: none;/);
  });

  it('焦點外框畫在裡面，捲到邊的控制項不被裁掉；64rem 以上改回外面', () => {
    // Arrange / Act / Assert
    expect(belowWide).toMatch(/:focus-visible \{[^}]*outline-offset: -2px;/);
    expect(wide).toMatch(/:focus-visible \{[^}]*outline-offset: 2px;/);
  });

  it('平板的搜尋框從 10rem 起、有空間才長大', () => {
    // Arrange / Act / Assert
    expect(belowWide).toMatch(/\.start > \[role='search'\] \{[^}]*flex: 1 0 10rem;/);
  });

  it('64rem 以上回到折行、不捲動', () => {
    // Arrange / Act / Assert
    expect(wide).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*flex-wrap: wrap;/);
    expect(wide).toMatch(/\.toolbar\[data-scrollable\] \{[^}]*overflow-x: visible;/);
  });
});

describe('手機（48rem 以下）的工具列', () => {
  it('工具列裡的下拉照內容的寬度：最窄 4.5rem，左右內距和旁邊的按鈕一樣', () => {
    // Arrange / Act / Assert
    expect(phone).toMatch(/\.toolbar\[data-scrollable\] \[data-width='content'\] \{[^}]*min-width: 4\.5rem;/);
    expect(phone).toMatch(/\[data-width='content'\] \{[^}]*padding-inline: var\(--space-3\);/);
  });
});
