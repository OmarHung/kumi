import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveDatedItems } from '../../test/calendar';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { replaceItemInCache } from '../item-table/itemCache';
import { serveActivity, created } from '../../test/activity';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import type { Item } from '../../api/types';

// 2026-11-18（週三）。11 月的月曆從 10/26（週一）排到 12/06（週日）。
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const SPRINT = '0190a000-0000-7000-8000-0000000000d2';
const V1 = '0190a000-0000-7000-8000-0000000000e1';

interface Options {
  readonly cycles?: ReturnType<typeof cycleOf>[];
  readonly milestones?: ReturnType<typeof milestoneOf>[];
  readonly pageSize?: number;
}

async function openCalendar(items: readonly Item[], path = '/projects/AGY?view=calendar', options: Options = {}) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveCycles('AGY', options.cycles ?? []);
  serveMilestones('AGY', options.milestones ?? []);
  const server = serveDatedItems('AGY', items, options.pageSize);
  const view = renderApp(path);
  const region = await screen.findByRole('region', { name: '行事曆' });
  await waitFor(() => {
    expect(screen.queryByText('正在載入行事曆…')).not.toBeInTheDocument();
  });
  return { ...view, region, server };
}

/** 月曆上某一天的格子：用那一格的標題找。 */
function dayCell(heading: string): HTMLElement {
  // 今天那一格的標題多一句「（今天）」，給報讀軟體。
  const cell = screen.getByRole('heading', { name: (name) => name === heading || name === `${heading}（今天）` }).closest('td');
  if (cell === null) throw new Error(`找不到 ${heading} 的格子`);
  return cell;
}

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

describe('行事曆的月曆', () => {
  it('預設是今天所在的月：標題是「2026 年 11 月」，表頭週一到週日，頭尾補上下個月的日子', async () => {
    // Arrange / Act
    const { region } = await openCalendar([]);

    // Assert
    expect(within(region).getByRole('heading', { level: 2, name: '2026 年 11 月' })).toBeInTheDocument();
    const table = within(region).getByRole('table', { name: '2026 年 11 月' });
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual(['週一', '週二', '週三', '週四', '週五', '週六', '週日']);
    expect(within(table).getAllByRole('row')).toHaveLength(7);
    expect(within(table).getByRole('heading', { name: '10 月 26 日（週一）' })).toBeInTheDocument();
    expect(within(table).getByRole('heading', { name: '12 月 6 日（週日）' })).toBeInTheDocument();
  });

  it('依畫面上看得到的那幾週讀到期日，依到期日排', async () => {
    // Arrange / Act
    const { server } = await openCalendar([agyItem({ number: 1, dueDate: '2026-11-18' })]);

    // Assert
    await screen.findByRole('link', { name: /AGY-1/ });
    const params = server.requests.at(-1)?.searchParams;
    expect(params?.get('dueFrom')).toBe('2026-10-26');
    expect(params?.get('dueTo')).toBe('2026-12-06');
    expect(params?.get('sort')).toBe('due_date:asc');
    expect(params?.get('limit')).toBe('200');
  });

  it('項目排在到期日那一天；每一天依優先度、再依編號', async () => {
    // Arrange
    const items = [
      agyItem({ number: 3, title: '低的', dueDate: '2026-11-18', priority: 'low' }),
      agyItem({ number: 5, title: '急的', dueDate: '2026-11-18', priority: 'urgent' }),
      agyItem({ number: 2, title: '另一個低的', dueDate: '2026-11-18', priority: 'low' }),
      agyItem({ number: 4, title: '隔天', dueDate: '2026-11-19' }),
    ];

    // Act
    await openCalendar(items);

    // Assert
    await screen.findByRole('link', { name: /AGY-5/ });
    const today = within(dayCell('11 月 18 日（週三）')).getAllByRole('link');
    expect(today.map((link) => link.textContent)).toEqual(['AGY-5急的', 'AGY-2另一個低的', 'AGY-3低的']);
    expect(within(dayCell('11 月 19 日（週四）')).getByRole('link', { name: /AGY-4/ })).toBeInTheDocument();
  });

  it('今天那一格 aria-current="date"，標題多一句「（今天）」；別的格子沒有', async () => {
    // Arrange / Act
    await openCalendar([]);

    // Assert
    expect(dayCell('11 月 18 日（週三）')).toHaveAttribute('aria-current', 'date');
    expect(screen.getByRole('heading', { name: '11 月 18 日（週三）（今天）' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: '11 月 19 日（週四）（今天）' })).not.toBeInTheDocument();
    expect(dayCell('11 月 19 日（週四）')).not.toHaveAttribute('aria-current');
  });

  it('一天最多 3 行（「還有 N 個」也算一行）：放不下時畫 2 筆，其餘是「還有 N 個」；按了用浮層列出那一天的全部', async () => {
    // Arrange
    const items = [1, 2, 3, 4, 5].map((number) => agyItem({ number, title: `事項${String(number)}`, dueDate: '2026-11-18' }));
    const { user } = await openCalendar(items);
    const cell = dayCell('11 月 18 日（週三）');
    await within(cell).findByRole('link', { name: /AGY-1/ });
    expect(within(cell).getAllByRole('link')).toHaveLength(2);

    // Act
    await user.click(within(cell).getByRole('button', { name: '11 月 18 日還有 3 個項目' }));

    // Assert
    const popover = await screen.findByRole('dialog', { name: '11 月 18 日（週三）的項目' });
    expect(within(popover).getAllByRole('link').map((link) => link.textContent)).toEqual(['AGY-1事項1', 'AGY-2事項2', 'AGY-3事項3', 'AGY-4事項4', 'AGY-5事項5']);
  });

  it('里程碑也算一行：里程碑加 3 筆項目時只畫里程碑與 1 筆，「還有 2 個」', async () => {
    // Arrange
    const items = [1, 2, 3].map((number) => agyItem({ number, title: `事項${String(number)}`, dueDate: '2026-11-20' }));
    const milestones = [milestoneOf('年終檔期上線', '2026-11-20', { id: V1 })];
    const { user } = await openCalendar(items, undefined, { milestones });
    const cell = dayCell('11 月 20 日（週五）');
    await within(cell).findByRole('link', { name: /AGY-1/ });

    // Assert
    expect(within(cell).getAllByRole('link').map((link) => link.textContent)).toEqual(['◆ 年終檔期上線', 'AGY-1事項1']);
    const more = within(cell).getByRole('button', { name: '11 月 20 日還有 2 個項目' });
    expect(more).toHaveTextContent('還有 2 個');

    // Act
    await user.click(more);

    // Assert：浮層列出那一天的全部（里程碑與 3 筆）
    const popover = await screen.findByRole('dialog', { name: '11 月 20 日（週五）的項目' });
    expect(within(popover).getAllByRole('link')).toHaveLength(4);
  });

  it('格子裡的項目只顯示標題：編號放在連結的名稱與 title，議程照舊顯示編號', async () => {
    // Arrange
    const item = agyItem({ number: 1, title: '年度回顧文章', dueDate: '2026-11-20' });

    // Act
    await openCalendar([item]);

    // Assert
    const link = await within(dayCell('11 月 20 日（週五）')).findByRole('link', { name: /AGY-1/ });
    expect(link).toHaveAttribute('title', 'AGY-1 年度回顧文章');
    expect(link.querySelector('.visually-hidden')).toHaveTextContent('AGY-1');
  });

  it('剛好 3 筆沒有「還有」', async () => {
    // Arrange / Act
    await openCalendar([1, 2, 3].map((number) => agyItem({ number, dueDate: '2026-11-18' })));

    // Assert
    await screen.findByRole('link', { name: /AGY-3/ });
    expect(screen.queryByRole('button', { name: /還有/ })).not.toBeInTheDocument();
  });

  it('過期的項目（到期日已過、還沒完成）標「已過期」，完成的不標', async () => {
    // Arrange
    const items = [
      agyItem({ number: 1, title: '拖延了', dueDate: '2026-11-10' }),
      agyItem({ number: 2, title: '已完成', dueDate: '2026-11-10', state: 'closed', stateCategory: 'done' }),
    ];

    // Act
    await openCalendar(items);

    // Assert
    expect(await screen.findByRole('link', { name: /AGY-1.*已過期/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'AGY-2已完成' })).toBeInTheDocument();
  });
});

describe('行事曆的里程碑與週期', () => {
  it('里程碑標在到期日、連到表格篩這個里程碑；週期標在開始日與結束日', async () => {
    // Arrange
    const cycles = [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT })];
    const milestones = [milestoneOf('年終檔期上線', '2026-11-20', { id: V1 })];

    // Act
    await openCalendar([], '/projects/AGY?view=calendar', { cycles, milestones });

    // Assert
    const marker = await within(dayCell('11 月 20 日（週五）')).findByRole('link', { name: /年終檔期上線/ });
    expect(marker).toHaveTextContent('◆ 年終檔期上線');
    expect(marker).toHaveAttribute('href', `/projects/AGY?milestone=${V1}`);
    expect(within(dayCell('11 月 16 日（週一）')).getByText('衝刺 2 開始')).toBeInTheDocument();
    expect(within(dayCell('11 月 29 日（週日）')).getByText('衝刺 2 結束')).toBeInTheDocument();
  });

  it('里程碑排在那一天的項目前面', async () => {
    // Arrange
    const milestones = [milestoneOf('里程碑', '2026-11-20', { id: V1 })];

    // Act
    await openCalendar([agyItem({ number: 1, dueDate: '2026-11-20' })], undefined, { milestones });

    // Assert
    await screen.findByRole('link', { name: /AGY-1/ });
    const links = within(dayCell('11 月 20 日（週五）')).getAllByRole('link');
    expect(links[0]).toHaveTextContent('里程碑');
  });
});

describe('行事曆的月份', () => {
  it('「下個月」「上個月」換月份寫在網址的 month，「今天」回到當月（網址不留 month）', async () => {
    // Arrange
    const { user, router, region } = await openCalendar([]);

    // Act / Assert
    await user.click(within(region).getByRole('link', { name: '下個月 ›' }));
    expect(currentPath(router)).toBe('/projects/AGY?view=calendar&month=2026-12');
    expect(await within(region).findByRole('heading', { level: 2, name: '2026 年 12 月' })).toBeInTheDocument();
    await user.click(within(region).getByRole('link', { name: '‹ 上個月' }));
    await user.click(within(region).getByRole('link', { name: '‹ 上個月' }));
    expect(currentPath(router)).toBe('/projects/AGY?view=calendar&month=2026-10');
    await user.click(within(region).getByRole('link', { name: '今天' }));
    expect(currentPath(router)).toBe('/projects/AGY?view=calendar');
    expect(await within(region).findByRole('heading', { level: 2, name: '2026 年 11 月' })).toBeInTheDocument();
  });

  it('網址上的月份：照它讀那個月的範圍；打錯的月份當成今天所在的月', async () => {
    // Arrange / Act
    const { server } = await openCalendar([], '/projects/AGY?view=calendar&month=2027-02');

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: '2027 年 2 月' })).toBeInTheDocument();
    expect(server.requests.at(-1)?.searchParams.get('dueFrom')).toBe('2027-02-01');
    expect(server.requests.at(-1)?.searchParams.get('dueTo')).toBe('2027-02-28');
  });

  it('月份打錯時回到今天所在的月', async () => {
    // Arrange / Act
    const { region } = await openCalendar([], '/projects/AGY?view=calendar&month=nope');

    // Assert
    expect(within(region).getByRole('heading', { level: 2, name: '2026 年 11 月' })).toBeInTheDocument();
  });

  it('換篩選條件時月份留著', async () => {
    // Arrange
    const { user, router } = await openCalendar([], '/projects/AGY?view=calendar&month=2026-12');
    const search = screen.getByRole('searchbox', { name: '搜尋' });

    // Act
    await user.type(search, '首頁{Enter}');

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY?view=calendar&q=%E9%A6%96%E9%A0%81&month=2026-12');
  });
});

describe('沒有到期日的項目', () => {
  it('上方說有幾個，連到表格並篩「沒有到期日」', async () => {
    // Arrange
    const items = [agyItem({ number: 1, dueDate: null }), agyItem({ number: 2, dueDate: null }), agyItem({ number: 3, dueDate: '2026-11-18' })];

    // Act
    await openCalendar(items, '/projects/AGY?view=calendar&assignee=unassigned');

    // Assert
    const link = await screen.findByRole('link', { name: '2 個項目沒有到期日' });
    expect(link).toHaveAttribute('href', '/projects/AGY?assignee=unassigned&due=none');
  });

  it('都有到期日時不說；篩了「本週到期」時也不說（沒有到期日的一定不符合）', async () => {
    // Arrange / Act
    await openCalendar([agyItem({ number: 1, dueDate: '2026-11-18' })], '/projects/AGY?view=calendar&due=week');

    // Assert
    await screen.findByRole('link', { name: /AGY-1/ });
    expect(screen.queryByText(/沒有到期日/)).not.toBeInTheDocument();
  });
});

describe('面板裡改了日期之後', () => {
  it('補上本月的到期日：項目出現在那一天，「沒有到期日」的數字跟著少一個', async () => {
    // Arrange
    const items = [agyItem({ number: 1, title: '補日期的', dueDate: null }), agyItem({ number: 2, dueDate: null })];
    const { queryClient } = await openCalendar(items);
    await screen.findByRole('link', { name: '2 個項目沒有到期日' });

    // Act：面板的修改先到伺服器，再換進快取（itemCache.ts）
    const edited = { ...items[0] as Item, dueDate: '2026-11-20' };
    items[0] = edited;
    replaceItemInCache(queryClient, detailOf(edited));

    // Assert
    expect(await within(dayCell('11 月 20 日（週五）')).findByRole('link', { name: /補日期的/ })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: '1 個項目沒有到期日' })).toBeInTheDocument();
  });

  it('把到期日改到別的月：項目從那一格消失，「沒有到期日」不變', async () => {
    // Arrange
    const items = [agyItem({ number: 1, title: '要搬走的', dueDate: '2026-11-20' })];
    const { queryClient } = await openCalendar(items);
    await within(dayCell('11 月 20 日（週五）')).findByRole('link', { name: /要搬走的/ });

    // Act
    const edited = { ...items[0] as Item, dueDate: '2027-03-01' };
    items[0] = edited;
    replaceItemInCache(queryClient, detailOf(edited));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('link', { name: /要搬走的/ })).not.toBeInTheDocument();
    });
  });
});

describe('讀很多頁', () => {
  it('一頁讀不完就接著讀，每一頁都排上去', async () => {
    // Arrange：一頁 2 筆，5 筆要讀 3 頁。
    const items = [1, 2, 3, 4, 5].map((number) => agyItem({ number, dueDate: `2026-11-${String(number + 9)}` }));

    // Act
    const { server } = await openCalendar(items, undefined, { pageSize: 2 });

    // Assert
    await screen.findByRole('link', { name: /AGY-5/ });
    expect(screen.getAllByRole('link', { name: /^AGY-/ })).toHaveLength(5);
    expect(server.requests.filter((url) => url.searchParams.get('noDue') !== 'true')).toHaveLength(3);
  });

  it('超過 1,000 筆：畫前 1,000 筆，上方說明', async () => {
    // Arrange：1,001 筆，全在同一天（放不下的收進「還有 N 個」）。
    const items = Array.from({ length: 1001 }, (_, index) => agyItem({ number: index + 1, dueDate: '2026-11-18' }));

    // Act
    await openCalendar(items, undefined, { pageSize: 200 });

    // Assert
    expect(await screen.findByText('這段時間的項目超過 1,000 個，只顯示前 1,000 個；用篩選縮小範圍。')).toBeInTheDocument();
    expect(within(dayCell('11 月 18 日（週三）')).getByRole('button', { name: '11 月 18 日還有 998 個項目' })).toBeInTheDocument();
  });

  it('剛好 1,000 筆不算超過', async () => {
    // Arrange
    const items = Array.from({ length: 1000 }, (_, index) => agyItem({ number: index + 1, dueDate: '2026-11-18' }));

    // Act
    await openCalendar(items, undefined, { pageSize: 200 });

    // Assert
    expect(await within(dayCell('11 月 18 日（週三）')).findByRole('button', { name: '11 月 18 日還有 998 個項目' })).toBeInTheDocument();
    expect(screen.queryByText(/只顯示前 1,000 個/)).not.toBeInTheDocument();
  });
});

describe('行事曆吃工具列的篩選', () => {
  it('負責人、優先度、搜尋、週期、里程碑一起送出；到期條件和畫面的範圍取交集', async () => {
    // Arrange
    const cycles = [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT })];
    const path = `/projects/AGY?view=calendar&assignee=me&priority=high&q=官網&cycle=current&milestone=${V1}&due=week`;

    // Act
    const { server } = await openCalendar([], path, { cycles });

    // Assert
    await screen.findByRole('table', { name: '2026 年 11 月' });
    const params = server.requests.find((url) => url.searchParams.has('dueFrom'))?.searchParams;
    expect(params?.get('assignee')).toBe('me');
    expect(params?.getAll('priority')).toEqual(['high']);
    expect(params?.get('q')).toBe('官網');
    expect(params?.get('cycle')).toBe(SPRINT);
    expect(params?.get('milestone')).toBe(V1);
    expect(params?.get('dueFrom')).toBe('2026-11-18');
    expect(params?.get('dueTo')).toBe('2026-11-22');
  });

  it('工具列有類型與狀態：選了寫進網址、送進請求，月份留著；換類型時不在新流程裡的狀態清掉', async () => {
    // Arrange
    const { user, router, server } = await openCalendar([], '/projects/AGY?view=calendar&month=2026-12');
    const bar = screen.getByRole('group', { name: '篩選行事曆' });

    // Act
    await chooseOption(user, within(bar).getByRole('combobox', { name: '類型' }), '任務');
    await chooseOption(user, within(bar).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/AGY?view=calendar&type=task&state=doing&month=2026-12');
    });
    await waitFor(() => {
      const last = server.requests.at(-1)?.searchParams;
      expect(last?.getAll('type')).toEqual(['task']);
      expect(last?.getAll('state')).toEqual(['doing']);
    });
  });

  it('網址帶類型與狀態直接打開：請求帶著，「清除條件」一次清掉', async () => {
    // Arrange
    const { user, router, server } = await openCalendar([], '/projects/AGY?view=calendar&type=task&state=doing');

    // Assert
    expect(server.requests.some((url) => url.searchParams.getAll('state').includes('doing'))).toBe(true);

    // Act
    await user.click(screen.getByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/AGY?view=calendar');
    });
  });

  it('「沒有到期日」的提示連到表格時帶著類型與狀態', async () => {
    // Arrange
    await openCalendar([agyItem({ number: 1, dueDate: null, type: 'task', state: 'doing' })], '/projects/AGY?view=calendar&type=task&state=doing');

    // Act
    const link = await screen.findByRole('link', { name: /個項目沒有到期日/ });

    // Assert
    expect(link.getAttribute('href')).toBe('/projects/AGY?type=task&state=doing&due=none');
  });

  it('工具列沒有「流程」', async () => {
    // Arrange / Act
    await openCalendar([]);

    // Assert
    expect(screen.getByRole('group', { name: '篩選行事曆' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: '流程' })).not.toBeInTheDocument();
  });
});

describe('行事曆的點擊與項目面板', () => {
  it('一般的點擊在旁邊開項目面板，網址記著 item；月曆還在', async () => {
    // Arrange
    const item = agyItem({ number: 1, title: '官網需求', dueDate: '2026-11-18' });
    serveItem(detailOf(item));
    serveActivity(item.key, [created()]);
    const { user, router } = await openCalendar([item]);

    // Act
    await user.click(await screen.findByRole('link', { name: /AGY-1/ }));

    // Assert
    expect(await screen.findByRole('complementary', { name: '項目 AGY-1' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/AGY?view=calendar&item=AGY-1');
    expect(screen.getByRole('table', { name: '2026 年 11 月' })).toBeInTheDocument();
  });
});

describe('窄螢幕的議程', () => {
  it('換成議程：只列這個月有項目（或里程碑）的日子，沒有的不列，也不列頭尾補的日子', async () => {
    // Arrange
    stubCompactScreen();
    const items = [
      agyItem({ number: 1, title: '本月的', dueDate: '2026-11-18' }),
      agyItem({ number: 2, title: '補的日子', dueDate: '2026-12-02' }),
    ];
    const milestones = [milestoneOf('里程碑', '2026-11-25', { id: V1 })];

    // Act
    await openCalendar(items, undefined, { milestones });

    // Assert
    await screen.findByRole('link', { name: /AGY-1/ });
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual(['11 月 18 日（週三）（今天）', '11 月 25 日（週三）']);
    expect(screen.queryByRole('link', { name: /AGY-2/ })).not.toBeInTheDocument();
    // 議程裡項目照舊有編號（不像月曆的格子那麼窄）。
    expect(screen.getByRole('link', { name: /AGY-1/ }).querySelector('.visually-hidden')).toBeNull();
  });

  it('換月份的三顆按鈕縮短成「上月」「今天」「下月」，排得進一行', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    const { region } = await openCalendar([]);

    // Assert
    const nav = within(region).getByRole('navigation', { name: '切換月份' });
    expect(within(nav).getAllByRole('link').map((link) => link.textContent)).toEqual(['‹ 上月', '今天', '下月 ›']);
  });

  it('「沒有到期日」的提示放在議程最下面；沒有東西要說時不留空的容器', async () => {
    // Arrange
    stubCompactScreen();
    const items = [agyItem({ number: 1, dueDate: '2026-11-18' }), agyItem({ number: 2, dueDate: null })];

    // Act
    const { region } = await openCalendar(items);

    // Assert
    const notice = await screen.findByRole('link', { name: '1 個項目沒有到期日' });
    const lastDay = screen.getAllByRole('heading', { level: 3 }).at(-1) as HTMLElement;
    expect(lastDay.compareDocumentPosition(notice) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(region.querySelectorAll('div:empty')).toHaveLength(0);
  });

  it('這個月沒有任何項目時說一句', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    await openCalendar([]);

    // Assert
    expect(await screen.findByText('這個月沒有項目。')).toBeInTheDocument();
  });
});
