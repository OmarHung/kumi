import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveCrossProjectItems } from '../../test/crossProjectItems';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf } from '../../test/items';
import { renderMyWorkDateView } from '../../test/timeline';
import { removeItemFromCache, replaceItemInCache } from '../item-table/itemCache';
import { DEFAULT_MY_WORK_FILTERS, type MyWorkFilters } from '../../domain/myWorkView';
import { MyWorkCalendar } from './MyWorkCalendar';

// 今天 2026-11-18：11 月的月曆從 10/26（週一）排到 12/06（週日）。
const TODAY = '2026-11-18';

afterEach(() => {
  vi.unstubAllGlobals();
});

const ME = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };

const CROSS_PROJECT = [
  agyItem({ number: 1, title: '接案的需求', assignee: ME, state: 'developing', stateCategory: 'in_progress', dueDate: '2026-11-20' }),
  smkItem({ number: 2, title: '冒煙的任務', dueDate: '2026-11-25' }),
  smkItem({ number: 3, title: '沒有日期的任務', dueDate: null }),
  smkItem({ number: 4, title: '完成的任務', state: 'done', stateCategory: 'done', dueDate: '2026-11-26' }),
];

function itemRequests(requests: readonly URL[]) {
  return requests.filter((url) => url.searchParams.get('noDue') !== 'true');
}

async function open(filters: Partial<MyWorkFilters> = {}, items = CROSS_PROJECT, path = '/my-work?view=calendar') {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  const requests = serveCrossProjectItems(items, exampleOwner.personId);
  const view = renderMyWorkDateView(<MyWorkCalendar filters={{ ...DEFAULT_MY_WORK_FILTERS, ...filters }} todayDate={TODAY} />, path);
  const region = await screen.findByRole('region', { name: '行事曆' });
  await waitFor(() => {
    expect(screen.queryByText('正在載入行事曆…')).not.toBeInTheDocument();
  });
  return { ...view, region, requests };
}

describe('我的工作的行事曆：讀哪些項目', () => {
  it('預設條件：我負責、還沒結束的，讀畫面上那幾週的到期日，依到期日排', async () => {
    // Arrange / Act
    const { requests } = await open();

    // Assert
    const query = itemRequests(requests)[0]?.searchParams;
    expect(query?.get('assignee')).toBe('me');
    expect(query?.getAll('stateCategory')).toEqual(['backlog', 'not_started', 'in_progress']);
    expect(query?.get('dueFrom')).toBe('2026-10-26');
    expect(query?.get('dueTo')).toBe('2026-12-06');
    expect(query?.get('sort')).toBe('due_date:asc');
    expect(query?.get('project')).toBeNull();
  });

  it('類型、專案、優先度、搜尋的條件一起送出', async () => {
    // Arrange / Act
    const { requests } = await open({ type: 'task', project: 'SMK', priority: 'high', q: '冒煙' });

    // Assert
    const query = itemRequests(requests)[0]?.searchParams;
    expect(query?.getAll('type')).toEqual(['task']);
    expect(query?.getAll('project')).toEqual(['SMK']);
    expect(query?.getAll('priority')).toEqual(['high']);
    expect(query?.get('q')).toBe('冒煙');
  });

  it('網址上的 assignee 不讀、一律送自己；到期條件和畫面的範圍取交集', async () => {
    // Arrange / Act
    const { requests } = await open({ due: 'week' });

    // Assert
    const query = itemRequests(requests)[0]?.searchParams;
    expect(query?.get('assignee')).toBe('me');
    expect(query?.get('dueFrom')).toBe('2026-11-18');
    expect(query?.get('dueTo')).toBe('2026-11-22');
  });

  it('網址上的月份：照那個月的範圍讀', async () => {
    // Arrange / Act
    const { requests } = await open({}, CROSS_PROJECT, '/my-work?view=calendar&month=2027-02');

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: '2027 年 2 月' })).toBeInTheDocument();
    await waitFor(() => {
      expect(itemRequests(requests).at(-1)?.searchParams.get('dueFrom')).toBe('2027-02-01');
    });
  });

  it('條件一定是空的（完成的項目加「已過期」）：不送請求，月曆是空的', async () => {
    // Arrange / Act
    const { region, requests } = await open({ status: { kind: 'category', category: 'done' }, due: 'overdue' });

    // Assert
    expect(within(region).getByRole('table', { name: '2026 年 11 月' })).toBeInTheDocument();
    expect(requests).toHaveLength(0);
    expect(within(region).queryByRole('link', { name: /-\d+/ })).not.toBeInTheDocument();
  });
});

describe('我的工作的行事曆：畫出來的項目', () => {
  it('各專案的項目都排在自己到期日那一天，連到自己專案的項目頁', async () => {
    // Arrange / Act
    const { region } = await open();

    // Assert
    const agy = await within(region).findByRole('link', { name: /AGY-1/ });
    expect(agy).toHaveAttribute('href', '/projects/AGY/items/AGY-1');
    expect(agy.closest('td')).toContainElement(within(region).getByRole('heading', { name: '11 月 20 日（週五）' }));
    const smk = within(region).getByRole('link', { name: /SMK-2/ });
    expect(smk).toHaveAttribute('href', '/projects/SMK/items/SMK-2');
    expect(smk.closest('td')).toContainElement(within(region).getByRole('heading', { name: '11 月 25 日（週三）' }));
  });

  it('只畫伺服器給的：預設條件下完成的項目不出現', async () => {
    // Arrange / Act
    const { region } = await open();

    // Assert
    await within(region).findByRole('link', { name: /SMK-2/ });
    expect(within(region).queryByRole('link', { name: /SMK-4/ })).not.toBeInTheDocument();
  });

  it('過期的項目用今天（由上層給的）判斷', async () => {
    // Arrange / Act
    const { region } = await open({}, [smkItem({ number: 5, title: '過期的', dueDate: '2026-11-10' })]);

    // Assert
    expect(await within(region).findByRole('link', { name: /SMK-5/ })).toHaveTextContent('已過期');
  });

  it('沒有週期與里程碑的標示（跨專案沒有單一專案可以讀）', async () => {
    // Arrange / Act
    const { region } = await open();

    // Assert
    await within(region).findByRole('link', { name: /AGY-1/ });
    expect(within(region).queryByText(/ (開始|結束)$/)).not.toBeInTheDocument();
    expect(within(region).queryByText(/◆/)).not.toBeInTheDocument();
  });
});

describe('我的工作的行事曆：沒有到期日的項目', () => {
  it('上方說有幾個，連到「我的工作」的表格，條件照舊再加「沒有到期日」', async () => {
    // Arrange / Act
    await open({ type: 'task', project: 'SMK' });

    // Assert
    const link = await screen.findByRole('link', { name: '1 個項目沒有到期日' });
    expect(link).toHaveAttribute('href', '/my-work?type=task&due=none&project=SMK');
  });

  it('都有到期日時不說；篩了「本週到期」時也不說', async () => {
    // Arrange / Act
    const { region, requests } = await open({ due: 'week' }, [smkItem({ number: 2, title: '本週的', dueDate: '2026-11-18' })]);

    // Assert
    await within(region).findByRole('link', { name: /SMK-2/ });
    expect(screen.queryByText(/沒有到期日/)).not.toBeInTheDocument();
    expect(requests.some((url) => url.searchParams.get('noDue') === 'true')).toBe(false);
  });
});

describe('我的工作的行事曆：在項目面板裡改了項目之後', () => {
  it('改了到期日：重抓，項目換到新的那一天', async () => {
    // Arrange
    const items = [...CROSS_PROJECT];
    const { region, queryClient, requests } = await open({}, items);
    await within(region).findByRole('link', { name: /SMK-2/ });
    const before = itemRequests(requests).length;
    const moved = smkItem({ number: 2, title: '冒煙的任務', dueDate: '2026-11-27' });
    items[1] = moved;

    // Act
    act(() => {
      replaceItemInCache(queryClient, detailOf(moved));
    });

    // Assert
    await waitFor(() => {
      expect(itemRequests(requests).length).toBeGreaterThan(before);
    });
    const link = await within(region).findByRole('link', { name: /SMK-2/ });
    await waitFor(() => {
      expect(link.closest('td')).toContainElement(within(region).getByRole('heading', { name: '11 月 27 日（週五）' }));
    });
  });

  it('只改了標題（沒動到條件）：不重抓，畫面上的標題換掉', async () => {
    // Arrange
    const { region, queryClient, requests } = await open();
    await within(region).findByRole('link', { name: /SMK-2/ });
    const before = itemRequests(requests).length;

    // Act
    act(() => {
      replaceItemInCache(queryClient, detailOf(smkItem({ number: 2, title: '冒煙的任務', dueDate: '2026-11-25', priority: 'high' })));
    });

    // Assert
    expect(itemRequests(requests)).toHaveLength(before);
  });

  it('刪除：項目從行事曆上不見，不重抓', async () => {
    // Arrange
    const { region, queryClient, requests } = await open();
    await within(region).findByRole('link', { name: /SMK-2/ });
    const before = itemRequests(requests).length;

    // Act
    act(() => {
      removeItemFromCache(queryClient, 'SMK', 'SMK-2');
    });

    // Assert
    await waitFor(() => {
      expect(within(region).queryByRole('link', { name: /SMK-2/ })).not.toBeInTheDocument();
    });
    expect(within(region).getByRole('link', { name: /AGY-1/ })).toBeInTheDocument();
    expect(itemRequests(requests)).toHaveLength(before);
  });
});

describe('我的工作的行事曆：依期間排', () => {
  const SPANS = [
    smkItem({ number: 2, title: '冒煙的任務', startDate: '2026-11-23', dueDate: '2026-11-25' }),
    smkItem({ number: 3, title: '只有開始日', startDate: '2026-11-24', dueDate: null }),
    smkItem({ number: 5, title: '沒有日期的任務', dueDate: null }),
  ];

  it('有「排法」的切換（專案頁同一份元件）；切到「期間」記在這個瀏覽器，改讀 datesFrom、datesTo 與同樣的條件', async () => {
    // Arrange
    const { user, region, requests } = await open({}, SPANS);
    await within(region).findByRole('link', { name: /SMK-2/ });
    expect(requests.some((url) => url.searchParams.has('datesFrom'))).toBe(false);

    // Act
    await user.click(within(within(region).getByRole('group', { name: '排法' })).getByRole('button', { name: '期間' }));

    // Assert
    expect(await within(region).findByRole('link', { name: 'SMK-2 冒煙的任務，11/23～11/25' })).toBeInTheDocument();
    expect(localStorage.getItem('calendar.dateMode')).toBe('span');
    const query = requests.filter((url) => url.searchParams.has('datesFrom')).at(-1)?.searchParams;
    expect(query?.get('datesFrom')).toBe('2026-10-26');
    expect(query?.get('datesTo')).toBe('2026-12-06');
    expect(query?.get('assignee')).toBe('me');
    expect(query?.get('dueFrom')).toBeNull();
  });

  it('只有開始日的是開始那一天；提示說「沒有開始日也沒有到期日」，連到「我的工作」的表格（due=none）', async () => {
    // Arrange
    localStorage.setItem('calendar.dateMode', 'span');

    // Act
    const { region } = await open({ type: 'task' }, SPANS);

    // Assert
    expect(await within(region).findByRole('link', { name: /SMK-3/ })).toBeInTheDocument();
    const link = await screen.findByRole('link', { name: '1 個項目沒有開始日也沒有到期日' });
    expect(link).toHaveAttribute('href', '/my-work?type=task&due=none');
  });

  it('「沒有到期日」的條件：讀回來把有到期日的拿掉', async () => {
    // Arrange
    localStorage.setItem('calendar.dateMode', 'span');

    // Act
    const { region } = await open({ due: 'none' }, SPANS);

    // Assert
    expect(await within(region).findByRole('link', { name: /SMK-3/ })).toBeInTheDocument();
    expect(within(region).queryByRole('link', { name: /SMK-2/ })).not.toBeInTheDocument();
  });

  it('條件互相矛盾（完成加已過期）時不讀', async () => {
    // Arrange
    localStorage.setItem('calendar.dateMode', 'span');

    // Act
    const { requests } = await open({ status: { kind: 'category', category: 'done' }, due: 'overdue' }, SPANS);

    // Assert
    expect(requests).toHaveLength(0);
  });

  it('只有開始日的多過一頁（200 筆）：往下多讀一頁，才數得到沒有日期的', async () => {
    // Arrange
    localStorage.setItem('calendar.dateMode', 'span');
    const startOnly = Array.from({ length: 201 }, (_unused, index) => smkItem({ number: index + 10, title: `開始${String(index)}`, startDate: '2026-11-24', dueDate: null }));

    // Act
    await open({}, [...startOnly, smkItem({ number: 300, title: '沒有日期', dueDate: null })]);

    // Assert
    expect(await screen.findByRole('link', { name: '1 個項目沒有開始日也沒有到期日' })).toBeInTheDocument();
  });
});
