import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）。jsdom 不排版：這裡守住「面板排在旁邊時」給內容的約定，
// 實際的樣子在瀏覽器量（1305 寬：看板、行事曆、時間軸各開一次面板）。
function read(path: string): string {
  return readFileSync(resolve(process.cwd(), path), 'utf8');
}

/** 取出一條規則（從選擇器到它的第一個 }）的內容。 */
function ruleOf(css: string, selector: string): string {
  const start = css.indexOf(`${selector} {`);
  expect(start, `找不到 ${selector}`).toBeGreaterThanOrEqual(0);
  return css.slice(start, css.indexOf('}', start));
}

describe('面板排在旁邊（80rem 以上）時清單那一塊的約定', () => {
  const css = read('src/components/SidePanel/SidePanel.module.css');
  const wide = css.slice(css.indexOf('@media (min-width: 80rem)'));

  it('清單那一塊自成一層：裡面有層級的東西（看板欄的表頭）不會畫到面板上面', () => {
    // Arrange & Act
    const main = ruleOf(wide, ".layout[data-has-panel='true'] > .main");

    // Assert
    expect(main).toMatch(/isolation:\s*isolate;/);
  });

  it('告訴內容右邊不能再伸出去（--content-bleed-end: 0）、至少留 --space-3（--content-gap-end）', () => {
    // Arrange & Act
    const main = ruleOf(wide, ".layout[data-has-panel='true'] > .main");

    // Assert
    expect(main).toMatch(/--content-bleed-end:\s*0px;/);
    expect(main).toMatch(/--content-gap-end:\s*var\(--space-3\);/);
  });
});

describe('內容照約定讓出面板的位置', () => {
  it('看板的欄右邊伸出去的距離與留白跟著約定走（沒有面板時照舊伸到頁面的邊緣）', () => {
    // Arrange & Act
    const lanes = ruleOf(read('src/features/board/Board.module.css'), '.lanes');

    // Assert
    expect(lanes).toMatch(/margin-inline-end:\s*calc\(-1 \* var\(--content-bleed-end, var\(--page-gutter\)\)\);/);
    expect(lanes).toMatch(/padding-inline-end:\s*max\(var\(--content-bleed-end, var\(--page-gutter\)\), var\(--content-gap-end, 0px\)\);/);
  });

  it('行事曆（含月份那一列的按鈕）與時間軸的外框右邊留 --content-gap-end', () => {
    // Arrange & Act
    const calendar = ruleOf(read('src/features/calendar/Calendar.module.css'), '.view');
    const timeline = ruleOf(read('src/features/timeline/Timeline.module.css'), '.view');

    // Assert
    expect(calendar).toMatch(/padding-inline-end:\s*var\(--content-gap-end, 0px\);/);
    expect(timeline).toMatch(/padding-inline-end:\s*var\(--content-gap-end, 0px\);/);
  });
});

describe('右下角的 AI 助理按鈕在的時候', () => {
  it('面板的內容底下多留按鈕的高度：最後的留言框捲得到按鈕上面', () => {
    // Arrange & Act
    const css = read('src/components/SidePanel/SidePanel.module.css');
    const body = ruleOf(css, ":root[data-assistant='button'] .body");

    // Assert
    expect(body).toMatch(/padding-bottom:[^;]*var\(--assistant-button-size\)/);
  });

  it('留言框捲進來時，下面留出按鈕的高度（scroll-margin-block-end）', () => {
    // Arrange & Act
    const css = read('src/features/item-page/ActivitySection.module.css');

    // Assert
    expect(css).toMatch(/\n\.composer\s*\{[^}]*scroll-margin-block-end:\s*var\(--space-3\);/);
    expect(ruleOf(css, ":root[data-assistant='button'] .composer")).toMatch(/scroll-margin-block-end:[^;]*var\(--assistant-button-size\)/);
  });
});

describe('編輯器工具列的「插入圖片或檔案」', () => {
  it('黏在工具列的右端（面板窄、格式按鈕捲動時也看得到），底色蓋住捲到底下的按鈕', () => {
    // Arrange & Act
    const group = ruleOf(read('src/features/rich-text/EditorToolbar.module.css'), '.insertGroup');

    // Assert
    expect(group).toMatch(/position:\s*sticky;/);
    expect(group).toMatch(/inset-inline-end:\s*0;/);
    expect(group).toMatch(/background:\s*var\(--color-surface\);/);
  });
});

