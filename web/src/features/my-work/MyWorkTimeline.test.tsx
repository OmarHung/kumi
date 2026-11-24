import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveCrossProjectItems } from '../../test/crossProjectItems';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf } from '../../test/items';
import { renderMyWorkDateView } from '../../test/timeline';
import { removeItemFromCache, replaceItemInCache } from '../item-table/itemCache';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { DEFAULT_MY_WORK_FILTERS, type MyWorkFilters } from '../../domain/myWorkView';
import { MyWorkTimeline } from './MyWorkTimeline';

// 今天 2026-11-18：預設畫面 11/09 ～ 12/20。
const TODAY = '2026-11-18';
const ME = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };

afterEach(() => {
  vi.unstubAllGlobals();
});

const AGY_ITEM = agyItem({ number: 1, title: '接案的需求', assignee: ME, state: 'developing', stateCategory: 'in_progress', startDate: '2026-11-12', dueDate: '2026-11-20' });

const CROSS_PROJECT = [
  AGY_ITEM,
  smkItem({ number: 2, title: '冒煙的任務', dueDate: '2026-11-25' }),
  smkItem({ number: 3, title: '沒有日期的任務' }),
  smkItem({ number: 4, title: '完成的任務', state: 'done', stateCategory: 'done', dueDate: '2026-11-26' }),
];

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

const isUndated = (url: URL) => url.searchParams.get('noDue') === 'true';

async function open(filters: Partial<MyWorkFilters> = {}, items = CROSS_PROJECT, path = '/my-work?view=timeline') {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  const requests = serveCrossProjectItems(items, exampleOwner.personId);
  const view = renderMyWorkDateView(<MyWorkTimeline filters={{ ...DEFAULT_MY_WORK_FILTERS, ...filters }} todayDate={TODAY} />, path);
  const region = await screen.findByRole('region', { name: '時間軸' });
  await waitFor(() => {
    expect(screen.queryByText('正在載入時間軸…')).not.toBeInTheDocument();
  });
  return { ...view, region, requests };
}

describe('我的工作的時間軸：讀哪些項目', () => {
  it('預設條件：我負責、還沒結束的，讀畫面這段時間（開始日或到期日落在其中）的項目', async () => {
    // Arrange / Act
    const { requests } = await open();

    // Assert
    const query = requests.find((url) => !isUndated(url))?.searchParams;
    expect(query?.get('assignee')).toBe('me');
    expect(query?.getAll('stateCategory')).toEqual(['backlog', 'not_started', 'in_progress']);
    expect(query?.get('datesFrom')).toBe('2026-11-09');
    expect(query?.get('datesTo')).toBe('2026-12-20');
    expect(query?.get('dueFrom')).toBeNull();
    expect(query?.get('noDue')).toBeNull();
  });

  it('類型、專案、優先度、搜尋的條件一起送出，沒有日期的那一份也帶著', async () => {
    // Arrange / Act
    const { requests } = await open({ type: 'task', project: 'SMK', priority: 'high', q: '冒煙' });

    // Assert
    for (const query of [requests.find((url) => !isUndated(url)), requests.find(isUndated)]) {
      expect(query?.searchParams.getAll('type')).toEqual(['task']);
      expect(query?.searchParams.getAll('project')).toEqual(['SMK']);
      expect(query?.searchParams.getAll('priority')).toEqual(['high']);
      expect(query?.searchParams.get('q')).toBe('冒煙');
    }
  });

  it('一律送自己的負責人；「本週到期」和畫面的日期範圍一起送，不數沒有日期的', async () => {
    // Arrange / Act
    const { requests } = await open({ due: 'week' });

    // Assert
    const query = requests.find((url) => !isUndated(url))?.searchParams;
    expect(query?.get('assignee')).toBe('me');
    expect(query?.get('dueFrom')).toBe('2026-11-18');
    expect(query?.get('datesFrom')).toBe('2026-11-09');
    expect(requests.some(isUndated)).toBe(false);
  });

  it('「沒有到期日」：日期範圍不和 noDue 一起送，在畫面上把有到期日的拿掉', async () => {
    // Arrange
    const items = [smkItem({ number: 5, title: '只有開始日', startDate: '2026-11-20', assignee: ME }), smkItem({ number: 6, title: '有到期日', dueDate: '2026-11-22' })];

    // Act
    const { requests } = await open({ due: 'none' }, items);

    // Assert
    const dated = requests.find((url) => !isUndated(url))?.searchParams;
    expect(dated?.get('datesFrom')).toBe('2026-11-09');
    expect(dated?.get('noDue')).toBeNull();
    expect(await screen.findByTestId('mark-SMK-5')).toBeInTheDocument();
    expect(screen.queryByTestId('mark-SMK-6')).not.toBeInTheDocument();
  });

  it('條件一定是空的：不送請求，說這段時間沒有項目', async () => {
    // Arrange / Act
    const { region, requests } = await open({ status: { kind: 'category', category: 'done' }, due: 'overdue' });

    // Assert
    expect(await within(region).findByText('這段時間沒有項目')).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });
});

describe('我的工作的時間軸：畫出來的項目', () => {
  it('各專案的項目都畫出來，連到自己專案的項目頁；預設條件下完成的不出現', async () => {
    // Arrange / Act
    const { region } = await open();

    // Assert
    expect(await within(region).findByTestId('mark-AGY-1')).toBeInTheDocument();
    expect(within(region).getByTestId('mark-SMK-2')).toBeInTheDocument();
    expect(within(region).queryByTestId('mark-SMK-4')).not.toBeInTheDocument();
    expect(within(region).getByRole('link', { name: /AGY-1/ })).toHaveAttribute('href', '/projects/AGY/items/AGY-1');
    expect(within(region).getByRole('link', { name: /SMK-2/ })).toHaveAttribute('href', '/projects/SMK/items/SMK-2');
  });

  it('窄螢幕的清單：每個項目的狀態名稱用自己專案的藍圖', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    await open({}, [AGY_ITEM, smkItem({ number: 2, title: '冒煙的任務', dueDate: '2026-11-25' })]);

    // Assert
    const list = await screen.findByRole('list', { name: '項目' });
    const [agy, smk] = await within(list).findAllByRole('listitem');
    expect(agy).toHaveTextContent('開發');
    expect(smk).toHaveTextContent('待辦');
  });
});

describe('我的工作的時間軸：沒有日期的項目', () => {
  it('下面說有幾個，連到「我的工作」的表格，條件照舊再加「沒有到期日」', async () => {
    // Arrange / Act
    await open({ type: 'task' });

    // Assert
    const link = await screen.findByRole('link', { name: '1 個項目沒有開始日也沒有到期日' });
    expect(link).toHaveAttribute('href', '/my-work?type=task&due=none');
  });

  it('都有日期時不說', async () => {
    // Arrange / Act
    const { region } = await open({}, [smkItem({ number: 2, title: '有日期', dueDate: '2026-11-25' })]);

    // Assert
    await within(region).findByTestId('mark-SMK-2');
    expect(screen.queryByText(/沒有開始日也沒有到期日/)).not.toBeInTheDocument();
  });
});

describe('我的工作的時間軸：在項目面板裡改了項目之後', () => {
  it('改了到期日：重抓，條上的日期換成新的', async () => {
    // Arrange
    const items = [...CROSS_PROJECT];
    const { region, queryClient, requests } = await open({}, items);
    await within(region).findByTestId('mark-SMK-2');
    const before = requests.filter((url) => !isUndated(url)).length;
    const moved = smkItem({ number: 2, title: '冒煙的任務', dueDate: '2026-11-27' });
    items[1] = moved;

    // Act
    act(() => {
      replaceItemInCache(queryClient, detailOf(moved));
    });

    // Assert
    await waitFor(() => {
      expect(requests.filter((url) => !isUndated(url)).length).toBeGreaterThan(before);
    });
  });

  it('刪除：項目從時間軸上不見，不重抓', async () => {
    // Arrange
    const { region, queryClient, requests } = await open();
    await within(region).findByTestId('mark-SMK-2');
    const before = requests.length;

    // Act
    act(() => {
      removeItemFromCache(queryClient, 'SMK', 'SMK-2');
    });

    // Assert
    await waitFor(() => {
      expect(within(region).queryByTestId('mark-SMK-2')).not.toBeInTheDocument();
    });
    expect(within(region).getByTestId('mark-AGY-1')).toBeInTheDocument();
    expect(requests).toHaveLength(before);
  });
});
