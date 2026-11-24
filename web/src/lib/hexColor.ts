/**
 * 色碼（#rrggbb）與 HSV（色相、飽和度、亮度）之間的換算，給自己畫的取色器用（features/account/CustomColorPicker）。
 * 用 HSV 而不是 HSL：取色器的方塊左右是飽和度、上下是亮度，右上角就是純色，和大多數的設計工具一樣。
 */

export interface Hsv {
  /** 0～360（不含 360）。 */
  readonly h: number;
  /** 0～1。 */
  readonly s: number;
  /** 0～1。 */
  readonly v: number;
}

const SHORT_HEX = /^#?([0-9a-f])([0-9a-f])([0-9a-f])$/i;
const LONG_HEX = /^#?([0-9a-f]{6})$/i;

/**
 * 使用者打的或存起來的色碼整理成小寫的 #rrggbb；可以不打 #、可以是三碼的簡寫。看不懂的是 null。
 * 色碼會原樣放進 SVG 的字串裡（分頁的圖示），所以只收這個格式，不收顏色名稱或 rgb()。
 */
export function normalizeHex(value: string): string | null {
  const text = value.trim();
  const short = SHORT_HEX.exec(text);
  if (short !== null) return `#${short.slice(1).map((digit) => digit + digit).join('')}`.toLowerCase();
  const long = LONG_HEX.exec(text);
  return long === null ? null : `#${long[1]}`.toLowerCase();
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value));
}

/** 只收 normalizeHex 整理過的色碼。 */
export function hexToHsv(hex: string): Hsv {
  const channel = (offset: number) => Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
  const [r, g, b] = [channel(1), channel(3), channel(5)];
  const max = Math.max(r, g, b);
  const delta = max - Math.min(r, g, b);
  const s = max === 0 ? 0 : delta / max;
  if (delta === 0) return { h: 0, s, v: max };
  const sector = max === r ? (g - b) / delta : max === g ? (b - r) / delta + 2 : (r - g) / delta + 4;
  return { h: (sector * 60 + 360) % 360, s, v: max };
}

export function hsvToHex({ h, s, v }: Hsv): string {
  const hue = (((h % 360) + 360) % 360) / 60;
  const [sat, value] = [clamp01(s), clamp01(v)];
  const channel = (n: number) => {
    const k = (n + hue) % 6;
    const level = value - value * sat * Math.max(0, Math.min(k, 4 - k, 1));
    return Math.round(level * 255).toString(16).padStart(2, '0');
  };
  return `#${channel(5)}${channel(3)}${channel(1)}`;
}
