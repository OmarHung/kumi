import { normalizeHex } from '../../lib/hexColor';

/**
 * 有未讀時分頁圖示上的圓點（docs/design/08-interaction.md 8.3「收件匣」）：標誌加一個圓點，外圍挖一圈透明的縫，
 * 在任何顏色的分頁列上都和標誌分得開。位置、大小、顏色由使用者在「設定 → 通知」選（tabBadgePreference.ts）。
 *
 * 用 SVG 字串現組，不用 canvas 把 favicon.svg 畫成點陣：不必等圖片載入、不受跨網域限制、放大縮小都是清楚的，
 * 而且是純函式，測得到。代價是標誌的形狀在這裡有一份，和 public/favicon.svg 一不一樣由測試守住。
 */

export type TabBadgePosition = 'top-right' | 'bottom-right' | 'top-left' | 'bottom-left';
export type TabBadgeSize = 'small' | 'medium' | 'large';
export type TabBadgeColor = 'red' | 'orange' | 'yellow' | 'green' | 'blue' | 'pink';
/** 預設的六色之一，或使用者自己調的顏色（customColor）。 */
export type TabBadgeColorChoice = TabBadgeColor | 'custom';

export interface TabBadge {
  readonly position: TabBadgePosition;
  readonly size: TabBadgeSize;
  readonly color: TabBadgeColorChoice;
  /**
   * 自訂的顏色（小寫的 #rrggbb），還沒調過是 null。選回預設的顏色時留著，下次打開「自訂顏色」從它開始。
   */
  readonly customColor: string | null;
}

export const TAB_BADGE_POSITIONS: readonly TabBadgePosition[] = ['top-right', 'bottom-right', 'top-left', 'bottom-left'];
export const TAB_BADGE_SIZES: readonly TabBadgeSize[] = ['small', 'medium', 'large'];

/**
 * 圖示的顏色是固定的（和標誌一樣不跟著主題換），所以寫死色碼；每一種在淺色與深色的分頁列上都看得到，
 * 也和標誌的紫藍分得開（所以沒有紫色）。
 */
export const TAB_BADGE_COLORS: Readonly<Record<TabBadgeColor, { readonly label: string; readonly hex: string }>> = {
  red: { label: '紅色', hex: '#e5322b' },
  orange: { label: '橘色', hex: '#f2780c' },
  yellow: { label: '黃色', hex: '#f5b800' },
  green: { label: '綠色', hex: '#1f9d55' },
  blue: { label: '藍色', hex: '#2f7ff5' },
  pink: { label: '粉紅色', hex: '#e8459a' },
};

export const TAB_BADGE_COLOR_KEYS = Object.keys(TAB_BADGE_COLORS) as readonly TabBadgeColor[];

const DEFAULT_TAB_BADGE_COLOR: TabBadgeColor = 'red';

export const DEFAULT_TAB_BADGE: TabBadge = { position: 'top-right', size: 'medium', color: DEFAULT_TAB_BADGE_COLOR, customColor: null };

/**
 * 圓點實際的顏色。自訂的色碼一律再檢查一次才用：它會原樣放進 SVG 的字串，不合格式（不可信的資料）時畫成預設的紅色。
 */
export function badgeFill(badge: TabBadge): string {
  if (badge.color !== 'custom') return TAB_BADGE_COLORS[badge.color].hex;
  const custom = badge.customColor === null ? null : normalizeHex(badge.customColor);
  return custom ?? TAB_BADGE_COLORS[DEFAULT_TAB_BADGE_COLOR].hex;
}

/** 標誌是 32×32 的畫布（public/favicon.svg）。 */
const VIEWBOX = 32;

/** 圓點的半徑（畫布的單位）：分頁上 16px 的圖示裡大約是 5、7、9px 的點。 */
const RADIUS: Readonly<Record<TabBadgeSize, number>> = { small: 5.5, medium: 7.5, large: 9.5 };

/** 圓點外圍挖掉的縫，與圓點離圖示邊緣的距離。 */
const GAP = 2;
const EDGE = 0.5;

/** 標誌的形狀（public/favicon.svg 裡 <svg> 的內容）。 */
export const LOGO_SHAPES =
  '<rect width="32" height="32" rx="7" fill="#5a4ae3"/>' +
  '<rect x="5" y="5" width="14" height="6" rx="1" fill="#fff"/>' +
  '<rect x="21" y="5" width="6" height="14" rx="1" fill="#fff"/>' +
  '<rect x="13" y="21" width="14" height="6" rx="1" fill="#fff"/>' +
  '<rect x="5" y="13" width="6" height="14" rx="1" fill="#fff"/>' +
  '<rect x="13" y="13" width="6" height="6" rx="1" fill="#c9c1ff"/>';

const SVG_OPEN = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${String(VIEWBOX)} ${String(VIEWBOX)}">`;

/** 沒有圓點的標誌，和 public/favicon.svg 一字不差。 */
export const LOGO_SVG = `${SVG_OPEN}${LOGO_SHAPES}</svg>`;

/** 圓點貼著那一個角（留 EDGE），半徑變大時圓心往內移，不會超出圖示。 */
function centerOf(position: TabBadgePosition, radius: number): { readonly cx: number; readonly cy: number } {
  const near = radius + EDGE;
  const far = VIEWBOX - radius - EDGE;
  const isRight = position === 'top-right' || position === 'bottom-right';
  const isBottom = position === 'bottom-right' || position === 'bottom-left';
  return { cx: isRight ? far : near, cy: isBottom ? far : near };
}

export function badgedFaviconSvg(badge: TabBadge): string {
  const radius = RADIUS[badge.size];
  const { cx, cy } = centerOf(badge.position, radius);
  const at = `cx="${String(cx)}" cy="${String(cy)}"`;
  // 遮罩：白色留著、黑色挖掉；挖掉的圓比圓點大一圈，就是那一圈透明的縫。
  const mask =
    `<defs><mask id="badge-cut"><rect width="${String(VIEWBOX)}" height="${String(VIEWBOX)}" fill="#fff"/>` +
    `<circle ${at} r="${String(radius + GAP)}" fill="#000"/></mask></defs>`;
  const dot = `<circle ${at} r="${String(radius)}" fill="${badgeFill(badge)}"/>`;
  return `${SVG_OPEN}${mask}<g mask="url(#badge-cut)">${LOGO_SHAPES}</g>${dot}</svg>`;
}

/** 給 <link rel="icon"> 與示意圖的 <img> 用；內容安全政策的 img-src 允許 data:。 */
export function badgedFaviconUrl(badge: TabBadge): string {
  return `data:image/svg+xml,${encodeURIComponent(badgedFaviconSvg(badge))}`;
}
