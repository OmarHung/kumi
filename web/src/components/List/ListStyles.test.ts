import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS（連 ?raw 也是空的模組），直接讀檔案的文字。測試從 web/ 底下執行。
function readStyles(path: string): string {
  return readFileSync(resolve(process.cwd(), 'src/components', path), 'utf8');
}

/** 取出一條規則（選擇器那一行到第一個行首的 }）裡 padding 的值。 */
function paddingOf(css: string, selectorLine: string): string {
  const lines = css.split('\n');
  const index = lines.indexOf(`${selectorLine} {`);
  expect(index, `找不到 ${selectorLine}`).toBeGreaterThanOrEqual(0);
  const rest = lines.slice(index + 1);
  const body = rest.slice(0, rest.findIndex((line) => line.startsWith('}'))).join('\n');
  const match = /^\s*padding:\s*([^;]+);/m.exec(body);
  expect(match, `${selectorLine} 沒有 padding`).not.toBeNull();
  return (match?.[1] ?? '').trim();
}

/** padding 簡寫的左右那一段（兩個值的寫法：上下 左右）。 */
function inlinePadding(padding: string): string {
  const parts = padding.split(/\s+/);
  expect(parts, `padding 要是「上下 左右」兩個值：${padding}`).toHaveLength(2);
  return parts[1] ?? '';
}

// jsdom 沒有版面，量不到內距；這裡守住「清單和表格同一套左右內距」，實際的樣子在瀏覽器看（docs/DESIGN.md 7.1）。
describe('清單一筆的左右內距', () => {
  it('和表格的格子一樣，滑過與選取的底色不會貼著頭像和右邊的時間', () => {
    // Arrange
    const listRow = paddingOf(readStyles('List/List.module.css'), '.content');
    const tableCell = paddingOf(readStyles('Table/Table.module.css'), '.table td');

    // Act
    const listInline = inlinePadding(listRow);

    // Assert
    expect(listInline).not.toBe('0');
    expect(listInline).toBe(inlinePadding(tableCell));
  });
});
