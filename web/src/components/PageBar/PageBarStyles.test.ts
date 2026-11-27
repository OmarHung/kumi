import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字。測試從 web/ 底下執行。
const css = readFileSync(resolve(process.cwd(), 'src/components/PageBar/PageBar.module.css'), 'utf8');

describe('頁列的標題字級', () => {
  it('48rem 以上是 --text-lg，48rem 以下照舊 --text-md', () => {
    // Arrange
    const base = /\n\.title \{([^}]*)\}/.exec(css)?.[1] ?? '';
    const phone = /@media \(max-width: 48rem\) \{\s*\.title \{([^}]*)\}/.exec(css)?.[1] ?? '';

    // Act / Assert
    expect(base).toMatch(/font-size: var\(--text-lg\)/);
    expect(phone).toMatch(/font-size: var\(--text-md\)/);
  });
});
