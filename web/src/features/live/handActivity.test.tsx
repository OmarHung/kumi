import { QueryClient } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createFakeLiveConnection } from '../../test/liveConnection';
import { isHandBusy, useReportHandBusy } from './handActivity';
import { startLiveUpdates } from './startLiveUpdates';

describe('拖曳中', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('拖曳中的訊息先記著，放下之後才失效；畫面被換掉也算結束', async () => {
    // Arrange
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const connection = createFakeLiveConnection();
    startLiveUpdates(queryClient, connection);
    await vi.advanceTimersByTimeAsync(0);
    const drag = renderHook(({ active }) => { useReportHandBusy(active); }, { initialProps: { active: true } });
    expect(isHandBusy()).toBe(true);

    // Act
    connection.emitItems({ projectKey: 'MNT', itemKeys: ['MNT-1'] });
    await vi.advanceTimersByTimeAsync(1_000);
    expect(invalidate).not.toHaveBeenCalled();
    act(() => {
      drag.unmount();
    });
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(isHandBusy()).toBe(false);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['item', 'MNT-1'] });
  });
});
