import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）；實際的長相在瀏覽器量。
function read(path: string): string {
  return readFileSync(resolve(process.cwd(), path), 'utf8');
}

describe('底部分頁列的未讀數', () => {
  const css = read('src/features/shell/TabBar.module.css');

  it('實心的小膠囊：強調色實心底、對比字、18px 高、最小寬度等於高度', () => {
    // Assert
    expect(css).toContain('background: var(--color-accent-solid)');
    expect(css).toContain('color: var(--color-accent-contrast)');
    expect(css).toContain('--badge-size: 1.125rem');
    expect(css).toContain('min-width: var(--badge-size)');
    expect(css).toContain('height: var(--badge-size)');
    expect(css).toContain('font-size: var(--text-xs)');
  });

  it('強制色彩模式有外框，數字看得到', () => {
    // Assert
    expect(css).toMatch(/forced-colors: active\) \{\s+\.badge \{[^}]*border: 1px solid CanvasText/);
  });
});
