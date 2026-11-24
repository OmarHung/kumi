import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveDatedItems } from '../../test/calendar';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { WEEK_START_STORAGE_KEY } from '../../lib/weekStart';

// 2026-11-18（週三）。從週一開始：10/26～12/06；從週日開始：11/01（週日）～12/05（週六）。
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

async function openCalendar() {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveCycles('AGY', []);
  serveMilestones('AGY', []);
  const server = serveDatedItems('AGY', [agyItem({ number: 1, dueDate: '2026-11-18' })]);
  const view = renderApp('/projects/AGY?view=calendar');
  const region = await screen.findByRole('region', { name: '行事曆' });
  await screen.findByRole('link', { name: /AGY-1/ });
  return { ...view, region, server };
}

function headersOf(region: HTMLElement): (string | null)[] {
  return within(region).getAllByRole('columnheader').map((cell) => cell.textContent);
}

describe('行事曆一週從哪天開始', () => {
  it('預設從週一開始：下拉顯示「週一開始」，表頭與讀資料的範圍照週一排', async () => {
    // Arrange / Act
    const { region, server } = await openCalendar();

    // Assert
    expect(within(region).getByRole('combobox', { name: '一週從' })).toHaveTextContent('週一開始');
    expect(headersOf(region)).toEqual(['週一', '週二', '週三', '週四', '週五', '週六', '週日']);
    expect(server.requests.at(-1)?.searchParams.get('dueFrom')).toBe('2026-10-26');
    expect(server.requests.at(-1)?.searchParams.get('dueTo')).toBe('2026-12-06');
  });

  it('偏好是週日開始：表頭週日在最前面，頭尾補的日子與讀資料的範圍跟著格子走', async () => {
    // Arrange
    localStorage.setItem(WEEK_START_STORAGE_KEY, 'sunday');

    // Act
    const { region, server } = await openCalendar();

    // Assert
    expect(headersOf(region)).toEqual(['週日', '週一', '週二', '週三', '週四', '週五', '週六']);
    const table = within(region).getByRole('table', { name: '2026 年 11 月' });
    expect(within(table).getAllByRole('row')).toHaveLength(6);
    expect(within(table).getByRole('heading', { name: '11 月 1 日（週日）' })).toBeInTheDocument();
    expect(within(table).getByRole('heading', { name: '12 月 5 日（週六）' })).toBeInTheDocument();
    expect(server.requests.at(-1)?.searchParams.get('dueFrom')).toBe('2026-11-01');
    expect(server.requests.at(-1)?.searchParams.get('dueTo')).toBe('2026-12-05');
  });

  it('用月份那一列的下拉切成「週日開始」：馬上生效並記在這個瀏覽器，網址不變', async () => {
    // Arrange
    const { user, region } = await openCalendar();

    // Act
    await chooseOption(user, within(region).getByRole('combobox', { name: '一週從' }), '週日開始');

    // Assert
    await waitFor(() => {
      expect(headersOf(region)[0]).toBe('週日');
    });
    expect(localStorage.getItem(WEEK_START_STORAGE_KEY)).toBe('sunday');
    expect(within(region).getByRole('combobox', { name: '一週從' })).toHaveTextContent('週日開始');
    expect(window.location.search).not.toContain('sunday');
  });

  it('窄螢幕的議程沒有這個下拉：議程依日期列出，和一週從哪天開始無關', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));

    // Act
    const { region } = await openCalendar();

    // Assert
    expect(within(region).queryByRole('combobox', { name: '一週從' })).not.toBeInTheDocument();
  });
});
