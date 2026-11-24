import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY, useIsCompact, useMediaQuery } from './useMediaQuery';

afterEach(() => {
  vi.unstubAllGlobals();
});

/** 假的 matchMedia：記住每個查詢的結果，change() 換掉結果並通知。 */
function stubMatchMedia(initial: boolean) {
  let matches = initial;
  const listeners = new Set<() => void>();
  const queries: string[] = [];
  vi.stubGlobal('matchMedia', (query: string) => {
    queries.push(query);
    return {
      get matches() {
        return matches;
      },
      addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
      removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
    };
  });
  return {
    queries,
    listeners,
    change(next: boolean) {
      matches = next;
      listeners.forEach((listener) => {
        listener();
      });
    },
  };
}

describe('useMediaQuery', () => {
  it('沒有 matchMedia（測試環境）時當成不符合：寬螢幕', () => {
    // Arrange
    vi.stubGlobal('matchMedia', undefined);

    // Act
    const { result } = renderHook(() => useMediaQuery('(max-width: 48rem)'));

    // Assert
    expect(result.current).toBe(false);
  });

  it('回傳查詢的結果，結果變了就跟著變', () => {
    // Arrange
    const media = stubMatchMedia(false);
    const { result } = renderHook(() => useMediaQuery('(pointer: coarse)'));

    // Act
    act(() => {
      media.change(true);
    });

    // Assert
    expect(result.current).toBe(true);
    expect(media.queries).toContain('(pointer: coarse)');
  });

  it('卸下之後不再聽', () => {
    // Arrange
    const media = stubMatchMedia(false);
    const { unmount } = renderHook(() => useMediaQuery('(pointer: coarse)'));

    // Act
    unmount();

    // Assert
    expect(media.listeners.size).toBe(0);
  });
});

describe('useIsCompact', () => {
  it('用和 CSS 同一個斷點判斷窄螢幕', () => {
    // Arrange
    const media = stubMatchMedia(true);

    // Act
    const { result } = renderHook(() => useIsCompact());

    // Assert
    expect(result.current).toBe(true);
    expect(COMPACT_QUERY).toBe('(max-width: 48rem)');
    expect(media.queries).toContain(COMPACT_QUERY);
  });
});
