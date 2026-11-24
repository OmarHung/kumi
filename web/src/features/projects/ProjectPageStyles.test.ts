import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字。測試從 web/ 底下執行。
const css = readFileSync(resolve(process.cwd(), 'src/features/projects/ProjectPage.module.css'), 'utf8');

describe('頁列上的「我的角色：…」', () => {
  it('64rem 以下只給報讀軟體（資訊加上設定、規劃、成員後，48～64rem 名稱會被擠到一兩個字）', () => {
    // Arrange / Act
    const base = /\.role \{([^}]*)\}/.exec(css)?.[1] ?? '';
    const wide = /@media \(min-width: (\d+rem)\) \{\s*\.role \{/.exec(css);

    // Assert
    expect(base).toMatch(/clip-path:\s*inset\(50%\)/);
    expect(wide?.[1]).toBe('64rem');
  });
});
