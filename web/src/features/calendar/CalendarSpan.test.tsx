import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveDatedItems } from '../../test/calendar';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { serveActivity, created } from '../../test/activity';
import { detailOf, serveItem } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { CALENDAR_DATE_MODE_STORAGE_KEY } from '../../lib/calendarDateMode';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import type { Item } from '../../api/types';
import { UNPLACED_MAX_PAGES } from './useUnplacedCount';

// 2026-11-18（週三）。11 月從 10/26（週一）排到 12/06（週日）：第 2 週 11/02～11/08、第 3 週 11/09～11/15。
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
  localStorage.setItem(CALENDAR_DATE_MODE_STORAGE_KEY, 'span');
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

async function openSpanCalendar(
  items: readonly Item[],
  path = '/projects/AGY?view=calendar',
  pageSize?: number,
  milestones = [milestoneOf('v1.0 上線', '2026-11-26')],
) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveCycles('AGY', []);
  serveMilestones('AGY', milestones);
  const server = serveDatedItems('AGY', items, pageSize);
  const view = renderApp(path);
  const region = await screen.findByRole('region', { name: '行事曆' });
  await waitFor(() => {
    expect(screen.queryByText('正在載入行事曆…')).not.toBeInTheDocument();
  });
  return { ...view, region, server };
}

function item(number: number, startDate: string | null, dueDate: string | null, extra: Partial<Item> = {}): Item {
  return agyItem({ number, title: `項目${String(number)}`, startDate, dueDate, ...extra });
}

/** 一段在週裡的位置：第幾天開始、佔幾天、第幾條車道（都寫在包住連結的 li 上）。 */
function placementOf(link: HTMLElement) {
  const entry = link.closest('li');
  return { col: entry?.dataset.col, span: entry?.dataset.span, lane: entry?.dataset.lane };
}

describe('期間排法：讀資料', () => {
  it('讀和畫面那幾週期間重疊的（datesFrom、datesTo），不帶 dueFrom、dueTo', async () => {
    // Arrange / Act
    const { server } = await openSpanCalendar([item(1, '2026-11-24', '2026-11-26')]);

    // Assert
    await screen.findByRole('link', { name: /AGY-1/ });
    const params = server.requests.at(-1)?.searchParams;
    expect(params?.get('datesFrom')).toBe('2026-10-26');
    expect(params?.get('datesTo')).toBe('2026-12-06');
    expect(params?.get('dueFrom')).toBeNull();
    expect(params?.get('dueTo')).toBeNull();
  });

  it('到期日在畫面之外、但期間跨進畫面的項目也畫出來', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, '2026-11-30', '2026-12-20')]);

    // Assert
    const link = await screen.findByRole('link', { name: 'AGY-1 項目1，11/30～12/20' });
    expect(placementOf(link)).toEqual({ col: '0', span: '7', lane: '0' });
  });

  it('工具列的「沒有到期日」：讀回來把有到期日的拿掉，只畫只有開始日的', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, '2026-11-24', '2026-11-26'), item(2, '2026-11-25', null)], '/projects/AGY?view=calendar&due=none');

    // Assert
    await screen.findByRole('link', { name: /^AGY-2\s?項目2$/ });
    expect(screen.queryByRole('link', { name: /AGY-1/ })).not.toBeInTheDocument();
  });
});

describe('期間排法：條', () => {
  it('跨天的項目畫成一條橫跨那幾天：名稱是「編號 標題，範圍」，title 一樣', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, '2026-11-24', '2026-11-26')]);

    // Assert
    const link = await screen.findByRole('link', { name: 'AGY-1 項目1，11/24～11/26' });
    expect(link).toHaveAttribute('title', 'AGY-1 項目1，11/24～11/26');
    expect(placementOf(link)).toEqual({ col: '1', span: '3', lane: '0' });
    expect(link).toHaveTextContent('項目1');
    expect(link).not.toHaveTextContent('‹');
    expect(link).not.toHaveTextContent('›');
  });

  it('跨年的範圍兩邊都帶年份', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, '2026-12-03', '2027-01-04')], '/projects/AGY?view=calendar&month=2026-12');

    // Assert
    expect(await screen.findAllByRole('link', { name: 'AGY-1 項目1，2026/12/3～2027/1/4' })).not.toHaveLength(0);
  });

  it('跨週的項目每一週各一段：左右接續處有「‹」「›」，只有第一段在 Tab 的順序裡', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, '2026-11-27', '2026-12-02')]);

    // Assert
    const segments = await screen.findAllByRole('link', { name: 'AGY-1 項目1，11/27～12/2' });
    expect(segments).toHaveLength(2);
    const [first, second] = segments;
    expect(placementOf(first as HTMLElement)).toEqual({ col: '4', span: '3', lane: '0' });
    expect(placementOf(second as HTMLElement)).toEqual({ col: '0', span: '3', lane: '0' });
    expect(first).not.toHaveAttribute('tabindex');
    expect(second).toHaveAttribute('tabindex', '-1');
    expect(first).toHaveTextContent('›');
    expect(first).not.toHaveTextContent('‹');
    expect(second).toHaveTextContent('‹');
    expect(second).not.toHaveTextContent('›');
    for (const mark of within(first as HTMLElement).getAllByText('›')) expect(mark).toHaveAttribute('aria-hidden', 'true');
  });

  it('只有到期日、只有開始日的項目是那一天，不是條，名稱沒有範圍；開始日晚於到期日當成只有到期日', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, null, '2026-11-25'), item(2, '2026-11-26', null), item(3, '2026-11-30', '2026-11-27')]);

    // Assert
    expect(placementOf(await screen.findByRole('link', { name: /^AGY-1\s?項目1$/ }))).toEqual({ col: '2', span: '1', lane: '0' });
    expect(placementOf(screen.getByRole('link', { name: /^AGY-2\s?項目2$/ }))).toEqual({ col: '3', span: '1', lane: '0' });
    expect(placementOf(screen.getByRole('link', { name: /^AGY-3\s?項目3$/ }))).toEqual({ col: '4', span: '1', lane: '0' });
  });

  it('不重疊的共用同一條車道，重疊的往下排；先開始的在上面、一樣早開始的長的在上面', async () => {
    // Arrange
    const items = [item(1, '2026-11-23', '2026-11-24'), item(2, '2026-11-25', '2026-11-26'), item(3, '2026-11-23', '2026-11-26'), item(4, '2026-11-24', '2026-11-25')];

    // Act
    await openSpanCalendar(items);

    // Assert
    const lane = async (name: RegExp) => placementOf(await screen.findByRole('link', { name })).lane;
    expect([await lane(/AGY-3 /), await lane(/AGY-1 /), await lane(/AGY-4 /), await lane(/AGY-2 /)]).toEqual(['0', '1', '2', '1']);
  });

  it('一天最多三條車道：多的收進那一天底下的「還有 N 個」，浮層列出全部、跨天的寫日期範圍', async () => {
    // Arrange
    const items = [1, 2, 3, 4, 5].map((number) => item(number, '2026-11-24', '2026-11-25'));

    // Act
    const { user } = await openSpanCalendar(items);

    // Assert
    await screen.findByRole('link', { name: /AGY-1 / });
    expect(screen.queryByRole('link', { name: /AGY-4 / })).not.toBeInTheDocument();
    const buttons = screen.getAllByRole('button', { name: /還有 2 個項目/ });
    expect(buttons.map((button) => button.getAttribute('aria-label'))).toEqual(['11 月 24 日還有 2 個項目', '11 月 25 日還有 2 個項目']);
    await user.click(buttons[0] as HTMLElement);
    const popover = await screen.findByRole('dialog', { name: '11 月 24 日（週二）的項目' });
    expect(within(popover).getAllByRole('link')).toHaveLength(5);
    expect(within(popover).getByRole('link', { name: 'AGY-4 項目4，11/24～11/25' })).toBeInTheDocument();
  });

  it('被收起來的第一段不在 Tab 的順序裡佔位：下一個畫得出來的那一段才是', async () => {
    // Arrange
    const items = [item(1, '2026-11-26', '2026-11-27'), item(2, '2026-11-26', '2026-11-27'), item(3, '2026-11-26', '2026-11-27'), item(4, '2026-11-27', '2026-12-01')];

    // Act
    await openSpanCalendar(items);

    // Assert
    const only = await screen.findAllByRole('link', { name: 'AGY-4 項目4，11/27～12/1' });
    expect(only).toHaveLength(1);
    expect(only[0]).not.toHaveAttribute('tabindex');
  });

  it('過期的（照到期日判斷）標題前有「!」、名稱加「已過期」；單天的項目照舊', async () => {
    // Arrange
    const items = [item(1, '2026-11-02', '2026-11-04'), item(2, null, '2026-11-05'), item(3, '2026-11-02', '2026-11-04', { state: 'done', stateCategory: 'done' })];

    // Act
    await openSpanCalendar(items);

    // Assert
    const bar = await screen.findByRole('link', { name: 'AGY-1 項目1，11/2～11/4（已過期）' });
    expect(within(bar).getByText('!')).toHaveAttribute('aria-hidden', 'true');
    expect(screen.getByRole('link', { name: /^AGY-2\s?項目2（已過期）$/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'AGY-3 項目3，11/2～11/4' })).toBeInTheDocument();
  });

  it('里程碑照舊標在到期日那一格', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, '2026-11-23', '2026-11-27')]);

    // Assert
    const heading = await screen.findByRole('heading', { name: '11 月 26 日（週四）' });
    expect(within(heading.closest('td') as HTMLElement).getByRole('link', { name: /v1.0 上線/ })).toBeInTheDocument();
  });

  it('一天有兩個標示、沒有收起來的項目：整行是「還有 2 個」，浮層列出兩個標示', async () => {
    // Arrange
    const milestones = [milestoneOf('v2.0 上線', '2026-11-24'), milestoneOf('v2.1 上線', '2026-11-24')];

    // Act
    const { user } = await openSpanCalendar([item(1, '2026-11-24', '2026-11-25')], '/projects/AGY?view=calendar', undefined, milestones);

    // Assert
    const more = await screen.findByRole('button', { name: '11 月 24 日還有 2 個項目' });
    expect(screen.queryByRole('link', { name: /v2\.0/ })).not.toBeInTheDocument();
    await user.click(more);
    const popover = await screen.findByRole('dialog', { name: '11 月 24 日（週二）的項目' });
    expect(within(popover).getByRole('link', { name: /v2\.0 上線/ })).toBeInTheDocument();
    expect(within(popover).getByRole('link', { name: /v2\.1 上線/ })).toBeInTheDocument();
  });

  it('點條開項目面板（和到期日排法一樣）', async () => {
    // Arrange
    const bar = item(1, '2026-11-24', '2026-11-26');
    serveItem(detailOf(bar));
    serveActivity(bar.key, [created()]);
    const { user, router } = await openSpanCalendar([bar]);

    // Act
    await user.click(await screen.findByRole('link', { name: /AGY-1 / }));

    // Assert
    expect(await screen.findByRole('complementary', { name: '項目 AGY-1' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/AGY?view=calendar&item=AGY-1');
  });
});

describe('期間排法：沒有日期的提示', () => {
  it('說「N 個項目沒有開始日也沒有到期日，不在月曆上」，只有開始日的不算；連到表格（due=none）', async () => {
    // Arrange
    const items = [item(1, null, null), item(2, null, null), item(3, '2026-11-25', null), item(4, '2026-11-24', '2026-11-26')];

    // Act
    await openSpanCalendar(items);

    // Assert
    const link = await screen.findByRole('link', { name: '2 個項目沒有開始日也沒有到期日' });
    expect(link).toHaveAttribute('href', expect.stringContaining('due=none'));
    expect(link.closest('p')).toHaveTextContent('2 個項目沒有開始日也沒有到期日，不在月曆上。');
  });

  it('只有開始日的佔滿前幾頁：往下多讀，才數得到後面沒有日期的', async () => {
    // Arrange：一頁 2 筆，前 4 筆只有開始日（畫得出來），第 5 筆才是沒有任何日期的。
    const items = [1, 2, 3, 4].map((number) => item(number, '2026-11-24', null)).concat(item(5, null, null));

    // Act
    await openSpanCalendar(items, '/projects/AGY?view=calendar', 2);

    // Assert
    expect(await screen.findByRole('link', { name: '1 個項目沒有開始日也沒有到期日' })).toBeInTheDocument();
  });

  it('最多讀 5 頁：讀到上限仍有下一頁就說「N 個以上」', async () => {
    // Arrange：12 筆都沒有日期，一頁 2 筆，只讀前 5 頁 = 10 筆。
    const items = Array.from({ length: 12 }, (_unused, index) => item(index + 1, null, null));

    // Act
    const { server } = await openSpanCalendar(items, '/projects/AGY?view=calendar', 2);

    // Assert
    expect(await screen.findByRole('link', { name: '10 個以上的項目沒有開始日也沒有到期日' })).toBeInTheDocument();
    expect(server.requests.filter((url) => url.searchParams.get('noDue') === 'true')).toHaveLength(UNPLACED_MAX_PAGES);
  });

  it('全部都有日期時不說', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, '2026-11-24', '2026-11-26'), item(3, '2026-11-25', null)]);

    // Assert
    await screen.findByRole('link', { name: /AGY-1 / });
    expect(screen.queryByText(/沒有開始日也沒有到期日/)).not.toBeInTheDocument();
  });

  it('工具列的到期條件是「已過期」時沒有這回事，不說', async () => {
    // Arrange / Act
    await openSpanCalendar([item(1, null, null)], '/projects/AGY?view=calendar&due=overdue');

    // Assert
    await waitFor(() => {
      expect(screen.queryByText('正在載入行事曆…')).not.toBeInTheDocument();
    });
    expect(screen.queryByText(/沒有開始日也沒有到期日/)).not.toBeInTheDocument();
  });
});

describe('期間排法：窄螢幕', () => {
  it('偏好是「期間」也照到期日排在小月曆：讀 dueFrom、dueTo，沒有條', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));

    // Act
    const { server } = await openSpanCalendar([item(1, '2026-11-24', '2026-11-26')]);

    // Assert
    expect(await screen.findByRole('button', { name: '11 月 26 日，1 個項目，1 個里程碑或週期標示' })).toBeInTheDocument();
    expect(server.requests.some((url) => url.searchParams.get('dueFrom') === '2026-10-26')).toBe(true);
    expect(server.requests.some((url) => url.searchParams.has('datesFrom'))).toBe(false);
    expect(screen.queryByRole('group', { name: '排法' })).not.toBeInTheDocument();
  });
});
