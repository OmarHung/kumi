import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { SPAN_LANE_BUDGET } from './calendarSpans';

// 測試環境不處理 CSS，直接讀檔案的文字（測試從 web/ 底下執行）。jsdom 量不到高度，這裡守住算式裡「窄螢幕拿掉的內距」，
// 實際的高度在瀏覽器量（scripts/ui-audit.js，手機預算 200）。
const css = readFileSync(resolve(process.cwd(), 'src/features/calendar/Calendar.module.css'), 'utf8');

/** 取出窄螢幕（48rem 以下）所有 @media 區塊的文字。 */
function narrowBlock(): string {
  const blocks = css.split('@media (max-width: 48rem)').slice(1).map((part) => part.slice(0, part.indexOf('\n}\n')));
  expect(blocks.length, '找不到 48rem 以下的 @media').toBeGreaterThan(0);
  return blocks.join('\n');
}

describe('窄螢幕行事曆的高度', () => {
  it('月份那一列上下各 12px，不另外設高度', () => {
    // Arrange & Act
    const block = narrowBlock();

    // Assert
    expect(block).toMatch(/\.bar\s*\{[^}]*padding-block:\s*var\(--space-3\);/);
    expect(block).not.toMatch(/min-height|height:/);
  });

  it('議程第一段不再有上方留白，左右與下方照舊', () => {
    // Arrange & Act
    const block = narrowBlock();

    // Assert
    expect(block).toMatch(/\.agenda\s*\{[^}]*padding-block-start:\s*0;/);
  });
});

describe('期間排法的條', () => {
  // 位置只靠 data-col、data-span、data-lane 設的變數（不寫行內樣式）：少一個值，那一種條就停在左上角。
  it('樣式表有一週每一天的起點、一到七天的寬度、每一條車道的位置', () => {
    // Arrange / Act
    const values = (attribute: string) => [...css.matchAll(new RegExp(`\\[data-${attribute}='(\\d+)'\\]`, 'g'))].map((match) => Number(match[1]));

    // Assert
    expect(values('col')).toEqual([0, 1, 2, 3, 4, 5, 6]);
    expect(values('span')).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(values('lane')).toEqual(Array.from({ length: SPAN_LANE_BUDGET }, (_, lane) => lane));
  });

  it('車道數和程式的預算一致', () => {
    // Arrange / Act / Assert
    expect(css).toMatch(new RegExp(`--lanes:\\s*${String(SPAN_LANE_BUDGET)};`));
  });

  // 後面六格是有定位的、後畫的在上：覆蓋層沒有層級時條被它們的底色蓋住（jsdom 不排版，只能守住樣式表的寫法）。
  it('覆蓋層有層級，在黏住的表頭（--z-sticky）底下', () => {
    // Arrange / Act
    const block = /\.spanLanes\s*\{[^}]*\}/.exec(css)?.[0] ?? '';

    // Assert
    expect(block).toMatch(/z-index:\s*1;/);
  });

  // CSS Modules 同名的類別會合併：條原本也叫 .bar，月份那一列（CalendarBar 的 styles.bar）就跟著多了框、淡底與滑過的變色。
  it('月份那一列的 .bar 沒有條的樣式：條的類別不叫 bar', () => {
    // Arrange / Act
    const selectors = [...css.matchAll(/^\s*(\.[\w:.\-[\]'>= ]+?)\s*\{/gm)].map((match) => match[1]);
    const barRules = [...css.matchAll(/(^|\n)\s*\.bar(:hover)?\s*\{([^}]*)\}/g)].map((match) => match[3] ?? '');

    // Assert
    expect(selectors).toContain('.spanBar');
    expect(barRules.length).toBeGreaterThan(0);
    for (const rule of barRules) expect(rule).not.toMatch(/border:|background:/);
  });
});
