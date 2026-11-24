import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS（連 ?raw 也是空的模組），直接讀檔案的文字。測試從 web/ 底下執行。
function readStyles(path: string): string {
  return readFileSync(resolve(process.cwd(), 'src/features/item-table', path), 'utf8');
}

const tableCss = readStyles('ItemTable.module.css');
const cellsCss = readStyles('cells/Cells.module.css');

/** 取出一條規則的內容：只有這一個選擇器、從行首開始的第一條（不是好幾個選擇器合寫的、也不是 @media 裡面的）。 */
function ruleBody(css: string, selector: string): string {
  const lines = css.split('\n');
  const index = lines.findIndex((line, at) => line === `${selector} {` && !(lines[at - 1] ?? '').trimEnd().endsWith(','));
  expect(index, `找不到 ${selector}`).toBeGreaterThanOrEqual(0);
  const rest = lines.slice(index + 1);
  return rest.slice(0, rest.findIndex((line) => line.startsWith('}'))).join('\n');
}

function declarations(body: string): Record<string, string> {
  return Object.fromEntries(
    [...body.matchAll(/^\s*([\w-]+):\s*([^;]+);/gm)].map((match) => [match[1] ?? '', (match[2] ?? '').trim()]),
  );
}

// jsdom 沒有版面，量不到欄寬；這裡守住「欄寬、省略號、捲動讓出的寬度用同一組變數」這個約定，
// 實際捲動時格子有沒有躲在固定欄底下，要在瀏覽器量（docs/DESIGN.md 7.1）。
describe('固定在左邊的兩欄', () => {
  it('編號與標題兩欄的寬度固定（width、min-width、max-width 是同一個變數）', () => {
    // Arrange / Act
    const key = declarations(ruleBody(tableCss, '.pinKey'));
    const title = declarations(ruleBody(tableCss, '.pinTitle'));

    // Assert
    expect([key.width, key['min-width'], key['max-width']]).toEqual(Array(3).fill('var(--item-key-width)'));
    expect([title.width, title['min-width'], title['max-width']]).toEqual(Array(3).fill('var(--item-title-width)'));
    // 多選的勾選框欄是第一個固定欄（沒有它時偏移是 0）：編號與標題各往右讓出它的寬度。
    expect(key.left).toBe('var(--item-select-offset)');
    expect(title.left).toBe('calc(var(--item-select-offset) + var(--item-key-width))');
  });

  it('捲動容器讓出的寬度是兩欄的寬度相加，用同一組變數', () => {
    // Arrange / Act
    const scroll = declarations(ruleBody(tableCss, '.scroll'));

    // Assert
    expect(scroll['scroll-padding-left']).toBe(
      'calc(var(--item-select-offset) + var(--item-key-width) + var(--item-title-width))',
    );
    expect(scroll['--item-key-width']).toBeDefined();
    expect(scroll['--item-title-width']).toBeDefined();
  });

  it('表格裡的標題最寬是標題欄減掉按鈕的內距，長標題不會把欄撐寬', () => {
    // Arrange / Act
    const titleLine = declarations(ruleBody(cellsCss, '.titleLine'));

    // Assert
    expect(titleLine['max-width']).toBe('calc(var(--item-title-width) - 2 * var(--space-3))');
    expect(titleLine['text-overflow']).toBe('ellipsis');
  });
});
