import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { created, serveActivity, showAllActivity, updated } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyDetailWithoutFieldFilters, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

// 2026-11-18 在「衝刺 2」（11/16～11/29）裡。
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
});

// 工作區是延後載入的頁面，還要等項目的請求：整套測試同時跑、CPU 忙的時候超過預設的 1 秒，一律多等。
const SLOW_PAGE = { timeout: 3000 };

const SPRINT_1 = '0190a000-0000-7000-8000-0000000000d1';
const SPRINT_2 = '0190a000-0000-7000-8000-0000000000d2';
const V1 = '0190a000-0000-7000-8000-0000000000e1';
const CYCLES = [cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 }), cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_2 })];
const MILESTONES = [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })];

/** 週期與里程碑的假端點：可以先卡住（載入中）、可以先失敗再好起來（重試）。 */
function controlPlanning(projectKey: string) {
  const state = { isFailing: false, release: Promise.resolve() as Promise<unknown>, requests: 0 };
  server.use(
    http.get(`/v1/projects/${projectKey}/cycles`, async () => {
      state.requests += 1;
      await state.release;
      return state.isFailing ? failure(500, 'server.error', '伺服器暫時無法使用。') : ok(CYCLES as never);
    }),
    http.get(`/v1/projects/${projectKey}/milestones`, async () => {
      await state.release;
      return state.isFailing ? failure(500, 'server.error', '伺服器暫時無法使用。') : ok(MILESTONES as never);
    }),
  );
  return {
    state,
    hold() {
      let open!: () => void;
      state.release = new Promise<void>((resolve) => {
        open = resolve;
      });
      return open;
    },
  };
}

describe('週期與里程碑的清單載入完，工作區才畫', () => {
  it('表格：清單載入中時和專案詳情一起算載入中，沒有工具列、沒送項目請求；載入完才出現', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const planning = controlPlanning('AGY');
    const open = planning.hold();
    const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);

    // Act
    renderApp('/projects/AGY');
    await flushPending();

    // Assert
    // 專案頁是延後載入的：CPU 忙的時候 50ms 還沒畫出來，用等的，不用同步的 getBy。
    expect(await screen.findByText('正在載入專案…', undefined, SLOW_PAGE)).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: '篩選項目' })).not.toBeInTheDocument();
    expect(requests).toHaveLength(0);
    open();
    expect(await screen.findByRole('table', { name: '項目' }, SLOW_PAGE)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: '更多條件' }, SLOW_PAGE)).toBeInTheDocument();
  });

  it('看板也一樣', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const planning = controlPlanning('AGY');
    const open = planning.hold();
    const board = serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);

    // Act
    renderApp('/projects/AGY?view=board');
    await flushPending();

    // Assert
    expect(screen.queryByRole('region', { name: '看板' })).not.toBeInTheDocument();
    expect(board.requests).toHaveLength(0);
    open();
    expect(await screen.findByRole('region', { name: '看板' }, SLOW_PAGE)).toBeInTheDocument();
  });

  it('項目的欄位區：清單載入中時寫載入中，不先出現再把下面往下推；載入完週期與里程碑一起出現', async () => {
    // Arrange
    const item = detailOf(smkItem({ number: 7, title: '首頁設計稿', cycleId: SPRINT_2 }), '說明');
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const planning = controlPlanning('SMK');
    const open = planning.hold();
    serveItem(item);
    serveActivity(item.key, [created()]);

    // Act
    renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' }, SLOW_PAGE);
    await flushPending();

    // Assert
    expect(within(fields).getByText('載入中…')).toBeInTheDocument();
    expect(within(fields).queryByRole('button', { name: /^優先度/ })).not.toBeInTheDocument();
    open();
    expect(await within(fields).findByRole('button', { name: '週期：衝刺 2' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '優先度：未填' })).toBeInTheDocument();
    expect(within(fields).queryByText('載入中…')).not.toBeInTheDocument();
  });
});

describe('清單讀取失敗：不說成「沒有」或「已刪除」', () => {
  it('網址帶著週期條件：表格不送項目的請求，顯示讀取失敗與「重試」，重試會重抓清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const planning = controlPlanning('AGY');
    planning.state.isFailing = true;
    const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const { user } = renderApp('/projects/AGY?cycle=current');
    expect(await screen.findByText('週期與里程碑載入失敗', undefined, SLOW_PAGE)).toBeInTheDocument();
    expect(screen.queryByText('現在沒有進行中的週期。')).not.toBeInTheDocument();
    expect(requests).toHaveLength(0);

    // 篩選那一格用中性的說法，不說已刪除：讓人拿得掉條件
    const bar = screen.getByRole('group', { name: '篩選項目' });
    await user.click(within(bar).getByRole('button', { name: '更多條件' }));
    const popover = await screen.findByRole('dialog', { name: '更多條件' }, SLOW_PAGE);
    expect(within(popover).getByRole('combobox', { name: '週期' })).toHaveTextContent('目前的週期');

    // Act
    planning.state.isFailing = false;
    await user.click(screen.getByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('table', { name: '項目' }, SLOW_PAGE)).toBeInTheDocument();
    expect(requests.at(-1)?.searchParams.get('cycle')).toBe(SPRINT_2);
  });

  it('網址帶著里程碑條件也一樣', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    controlPlanning('AGY').state.isFailing = true;
    const requests = serveItems('AGY', []);

    // Act
    renderApp(`/projects/AGY?milestone=${V1}`);

    // Assert
    expect(await screen.findByText('週期與里程碑載入失敗', undefined, SLOW_PAGE)).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });

  it('看板：帶著週期條件時不送請求，顯示讀取失敗與「重試」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const planning = controlPlanning('AGY');
    planning.state.isFailing = true;
    const board = serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const { user } = renderApp(`/projects/AGY?view=board&cycle=${SPRINT_1}`);
    const region = await screen.findByRole('region', { name: '看板' }, SLOW_PAGE);
    expect(await within(region).findByText('週期與里程碑載入失敗')).toBeInTheDocument();
    expect(board.requests).toHaveLength(0);

    // Act
    planning.state.isFailing = false;
    await user.click(within(region).getByRole('button', { name: '重試' }));

    // Assert
    await waitFor(() => {
      expect(board.requests.length).toBeGreaterThan(0);
    });
    expect(within(region).queryByText('週期與里程碑載入失敗')).not.toBeInTheDocument();
  });

  it('沒有帶這兩個條件：項目照常顯示，週期與里程碑的篩選、表格欄當成沒有', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    controlPlanning('AGY').state.isFailing = true;
    serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);

    // Act
    renderApp('/projects/AGY');
    const table = await screen.findByRole('table', { name: '項目' }, SLOW_PAGE);

    // Assert
    expect(within(table).getByRole('row', { name: /官網需求/ })).toBeInTheDocument();
    const bar = screen.getByRole('group', { name: '篩選項目' });
    expect(within(bar).queryByRole('button', { name: '更多條件' })).not.toBeInTheDocument();
    expect(within(bar).queryByRole('combobox', { name: '週期' })).not.toBeInTheDocument();
    expect(screen.queryByText('週期與里程碑載入失敗')).not.toBeInTheDocument();
  });

  it('項目頁：欄位用「某個週期」，活動用「某個週期」「某個里程碑」，不說已刪除、也不一直是「…」', async () => {
    // Arrange
    const item = detailOf(smkItem({ number: 7, title: '首頁設計稿', cycleId: SPRINT_2 }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    controlPlanning('SMK').state.isFailing = true;
    serveItem(item);
    serveActivity(item.key, [created(), updated([{ field: 'cycle', from: SPRINT_2, to: SPRINT_1 }]), updated([{ field: 'milestone', from: V1, to: null }])]);

    // Act
    renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' }, SLOW_PAGE);
    const section = await screen.findByRole('region', { name: '活動與留言' }, SLOW_PAGE);

    // Assert
    expect(await within(fields).findByRole('button', { name: '週期：某個週期' })).toBeInTheDocument();
    const list = await within(section).findByRole('list', { name: '活動與留言' });
    await waitFor(() => {
      expect(list).toHaveTextContent('把「週期」從 某個週期 改成 某個週期');
    });
    expect(list).toHaveTextContent('清除了「里程碑」（原本是 某個里程碑）');
    expect(list).not.toHaveTextContent('已刪除');
  });
});

describe('現在沒有進行中的週期', () => {
  it('表格：不送項目的請求，空白狀態說明', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveCycles('AGY', [cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 })]);
    serveMilestones('AGY', []);
    const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);

    // Act
    renderApp('/projects/AGY?cycle=current');

    // Assert
    expect(await screen.findByText('現在沒有進行中的週期。', undefined, SLOW_PAGE)).toBeInTheDocument();
    expect(screen.getByText('沒有符合條件的項目')).toBeInTheDocument();
    await flushPending();
    expect(requests).toHaveLength(0);
  });

  it('看板：整個看板是空白狀態，說明現在沒有進行中的週期，可以清除條件；不送項目的請求', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveCycles('AGY', [cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 })]);
    serveMilestones('AGY', []);
    const board = serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const { user, router } = renderApp('/projects/AGY?view=board&cycle=current');

    // Act
    const region = await screen.findByRole('region', { name: '看板' }, SLOW_PAGE);
    expect(await within(region).findByText('現在沒有進行中的週期。')).toBeInTheDocument();
    await flushPending();

    // Assert
    expect(board.requests).toHaveLength(0);
    await user.click(within(region).getByRole('button', { name: '清除條件' }));
    expect(new URLSearchParams(router.state.location.search).has('cycle')).toBe(false);
    await waitFor(() => {
      expect(board.requests.length).toBeGreaterThan(0);
    });
  });
});
