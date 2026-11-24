import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

function myWorkLink(): HTMLElement {
  return within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '我的工作' });
}

afterEach(() => {
  vi.useRealTimers();
});

describe('側欄的過期數：分頁開過夜', () => {
  it('視窗再取得焦點時重新取今天：昨天到期的變成過期', async () => {
    // Arrange：只換掉 Date（計時器照常），「今天」是 2026-11-18。
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 10, 18, 9));
    const dueWednesday = makeItem({ number: 1, title: '寄出報價', dueDate: '2026-11-18' });
    signedInAs(exampleOwner, { projects: [webProject], myWork: [dueWednesday] });
    const { router } = renderApp('/my-work');
    await screen.findByRole('table', { name: '今天' });
    await router.navigate('/projects');
    await screen.findByRole('heading', { level: 1, name: '專案' });
    await waitFor(() => {
      expect(myWorkLink()).toHaveAccessibleDescription('1 項');
    });

    // Act：過了一夜，回到這個分頁。
    vi.setSystemTime(new Date(2026, 10, 19, 9));
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });

    // Assert
    await waitFor(() => {
      expect(myWorkLink()).toHaveAccessibleDescription('1 項、1 項已過期');
    });
  });

  it('分頁從背景回到前景（visibilitychange）也會重新取今天', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 10, 18, 9));
    const dueWednesday = makeItem({ number: 1, title: '寄出報價', dueDate: '2026-11-18' });
    signedInAs(exampleOwner, { projects: [webProject], myWork: [dueWednesday] });
    const { router } = renderApp('/my-work');
    await screen.findByRole('table', { name: '今天' });
    await router.navigate('/projects');
    await screen.findByRole('heading', { level: 1, name: '專案' });

    // Act
    vi.setSystemTime(new Date(2026, 10, 19, 9));
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });

    // Assert
    await waitFor(() => {
      expect(myWorkLink()).toHaveAccessibleDescription('1 項、1 項已過期');
    });
  });
});
