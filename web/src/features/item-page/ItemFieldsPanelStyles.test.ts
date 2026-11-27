import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）；jsdom 沒有版面，量不到欄寬，實際寬度在瀏覽器量。
const css = readFileSync(resolve(process.cwd(), 'src/features/item-page/ItemFieldsPanel.module.css'), 'utf8');

function ruleBody(selector: string): string {
  const lines = css.split('\n');
  const index = lines.findIndex((line) => line === `${selector} {`);
  expect(index, `找不到 ${selector}`).toBeGreaterThanOrEqual(0);
  const rest = lines.slice(index + 1);
  return rest.slice(0, rest.findIndex((line) => line.startsWith('}'))).join('\n');
}

describe('欄位區的版面（docs/DESIGN.md 欄位面板）', () => {
  it('名稱和值在同一行、名稱只佔自己的字寬：沒有對齊用的名稱欄、也不用 subgrid', () => {
    // Arrange / Act
    const row = ruleBody('.row');
    const label = ruleBody('.label');

    // Assert
    expect(row).toContain('flex-direction: row');
    expect(label).toContain('flex: 0 0 auto');
    expect(css).not.toContain('subgrid');
    expect(css).not.toContain('fit-content');
    expect(css).not.toContain('--label-max');
  });

  it('一列幾欄由面板的寬度自動決定（auto-fill，每欄至少 --field-min），不逐一寫欄數', () => {
    // Arrange / Act
    const list = ruleBody('.list');

    // Assert
    expect(list).toContain('repeat(auto-fill, minmax(var(--field-min), 1fr))');
    expect(css).toMatch(/--field-min: 10\.5rem;/);
    expect(css).not.toMatch(/repeat\([23],/);
  });

  it('窄螢幕（48rem 以下）一列一組，到期日與「已過期」不折行', () => {
    // Arrange / Act
    const narrow = css.slice(css.indexOf('@media (max-width: 48rem)'));

    // Assert
    expect(narrow).toContain('grid-template-columns: minmax(0, 1fr)');
    expect(narrow).toContain('flex-wrap: nowrap');
    // 一欄時列高 36px（使用者嫌 44px 的行距太寬），只在這張清單裡換掉。
    expect(narrow).toMatch(/\.list \{[^}]*--row-height: 2\.25rem;[^}]*--target-min: 2\.25rem;/);
  });

  it('名稱淡色、和值同樣的字級；名稱最多佔四成，長的折行', () => {
    // Arrange / Act
    const label = ruleBody('.label');

    // Assert
    expect(label).toContain('font-size: var(--text-sm)');
    expect(label).toContain('color: var(--color-text-muted)');
    expect(label).toContain('max-width: 40%');
  });

  it('名稱可以折行看到全名（長的自訂欄位名稱、手機沒有滑過）：不省略、不 nowrap', () => {
    // Arrange / Act
    const label = ruleBody('.label');

    // Assert
    expect(label).toContain('overflow-wrap: anywhere');
    expect(label).not.toContain('nowrap');
    expect(label).not.toContain('ellipsis');
    expect(label).not.toContain('overflow: hidden');
  });
});
