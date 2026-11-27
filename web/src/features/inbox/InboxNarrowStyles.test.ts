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

describe('收件匣在窄螢幕的比例', () => {
  it('分類的分段切換撐滿整列、每格等寬，右緣和搜尋列對齊', () => {
    // Arrange
    const narrow = narrowPart(read('src/features/inbox/Inbox.module.css'));

    // Assert
    expect(narrow).toContain('flex: 1 1 100%');
    expect(narrow).toContain('.categories li');
    expect(narrow).toContain('flex: 1 1 0');
  });

  it('未讀的小圓點在列左邊的留白裡、不壓在頭像上，頭像是 32px', () => {
    // Arrange
    const css = read('src/features/inbox/CompactNotificationRow.module.css');

    // Assert
    expect(css).toContain('position: absolute;\n  top: calc(var(--space-3)');
    expect(css).toContain('left: var(--space-1)');
    expect(css).not.toContain('border: 2px solid var(--color-surface)');
    expect(css).toMatch(/\.leading > \* \{\s+width: 2rem;\s+height: 2rem;/);
  });
});
