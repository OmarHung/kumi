import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const css = readFileSync(resolve(process.cwd(), 'src/components/Menu/Menu.module.css'), 'utf8');

describe('選單項目的排版', () => {
  it('字與圖示垂直置中，只有帶第二行說明的項目靠上對齊', () => {
    // Arrange / Act
    const item = css.slice(css.indexOf('.item {'), css.indexOf('}', css.indexOf('.item {')));

    // Assert
    expect(item).toContain('align-items: center');
    expect(css).toMatch(/\.item:has\(\.reason\) \{[^}]*align-items: flex-start/);
  });

  it('觸控時一項和控制項同高，不是 44px', () => {
    // Arrange / Act
    const coarse = css.slice(css.indexOf('@media (pointer: coarse)'));

    // Assert
    expect(coarse).toMatch(/\.item \{[^}]*min-height: var\(--control-height\)/);
  });
});
