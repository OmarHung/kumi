import { renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BELOW_WIDE_QUERY, COMPACT_QUERY } from './useMediaQuery';
import { useLayoutTier } from './useLayoutTier';

function stubScreen(matching: readonly string[]) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: matching.includes(query),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('useLayoutTier', () => {
  it('沒有 matchMedia（測試環境）是寬螢幕', () => {
    // Arrange / Act
    const { result } = renderHook(() => useLayoutTier());

    // Assert
    expect(result.current).toBe('wide');
  });

  it('只有「不到 64rem」成立是平板', () => {
    // Arrange
    stubScreen([BELOW_WIDE_QUERY]);

    // Act
    const { result } = renderHook(() => useLayoutTier());

    // Assert
    expect(result.current).toBe('tablet');
  });

  it('窄螢幕的查詢成立就是手機，就算「不到 64rem」沒被模擬', () => {
    // Arrange
    stubScreen([COMPACT_QUERY]);

    // Act
    const { result } = renderHook(() => useLayoutTier());

    // Assert
    expect(result.current).toBe('phone');
  });
});
