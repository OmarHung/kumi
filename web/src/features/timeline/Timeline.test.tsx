import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { currentPath } from '../../test/renderApp';
import { server } from '../../test/server';
import { WEEK_START_STORAGE_KEY } from '../../lib/weekStart';
import { NO_FILTERS, renderTimeline } from '../../test/timeline';

// 今天 2026-11-18（週三）：預設的畫面是 11/09（週一）到 12/20。
const BAR = smkItem({ number: 1, title: '首頁設計稿', state: 'doing', stateCategory: 'in_progress', startDate: '2026-11-18', dueDate: '2026-11-28' });
const POINT = smkItem({ number: 2, title: '內容審稿', dueDate: '2026-11-25' });

async function openTimeline(items = [BAR, POINT], path?: string) {
  signedInAs(exampleOwner);
  const requests = serveItems('SMK', items);
  serveUndated([]);
  const view = renderTimeline({ path });
  await screen.findByRole('region', { name: '時間軸' });
  return { ...view, requests };
}

/** 沒有日期的那個請求（noDue）另外回，其他請求照 serveItems 的。 */
function serveUndated(items: readonly ReturnType<typeof smkItem>[]) {
  server.use(
    http.get('/v1/projects/SMK/items', ({ request }) => {
      const url = new URL(request.url);
      if (url.searchParams.get('noDue') !== 'true') return undefined;
      return ok({ items, nextCursor: null });
    }),
  );
}

describe('時間軸的項目', () => {
  it('讀畫面上那一段（datesFrom、datesTo），每一列的連結名稱有編號、標題與日期', async () => {
    // Arrange & Act
    const { requests } = await openTimeline();

    // Assert
    const list = await screen.findByRole('list', { name: '項目' });
    expect(within(list).getByRole('link', { name: 'SMK-1 首頁設計稿，11/18～11/28' })).toBeInTheDocument();
    expect(within(list).getByRole('link', { name: 'SMK-2 內容審稿，11/25' })).toBeInTheDocument();
    const dated = requests.find((url) => url.searchParams.has('datesFrom'));
    expect(dated?.searchParams.get('datesFrom')).toBe('2026-11-09');
    expect(dated?.searchParams.get('datesTo')).toBe('2026-12-20');
    expect(dated?.searchParams.get('limit')).toBe('200');
  });

  it('列照開始日排，有開始日與到期日的是條，只有一個的是點', async () => {
    // Arrange
    const early = smkItem({ number: 7, title: '提早的', dueDate: '2026-11-10' });
    await openTimeline([BAR, POINT, early]);

    // Act
    const list = await screen.findByRole('list', { name: '項目' });

    // Assert
    expect(within(list).getAllByRole('link').map((link) => link.textContent)).toEqual([
      expect.stringContaining('SMK-7'),
      expect.stringContaining('SMK-1'),
      expect.stringContaining('SMK-2'),
    ]);
    expect(screen.getByTestId('mark-SMK-1')).toHaveAttribute('data-kind', 'bar');
    expect(screen.getByTestId('mark-SMK-2')).toHaveAttribute('data-kind', 'point');
  });

  it('過期的有標示，名稱最後說已過期；超出畫面的標出往左或往右還有', async () => {
    // Arrange
    const overdue = smkItem({ number: 3, title: '過期的', dueDate: '2026-11-12' });
    const long = smkItem({ number: 4, title: '很長的', startDate: '2026-10-20', dueDate: '2027-01-30' });
    await openTimeline([overdue, long]);

    // Assert
    expect(await screen.findByRole('link', { name: 'SMK-3 過期的，11/12，已過期' })).toBeInTheDocument();
    expect(screen.getByTestId('mark-SMK-3')).toHaveAttribute('data-overdue', 'true');
    expect(screen.getByTestId('mark-SMK-4')).toHaveAttribute('data-clipped', 'both');
  });

  it('點標題或條在旁邊開項目面板（網址多 item）', async () => {
    // Arrange
    const { user, router } = await openTimeline();

    // Act
    await user.click(await screen.findByRole('link', { name: /SMK-1 首頁設計稿/ }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toContain('item=SMK-1');
    });
  });

  it('點條也開面板', async () => {
    // Arrange
    const { user, router } = await openTimeline();
    await screen.findByRole('list', { name: '項目' });

    // Act
    await user.click(screen.getByTestId('mark-SMK-2'));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toContain('item=SMK-2');
    });
  });
});

describe('時間軸的範圍與網址', () => {
  it('只有畫面的範圍說給報讀軟體：範圍、數量；表頭的刻度是裝飾', async () => {
    // Arrange & Act
    await openTimeline();

    // Assert
    expect(await screen.findByText('顯示 2026/11/09 到 2026/12/20，共 2 個項目。')).toBeInTheDocument();
  });

  it('「往後」「往前」移動四週並換掉網址（不多一筆瀏覽紀錄），「今天」回到預設', async () => {
    // Arrange
    const { user, router } = await openTimeline();

    // Act
    await user.click(screen.getByRole('link', { name: /往後/ }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK?view=timeline&from=2026-12-07');
    expect(await screen.findByText('顯示 2026/12/07 到 2027/01/17，共 0 個項目。')).toBeInTheDocument();
    expect(router.state.historyAction).toBe('REPLACE');
    await user.click(screen.getByRole('link', { name: /往前/ }));
    await user.click(screen.getByRole('link', { name: /往前/ }));
    expect(currentPath(router)).toBe('/projects/SMK?view=timeline&from=2026-10-12');
    await user.click(screen.getByRole('link', { name: '今天' }));
    expect(currentPath(router)).toBe('/projects/SMK?view=timeline');
  });

  it('刻度「月」記在網址，範圍變成半年，請求跟著換', async () => {
    // Arrange
    const { user, router, requests } = await openTimeline();

    // Act
    await user.click(within(screen.getByRole('navigation', { name: '刻度' })).getByRole('link', { name: '月' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK?view=timeline&scale=month');
    expect(await screen.findByText(/顯示 2026\/11\/09 到 2027\/05\/09/)).toBeInTheDocument();
    await waitFor(() => {
      expect(requests.some((url) => url.searchParams.get('datesTo') === '2027-05-09')).toBe(true);
    });
  });

  it('網址帶 scale 與 from 直接打開，from 對齊到週一', async () => {
    // Arrange & Act
    await openTimeline([], '/projects/SMK?view=timeline&scale=month&from=2026-12-02');

    // Assert
    expect(await screen.findByText(/顯示 2026\/11\/30 到 2027\/05\/30/)).toBeInTheDocument();
  });
});

describe('時間軸一週從哪天開始', () => {
  it('偏好是週日開始：預設的畫面從 11/08（週日）到 12/19，格線與休息日的底色跟著標出來', async () => {
    // Arrange
    localStorage.setItem(WEEK_START_STORAGE_KEY, 'sunday');

    // Act
    const { requests } = await openTimeline();

    // Assert
    const dated = await waitFor(() => {
      const found = requests.find((url) => url.searchParams.has('datesFrom'));
      expect(found).toBeDefined();
      return found;
    });
    expect(dated?.searchParams.get('datesFrom')).toBe('2026-11-08');
    expect(dated?.searchParams.get('datesTo')).toBe('2026-12-19');
    expect((await screen.findByTestId('timeline-track')).querySelector('[data-week-start]')).toHaveAttribute('data-week-start', 'sunday');
  });

  it('網址帶的 from 對齊到那一週的週日', async () => {
    // Arrange
    localStorage.setItem(WEEK_START_STORAGE_KEY, 'sunday');

    // Act
    await openTimeline([], '/projects/SMK?view=timeline&scale=month&from=2026-12-02');

    // Assert
    expect(await screen.findByText(/顯示 2026\/11\/29 到 2027\/05\/29/)).toBeInTheDocument();
  });

  it('預設從週一開始，格線的底色標在週六、週日', async () => {
    // Arrange / Act
    await openTimeline();

    // Assert
    expect((await screen.findByTestId('timeline-track')).querySelector('[data-week-start]')).toHaveAttribute('data-week-start', 'monday');
  });
});

describe('週期、里程碑與今天', () => {
  it('週期有名稱與範圍，里程碑有名稱與日期，今天標在表頭', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveCycles('SMK', [cycleOf('衝刺 2', '2026-11-16', '2026-11-29')]);
    serveMilestones('SMK', [milestoneOf('年終檔期上線', '2026-12-01')]);
    serveItems('SMK', [BAR]);
    serveUndated([]);

    // Act
    renderTimeline();

    // Assert
    const planning = await screen.findByRole('list', { name: '週期與里程碑' });
    expect(within(planning).getByText('週期：衝刺 2，11/16～11/29')).toBeInTheDocument();
    expect(within(planning).getByText('里程碑：年終檔期上線，12/01')).toBeInTheDocument();
    expect(screen.getByTestId('today-line')).toBeInTheDocument();
  });
});

describe('沒有日期的項目、很多項目與失敗', () => {
  it('說 N 個項目沒有開始日也沒有到期日（只有開始日的不算），連到表格', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveItems('SMK', [BAR]);
    serveUndated([smkItem({ number: 8 }), smkItem({ number: 9 }), smkItem({ number: 10, startDate: '2026-11-20' })]);

    // Act
    renderTimeline();

    // Assert
    const link = await screen.findByRole('link', { name: '2 個項目沒有開始日也沒有到期日' });
    expect(link).toHaveAttribute('href', '/projects/SMK?due=none');
  });

  it('一頁一頁讀完，超過 1,000 個只畫前 1,000 個並說明', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const pages = Array.from({ length: 6 }, (_, page) =>
      Array.from({ length: 200 }, (__, index) => smkItem({ number: page * 200 + index + 1, dueDate: '2026-11-20' })),
    );
    const requests = serveItems('SMK', ...pages);
    serveUndated([]);

    // Act
    renderTimeline();

    // Assert
    expect(await screen.findByText('這段時間的項目超過 1,000 個，只顯示前 1,000 個；用篩選縮小範圍。')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /^SMK-/ })).toHaveLength(1000);
    expect(requests.filter((url) => url.searchParams.has('datesFrom'))).toHaveLength(5);
  });

  it('沒有項目時說這段時間沒有，並教怎麼換一段', async () => {
    // Arrange & Act
    await openTimeline([]);

    // Assert
    expect(await screen.findByText('這段時間沒有項目')).toBeInTheDocument();
  });

  it('讀取失敗時說明並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/projects/SMK/items', () => failure(500, 'server.error', '伺服器出了問題。')));

    // Act
    renderTimeline();

    // Assert
    expect(await screen.findByRole('button', { name: '重試' })).toBeInTheDocument();
  });
});

describe('篩選', () => {
  it('篩選條件換成 API 的參數；到期篩「沒有到期日」不送 noDue（後端不收），在畫面上篩', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const requests = serveItems('SMK', [BAR, POINT, smkItem({ number: 3, title: '只有開始', startDate: '2026-11-20' })]);
    serveUndated([]);

    // Act
    renderTimeline({ filters: { ...NO_FILTERS, assignee: 'me', priority: 'high', due: 'none', q: '首頁' } });

    // Assert
    expect(await screen.findByRole('link', { name: /SMK-3/ })).toBeInTheDocument();
    const dated = requests.find((url) => url.searchParams.has('datesFrom'));
    expect(dated?.searchParams.get('assignee')).toBe('me');
    expect(dated?.searchParams.getAll('priority')).toEqual(['high']);
    expect(dated?.searchParams.get('q')).toBe('首頁');
    expect(dated?.searchParams.has('noDue')).toBe(false);
    expect(screen.getByRole('link', { name: /SMK-3/ })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /SMK-1/ })).not.toBeInTheDocument();
  });
});
