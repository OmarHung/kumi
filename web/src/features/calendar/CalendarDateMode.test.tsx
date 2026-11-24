import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveDatedItems } from '../../test/calendar';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { CALENDAR_DATE_MODE_STORAGE_KEY } from '../../lib/calendarDateMode';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openCalendar() {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveCycles('AGY', []);
  serveMilestones('AGY', []);
  const server = serveDatedItems('AGY', [agyItem({ number: 1, startDate: '2026-11-16', dueDate: '2026-11-18' })]);
  const view = renderApp('/projects/AGY?view=calendar');
  const region = await screen.findByRole('region', { name: '行事曆' });
  await screen.findByRole('link', { name: /AGY-1/ });
  return { ...view, region, server };
}

function modeGroup(region: HTMLElement) {
  return within(region).getByRole('group', { name: '排法' });
}

describe('行事曆的排法切換', () => {
  it('預設「到期日」：選項依序「到期日」「期間」，讀 dueFrom、dueTo，網址與畫面照舊', async () => {
    // Arrange / Act
    const { region, server } = await openCalendar();

    // Assert
    const buttons = within(modeGroup(region)).getAllByRole('button');
    expect(buttons.map((button) => button.textContent)).toEqual(['到期日', '期間']);
    expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual(['true', 'false']);
    expect(server.requests.at(-1)?.searchParams.get('dueFrom')).toBe('2026-10-26');
    expect(server.requests.at(-1)?.searchParams.get('datesFrom')).toBeNull();
  });

  it('切到「期間」：馬上生效並記在這個瀏覽器，網址不變；改讀 datesFrom、datesTo', async () => {
    // Arrange
    const { user, region, server } = await openCalendar();

    // Act
    await user.click(within(modeGroup(region)).getByRole('button', { name: '期間' }));

    // Assert
    await waitFor(() => {
      expect(within(modeGroup(region)).getByRole('button', { name: '期間' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(localStorage.getItem(CALENDAR_DATE_MODE_STORAGE_KEY)).toBe('span');
    expect(window.location.search).not.toContain('span');
    await waitFor(() => {
      expect(server.requests.at(-1)?.searchParams.get('datesFrom')).toBe('2026-10-26');
    });
    expect(await screen.findByRole('link', { name: 'AGY-1 首頁設計稿，11/16～11/18' })).toBeInTheDocument();
  });

  it('記住：下次打開還是「期間」；再切回「到期日」也記住', async () => {
    // Arrange
    localStorage.setItem(CALENDAR_DATE_MODE_STORAGE_KEY, 'span');

    // Act
    const { user, region } = await openCalendar();

    // Assert
    expect(within(modeGroup(region)).getByRole('button', { name: '期間' })).toHaveAttribute('aria-pressed', 'true');
    await user.click(within(modeGroup(region)).getByRole('button', { name: '到期日' }));
    expect(localStorage.getItem(CALENDAR_DATE_MODE_STORAGE_KEY)).toBe('due');
  });

  it('記下的值不認得時用預設「到期日」', async () => {
    // Arrange
    localStorage.setItem(CALENDAR_DATE_MODE_STORAGE_KEY, 'weekly');

    // Act
    const { region } = await openCalendar();

    // Assert
    expect(within(modeGroup(region)).getByRole('button', { name: '到期日' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('localStorage 寫不進去（私密瀏覽）時這一頁照樣切得過去', async () => {
    // Arrange
    const { user, region } = await openCalendar();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('quota', 'QuotaExceededError');
    });

    // Act
    await user.click(within(modeGroup(region)).getByRole('button', { name: '期間' }));

    // Assert
    await waitFor(() => {
      expect(within(modeGroup(region)).getByRole('button', { name: '期間' })).toHaveAttribute('aria-pressed', 'true');
    });
  });

  it('窄螢幕的議程沒有這個切換', async () => {
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
    expect(within(region).queryByRole('group', { name: '排法' })).not.toBeInTheDocument();
  });
});
