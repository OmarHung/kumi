import { useIsBelowWide, useIsCompact } from './useMediaQuery';

/**
 * 外框、頁列與工具列共用的三種寬度（docs/DESIGN.md 6.1、6.3；外框的 phone 是底部分頁列、tablet 是圖示列）：phone 是 48rem 以下，tablet 是 48～64rem，wide 是 64rem 以上。
 * BELOW_WIDE_QUERY 在 48rem 以下也成立，但測試只模擬窄螢幕時它不符合：這裡把窄螢幕一律算進「不到 64rem」，兩邊才一致。
 */
export type LayoutTier = 'phone' | 'tablet' | 'wide';

export function useLayoutTier(): LayoutTier {
  const isCompact = useIsCompact();
  const isBelowWide = useIsBelowWide();
  if (isCompact) return 'phone';
  return isBelowWide ? 'tablet' : 'wide';
}
