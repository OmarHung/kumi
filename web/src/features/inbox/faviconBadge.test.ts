import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  badgeFill,
  badgedFaviconSvg,
  badgedFaviconUrl,
  DEFAULT_TAB_BADGE,
  LOGO_SHAPES,
  LOGO_SVG,
  TAB_BADGE_COLORS,
  TAB_BADGE_POSITIONS,
  TAB_BADGE_SIZES,
  type TabBadge,
} from './faviconBadge';

const VIEWBOX = 32;

interface Circle {
  readonly cx: number;
  readonly cy: number;
  readonly r: number;
  readonly fill: string | null;
}

function toCircle(element: Element | null): Circle {
  if (element === null) throw new Error('找不到圓');
  return {
    cx: Number(element.getAttribute('cx')),
    cy: Number(element.getAttribute('cy')),
    r: Number(element.getAttribute('r')),
    fill: element.getAttribute('fill'),
  };
}

/** 圓點是最外層的圓；挖縫的圓在遮罩裡。 */
function shapesOf(badge: TabBadge) {
  const svg = new DOMParser().parseFromString(badgedFaviconSvg(badge), 'image/svg+xml');
  return { dot: toCircle(svg.querySelector('svg > circle')), cut: toCircle(svg.querySelector('mask circle')) };
}

describe('帶圓點的分頁圖示', () => {
  it('預設是右上角、中等大小的紅點，外圍挖一圈透明的縫', () => {
    // Arrange & Act
    const { dot, cut } = shapesOf(DEFAULT_TAB_BADGE);

    // Assert
    expect(dot.cx).toBeGreaterThan(VIEWBOX / 2);
    expect(dot.cy).toBeLessThan(VIEWBOX / 2);
    expect(dot.fill).toBe(TAB_BADGE_COLORS.red.hex);
    expect([cut.cx, cut.cy]).toEqual([dot.cx, dot.cy]);
    expect(cut.r).toBeGreaterThan(dot.r);
  });

  it.each([
    ['top-right', true, false],
    ['bottom-right', true, true],
    ['top-left', false, false],
    ['bottom-left', false, true],
  ] as const)('位置 %s 的圓點在那一個角', (position, isRight, isBottom) => {
    // Arrange & Act
    const { dot } = shapesOf({ ...DEFAULT_TAB_BADGE, position });

    // Assert
    expect(dot.cx > VIEWBOX / 2).toBe(isRight);
    expect(dot.cy > VIEWBOX / 2).toBe(isBottom);
  });

  it('大小依序變大，而且不超出圖示', () => {
    // Arrange & Act
    const radii = TAB_BADGE_SIZES.map((size) => shapesOf({ ...DEFAULT_TAB_BADGE, size, position: 'bottom-left' }).dot);

    // Assert
    expect(radii.map((dot) => dot.r)).toEqual([...radii.map((dot) => dot.r)].sort((a, b) => a - b));
    expect(new Set(radii.map((dot) => dot.r)).size).toBe(TAB_BADGE_SIZES.length);
    radii.forEach((dot) => {
      expect(dot.cx - dot.r).toBeGreaterThanOrEqual(0);
      expect(dot.cy + dot.r).toBeLessThanOrEqual(VIEWBOX);
    });
  });

  it('每一種顏色都畫成那個顏色', () => {
    // Arrange
    const colors = Object.keys(TAB_BADGE_COLORS) as (keyof typeof TAB_BADGE_COLORS)[];

    // Act
    const fills = colors.map((color) => shapesOf({ ...DEFAULT_TAB_BADGE, color }).dot.fill);

    // Assert
    expect(fills).toEqual(colors.map((color) => TAB_BADGE_COLORS[color].hex));
  });

  it('自訂顏色畫成使用者選的色碼', () => {
    // Arrange & Act
    const { dot } = shapesOf({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '#123abc' });

    // Assert
    expect(dot.fill).toBe('#123abc');
  });

  it('自訂的色碼不合格式時（不可信的資料）畫成預設的紅色，不把字串放進圖裡', () => {
    // Arrange
    const badge = { ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '"/><script>alert(1)</script>' } as const;

    // Act
    const svg = badgedFaviconSvg(badge);

    // Assert
    expect(shapesOf(badge).dot.fill).toBe(TAB_BADGE_COLORS.red.hex);
    expect(svg).not.toContain('script');
  });

  it('badgeFill 是圓點實際的顏色', () => {
    // Arrange & Act & Assert
    expect(badgeFill({ ...DEFAULT_TAB_BADGE, color: 'green' })).toBe(TAB_BADGE_COLORS.green.hex);
    expect(badgeFill({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '#abcdef' })).toBe('#abcdef');
    expect(badgeFill({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: null })).toBe(TAB_BADGE_COLORS.red.hex);
  });

  it('位置與大小的選項各有一組', () => {
    // Arrange & Act & Assert
    expect(TAB_BADGE_POSITIONS).toEqual(['top-right', 'bottom-right', 'top-left', 'bottom-left']);
    expect(TAB_BADGE_SIZES).toEqual(['small', 'medium', 'large']);
  });

  it('給分頁用的是 SVG 的資料網址（內容安全政策允許 data: 的圖片）', () => {
    // Arrange & Act
    const url = badgedFaviconUrl(DEFAULT_TAB_BADGE);

    // Assert
    expect(url.startsWith('data:image/svg+xml,')).toBe(true);
    expect(decodeURIComponent(url.slice('data:image/svg+xml,'.length))).toBe(badgedFaviconSvg(DEFAULT_TAB_BADGE));
  });

  it('畫的是同一個標誌：原本的形狀都在遮罩裡', () => {
    // Arrange & Act
    const svg = badgedFaviconSvg(DEFAULT_TAB_BADGE);

    // Assert
    expect(svg).toContain(`<g mask="url(#badge-cut)">${LOGO_SHAPES}</g>`);
  });
});

describe('標誌的來源', () => {
  it('和 public/favicon.svg 是同一份：改標誌時兩邊一起改', () => {
    // Arrange（測試從 web/ 底下執行；favicon.svg 是瀏覽器直接載入的靜態檔，不經過打包，所以由這裡守住）
    const file = readFileSync(resolve(process.cwd(), 'public', 'favicon.svg'), 'utf8').trim();

    // Act & Assert
    expect(LOGO_SVG).toBe(file);
  });
});
