import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）。
const css = readFileSync(resolve(process.cwd(), 'src/features/timeline/Timeline.module.css'), 'utf8');

/** 取出一條規則（選擇器那一行到第一個行首的 }）的內容。 */
function ruleOf(selectorLine: string): string {
  const lines = css.split('\n');
  const index = lines.indexOf(`${selectorLine} {`);
  expect(index, `找不到 ${selectorLine}`).toBeGreaterThanOrEqual(0);
  const rest = lines.slice(index + 1);
  return rest.slice(0, rest.findIndex((line) => line.startsWith('}'))).join('\n');
}

describe('條的命中範圍', () => {
  it('條的連結本身至少 --target-min 寬（腳本量的是連結的外框），看得到的條用內層畫', () => {
    // Arrange & Act
    const link = ruleOf('.markLink');
    const body = ruleOf('.markBody');

    // Assert
    expect(link).toMatch(/min-width:\s*var\(--target-min\)/);
    expect(link).not.toMatch(/border:/);
    expect(body).toMatch(/width:\s*calc\(var\(--span, 1\) \* var\(--day-width\)\)/);
  });
});

describe('窄螢幕時間軸的高度', () => {
  it('控制列上下各留 12px，和工具列、下面的內容分得開（2026-11-28 使用者回報三塊黏在一起）', () => {
    // Arrange & Act
    const bar = ruleOf('.compactBar');

    // Assert
    expect(bar).toMatch(/padding-block: var\(--space-3\);/);
  });
});

describe('窄螢幕的軌道', () => {
  it('軌道 8px 高、膠囊形、凹下去的底；條的顏色照狀態類別，過期的多一圈危險色外框', () => {
    // Arrange & Act
    const track = ruleOf('.lane');

    // Assert
    expect(track).toMatch(/block-size: 8px;/);
    expect(track).toMatch(/border-radius: var\(--radius-pill\);/);
    expect(track).toMatch(/background: var\(--color-surface-sunken\);/);
    expect(ruleOf(".trackBar[data-category='done']")).toMatch(/--color-state-done/);
    expect(ruleOf(".trackBar[data-category='in_progress']")).toMatch(/--color-state-active/);
    expect(ruleOf('.trackBar')).toMatch(/--color-state-idle/);
    expect(ruleOf('.trackOverdue')).toMatch(/box-shadow: 0 0 0 1\.5px var\(--color-danger\);/);
  });

  it('刻度列 28px 高、左右和清單同一條邊；今天的線 1.5px 的強調色、膠囊是實心強調色', () => {
    // Arrange & Act & Assert
    expect(ruleOf('.scale')).toMatch(/block-size: 1\.75rem;/);
    expect(ruleOf('.scale')).toMatch(/margin-inline: var\(--space-4\);/);
    expect(ruleOf('.todayLine')).toMatch(/inline-size: 1\.5px;/);
    expect(ruleOf('.todayLine')).toMatch(/background: var\(--color-accent\);/);
    expect(ruleOf('.todayPill')).toMatch(/background: var\(--color-accent-solid\);/);
  });

  it('條的位置與寬度用天數的比例換算，不寫死 px', () => {
    // Arrange & Act
    const bar = ruleOf('.trackBar');

    // Assert
    expect(bar).toMatch(/inset-inline-start: calc\(var\(--col\) \/ var\(--days\) \* 100%\);/);
    expect(bar).toMatch(/inline-size: calc\(var\(--span\) \/ var\(--days\) \* 100%\);/);
  });
});
