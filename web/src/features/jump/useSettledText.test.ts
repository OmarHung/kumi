import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useSettledText } from './useSettledText';

describe('useSettledText', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('停下來 300ms 才換成新的字，打字中維持上一個值', () => {
    // Arrange
    const { result, rerender } = renderHook(({ text }) => useSettledText(text), { initialProps: { text: '' } });

    // Act
    rerender({ text: 'abc' });
    const whileTyping = result.current;
    act(() => {
      vi.advanceTimersByTime(300);
    });

    // Assert
    expect(whileTyping).toBe('');
    expect(result.current).toBe('abc');
  });

  it('清空時馬上變空字串；清空後再打字，停下來之前不會回到清空前的字', () => {
    // Arrange
    const { result, rerender } = renderHook(({ text }) => useSettledText(text), { initialProps: { text: 'abc' } });
    act(() => {
      vi.advanceTimersByTime(300);
    });

    // Act
    rerender({ text: '' });
    const afterClear = result.current;
    rerender({ text: 'a' });
    const afterTypingAgain = result.current;
    act(() => {
      vi.advanceTimersByTime(300);
    });

    // Assert
    expect(afterClear).toBe('');
    expect(afterTypingAgain).toBe('');
    expect(result.current).toBe('a');
  });
});
