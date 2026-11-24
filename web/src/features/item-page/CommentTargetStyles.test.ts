import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { HIGHLIGHT_MS, ownsCommentTarget } from './commentTarget';

// 測試環境不處理 CSS，直接讀檔案的文字。測試從 web/ 底下執行。
function readSource(path: string): string {
  return readFileSync(resolve(process.cwd(), 'src', path), 'utf8');
}

describe('標出目標的時間', () => {
  it('HIGHLIGHT_MS 和 tokens.css 的 --duration-target 是同一個數字', () => {
    // Arrange
    const tokens = readSource('styles/tokens.css');

    // Act
    const match = /--duration-target:\s*([\d.]+)s/.exec(tokens);

    // Assert
    expect(match, '找不到 --duration-target').not.toBeNull();
    expect(Number(match?.[1]) * 1000).toBe(HIGHLIGHT_MS);
  });
});

describe('哪一個時間軸回應 hash', () => {
  it('面板裡的：網址的 item 和自己的項目編號不分大小寫地相同才回應', () => {
    expect(ownsCommentTarget(true, 'smk-8', 'SMK-8')).toBe(true);
    expect(ownsCommentTarget(true, 'SMK-7', 'SMK-8')).toBe(false);
    expect(ownsCommentTarget(true, null, 'SMK-8')).toBe(false);
  });

  it('整頁的：面板沒開才回應', () => {
    expect(ownsCommentTarget(false, null, 'SMK-7')).toBe(true);
    expect(ownsCommentTarget(false, 'SMK-8', 'SMK-7')).toBe(false);
  });
});
