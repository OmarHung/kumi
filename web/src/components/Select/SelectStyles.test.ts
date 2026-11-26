import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（同 BulkEditStyles.test.ts）；實際的對比用瀏覽器實測確認。
const css = readFileSync(resolve(process.cwd(), 'src/components/Select/Select.module.css'), 'utf8');

describe('選項的職稱（description）', () => {
  it('實心反白而且可選的選項：職稱跟著字色，不是灰字（灰字壓在強調色底上對比不足）', () => {
    // Arrange / Act
    const rule = /\.option\[data-active='true'\]:not\(\[aria-disabled='true'\]\) \.description \{([^}]*)\}/.exec(css)?.[1] ?? '';

    // Assert
    expect(rule).toMatch(/color: inherit;/);
  });

  it('不能選的選項反白是淡底灰字：職稱沒有被改成跟著字色的規則吃到', () => {
    // Arrange / Act
    const selectors = [...css.matchAll(/([^{}]*)\{[^}]*color: inherit;[^}]*\}/g)].map((match) => match[1] ?? '');

    // Assert
    expect(selectors.some((selector) => selector.includes('.description') && !selector.includes(":not([aria-disabled='true'])"))).toBe(false);
  });
});
