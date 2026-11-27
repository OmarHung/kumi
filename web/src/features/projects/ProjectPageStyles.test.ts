import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字。測試從 web/ 底下執行。
const css = readFileSync(resolve(process.cwd(), 'src/features/projects/ProjectPage.module.css'), 'utf8');

describe('標題旁的「我的角色」', () => {
  it('報讀軟體的文字一律視覺上隱藏（剪掉，但還在 DOM 裡）', () => {
    // Arrange / Act
    const name = /\.roleName \{([^}]*)\}/.exec(css)?.[1] ?? '';

    // Assert
    expect(name).toMatch(/clip-path:\s*inset\(50%\)/);
  });

  it('64rem 以下不畫標籤（平板上名稱會被擠到一兩個字），64rem 以上才畫', () => {
    // Arrange / Act
    const base = /\.roleBadge \{([^}]*)\}/.exec(css)?.[1] ?? '';
    const wide = /@media \(min-width: (\d+rem)\) \{\s*\.roleBadge \{([^}]*)\}/.exec(css);

    // Assert
    expect(base).toMatch(/display:\s*none/);
    expect(wide?.[1]).toBe('64rem');
    expect(wide?.[2]).toMatch(/display:\s*inline-flex/);
  });
});
