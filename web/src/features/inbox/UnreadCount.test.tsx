import { focusManager } from '@tanstack/react-query';
import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UNREAD_POLL_MS } from '../../api/notificationQueries';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { notification, serveNotifications } from '../../test/notifications';
import { renderApp } from '../../test/renderApp';

function setup(items = [notification()]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const requests = serveNotifications(items);
  return { requests, ...renderApp('/my-work') };
}

afterEach(() => {
  vi.useRealTimers();
  focusManager.setFocused(undefined);
});

describe('側欄的未讀數怎麼更新', () => {
  it('每 60 秒問一次', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const { requests } = setup();
    await waitFor(() => {
      expect(requests.counts.count).toBe(1);
    });

    // Act
    await act(async () => {
      await vi.advanceTimersByTimeAsync(UNREAD_POLL_MS);
    });

    // Assert
    await waitFor(() => {
      expect(requests.counts.count).toBe(2);
    });
  });

  it('回到分頁時問一次（即使還不到 30 秒）', async () => {
    // Arrange
    const { requests } = setup();
    await waitFor(() => {
      expect(requests.counts.count).toBe(1);
    });

    // Act
    act(() => {
      focusManager.setFocused(false);
      focusManager.setFocused(true);
    });

    // Assert
    await waitFor(() => {
      expect(requests.counts.count).toBe(2);
    });
  });

  it('後來有了新的通知，數字跟著變（不必重新整理）', async () => {
    // Arrange
    const { requests } = setup([]);
    const link = () => within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '收件匣' });
    await waitFor(() => {
      expect(requests.counts.count).toBe(1);
    });
    expect(link()).toHaveAccessibleDescription('');
    serveNotifications([notification(), notification()]);

    // Act
    act(() => {
      focusManager.setFocused(false);
      focusManager.setFocused(true);
    });

    // Assert
    await waitFor(() => {
      expect(link()).toHaveAccessibleDescription('2 則未讀');
    });
  });
});
