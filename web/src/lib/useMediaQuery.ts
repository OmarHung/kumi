import { useCallback, useSyncExternalStore } from 'react';

/** 窄螢幕：和 CSS 的 @media (max-width: 48rem) 同一個值（docs/DESIGN.md 6.3）。 */
export const COMPACT_QUERY = '(max-width: 48rem)';

// 測試環境（jsdom）沒有 matchMedia：當成寬螢幕，既有的測試看到的仍然是表格。
function mediaList(query: string): MediaQueryList | null {
  return 'matchMedia' in window && typeof window.matchMedia === 'function' ? window.matchMedia(query) : null;
}

/** 媒體查詢目前符不符合，變了就重新渲染。只在要「換一種元件」時用；只換樣式的用 CSS。 */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      const list = mediaList(query);
      list?.addEventListener('change', onChange);
      return () => {
        list?.removeEventListener('change', onChange);
      };
    },
    [query],
  );
  return useSyncExternalStore(
    subscribe,
    () => mediaList(query)?.matches ?? false,
    () => false,
  );
}

/** 窄螢幕（抽屜、清單取代表格、單欄）。 */
export function useIsCompact(): boolean {
  return useMediaQuery(COMPACT_QUERY);
}

/**
 * 不到 64rem（CSS 的 @media (min-width: 64rem) 不成立的時候）：側欄之外再放一欄次導覽太擠。
 * 寫成「not all and」而不是 max-width：斷點照第 6.3 節只用 min-width: 64rem，而且沒有 matchMedia 的環境（測試）照樣當成寬螢幕。
 */
export const BELOW_WIDE_QUERY = 'not all and (min-width: 64rem)';

export function useIsBelowWide(): boolean {
  return useMediaQuery(BELOW_WIDE_QUERY);
}
