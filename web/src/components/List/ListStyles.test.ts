import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS（連 ?raw 也是空的模組），直接讀檔案的文字。測試從 web/ 底下執行。
function readStyles(path: string): string {
  return readFileSync(resolve(process.cwd(), 'src/components', path), 'utf8');
}

/** 取出一條規則（選擇器那一行到第一個行首的 }）的內容。 */
function ruleBody(css: string, selectorLine: string): string {
  const lines = css.split('\n');
  const index = lines.indexOf(`${selectorLine} {`);
  expect(index, `找不到 ${selectorLine}`).toBeGreaterThanOrEqual(0);
  const rest = lines.slice(index + 1);
  return rest.slice(0, rest.findIndex((line) => line.startsWith('}'))).join('\n');
}

/** 一條規則裡某個宣告的值。 */
function declarationOf(css: string, selectorLine: string, property: string): string {
  const match = new RegExp(`^\\s*${property}:\\s*([^;]+);`, 'm').exec(ruleBody(css, selectorLine));
  expect(match, `${selectorLine} 沒有 ${property}`).not.toBeNull();
  return (match?.[1] ?? '').trim();
}

/** 「var(--名稱, 備用值)」的備用值：沒有外層調整時用的那一個。 */
function fallbackOf(value: string, name: string): string {
  const match = new RegExp(`^var\\(${name},\\s*(.+)\\)$`).exec(value);
  expect(match, `要寫成 var(${name}, 預設值)：${value}`).not.toBeNull();
  return (match?.[1] ?? '').trim();
}

/** padding 簡寫的左右那一段（兩個值的寫法：上下 左右）。左右可以是 var(--x, 預設值)，裡面的空白不算分隔。 */
function inlinePadding(padding: string): string {
  const match = /^(\S+)\s+(.+)$/.exec(padding);
  expect(match, `padding 要是「上下 左右」兩個值：${padding}`).not.toBeNull();
  return (match?.[2] ?? '').trim();
}

// jsdom 沒有版面，量不到內距；這裡守住「清單和表格同一套左右內距」與設定頁右欄的例外，實際的樣子在瀏覽器看（docs/DESIGN.md 7.1）。
describe('清單一筆的左右內距', () => {
  it('預設和表格的格子一樣，滑過與選取的底色不會貼著頭像和右邊的時間', () => {
    // Arrange
    const listRow = declarationOf(readStyles('List/List.module.css'), '.content', 'padding');
    const tableCell = declarationOf(readStyles('Table/Table.module.css'), '.table td', 'padding');

    // Act
    const listInline = fallbackOf(inlinePadding(listRow), '--list-row-inset');

    // Assert
    expect(listInline).not.toBe('0');
    expect(listInline).toBe(inlinePadding(tableCell));
  });

  it('滑過的底色預設是 --color-hover，外層可以換掉', () => {
    // Arrange
    const css = readStyles('List/List.module.css');

    // Act
    const hover = declarationOf(css, '.content:hover', 'background');

    // Assert
    expect(fallbackOf(hover, '--list-row-hover')).toBe('var(--color-hover)');
  });

  it('設定頁的右欄裡左右不留內距、不畫滑過的底色：文字和右欄其他內容的左緣對齊', () => {
    // Arrange
    const css = readStyles('SettingsSection/SettingsSection.module.css');

    // Act
    const inset = declarationOf(css, '.body', '--list-row-inset');
    const hover = declarationOf(css, '.body', '--list-row-hover');

    // Assert
    expect(inset).toBe('0');
    expect(hover).toBe('transparent');
  });
});
