import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useDrafts } from './useComposerDrafts';

const IVAN = { personId: 'p-1', displayName: 'Ivan' };

function setup(initial: string | undefined) {
  return renderHook(({ id, created }: { id: string | undefined; created: string | null }) => useDrafts(id, null, created), {
    initialProps: { id: initial, created: null as string | null },
  });
}

describe('輸入框草稿與選過的人', () => {
  it('換對話不沿用上一則選過的人；換回來時連選過的人一起還原', () => {
    // Arrange
    const { result, rerender } = setup('c1');
    act(() => {
      result.current.setText('@Ivan 好');
      result.current.addPick(IVAN);
    });

    // Act
    rerender({ id: 'c2', created: null });
    const inOther = result.current.picks;
    rerender({ id: 'c1', created: null });

    // Assert
    expect(inOther).toEqual([]);
    expect(result.current.text).toBe('@Ivan 好');
    expect(result.current.picks).toEqual([IVAN]);
  });

  it('從新的對話送出、網址換成剛開的對話：草稿與選過的人跟過去', () => {
    // Arrange
    const { result, rerender } = setup(undefined);
    act(() => {
      result.current.setText('@Ivan 好');
      result.current.addPick(IVAN);
    });

    // Act
    rerender({ id: 'c9', created: 'c9' });

    // Assert
    expect(result.current.picks).toEqual([IVAN]);
    rerender({ id: undefined, created: null });
    expect(result.current.picks).toEqual([]);
  });

  it('伺服器收下之後清掉選過的人', () => {
    // Arrange
    const { result } = setup('c1');
    act(() => {
      result.current.setText('@Ivan 好');
      result.current.addPick(IVAN);
    });

    // Act
    act(() => {
      result.current.clearSent('c1', '@Ivan 好');
    });

    // Assert
    expect(result.current.picks).toEqual([]);
    expect(result.current.text).toBe('');
  });
});
