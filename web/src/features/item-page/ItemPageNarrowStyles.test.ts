import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）；實際的長相在瀏覽器量。
function read(path: string): string {
  return readFileSync(resolve(process.cwd(), path), 'utf8');
}

/** 取出 @media (max-width: 48rem) 裡面的文字（到檔尾，這幾份檔案的窄螢幕區塊都放在後面）。 */
function narrowPart(css: string): string {
  const start = css.indexOf('@media (max-width: 48rem)');
  expect(start, '找不到窄螢幕區塊').toBeGreaterThanOrEqual(0);
  return css.slice(start);
}

describe('項目頁在窄螢幕的比例', () => {
  it('標題小一階（--text-lg），行高 --leading-tight，折行時兩行長度接近', () => {
    // Arrange
    const narrow = narrowPart(read('src/features/item-page/ItemPage.module.css'));

    // Assert
    expect(narrow).toContain('font-size: var(--text-lg)');
    expect(narrow).toContain('line-height: var(--leading-tight)');
    expect(narrow).toContain('text-wrap: balance');
  });

  it('欄位一欄時名稱至少四個字寬，值對齊同一條線；比四個字長的名稱照自己的寬度、最多四成', () => {
    // Arrange
    const narrow = narrowPart(read('src/features/item-page/ItemFieldsPanel.module.css'));

    // Assert
    expect(narrow).toMatch(/\.label \{[^}]*min-width: 4em;/);
    expect(narrow).not.toContain('max-width: none');
  });
});
