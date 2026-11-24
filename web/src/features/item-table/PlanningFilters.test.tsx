import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetailWithoutFieldFilters, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { stubResizeObserver } from '../../test/resizeObserver';

// 2026-11-18 在「衝刺 2」（11/16～11/29）裡。
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const SPRINT_1 = '0190a000-0000-7000-8000-0000000000d1';
const SPRINT_2 = '0190a000-0000-7000-8000-0000000000d2';
const V1 = '0190a000-0000-7000-8000-0000000000e1';

const CYCLES = [
  cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 }),
  cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_2 }),
];
const MILESTONES = [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })];

interface Options {
  readonly cycles?: typeof CYCLES;
  readonly milestones?: typeof MILESTONES;
}

async function openAgy(path = '/projects/AGY', { cycles = CYCLES, milestones = MILESTONES }: Options = {}) {
  signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
  serveCycles('AGY', cycles);
  serveMilestones('AGY', milestones);
  const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  return { ...view, requests, filters: screen.getByRole('group', { name: '篩選項目' }) };
}

/** 週期與里程碑一律在「更多條件」的浮層裡（寬螢幕）：打開它，回傳浮層。 */
async function openMore(user: ReturnType<typeof renderApp>['user'], filters: HTMLElement): Promise<HTMLElement> {
  await user.click(await within(filters).findByRole('button', { name: '更多條件' }));
  return screen.findByRole('dialog', { name: '更多條件' });
}

function lastParams(requests: readonly URL[]): URLSearchParams {
  return requests.at(-1)?.searchParams ?? new URLSearchParams();
}

function urlParams(router: ReturnType<typeof renderApp>['router']): URLSearchParams {
  return new URLSearchParams(router.state.location.search);
}

describe('表格的週期與里程碑篩選', () => {
  it('週期的選項：所有週期、目前的週期、沒有週期，再來是每一個週期', async () => {
    // Arrange
    const { user, filters } = await openAgy();

    // Act
    const more = await openMore(user, filters);
    const options = await openOptions(user, within(more).getByRole('combobox', { name: '週期' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['所有週期', '目前的週期', '沒有週期', '衝刺 1', '衝刺 2']);
  });

  it('里程碑的選項：所有里程碑、沒有里程碑，再來是每一個', async () => {
    // Arrange
    const { user, filters } = await openAgy();

    // Act
    const more = await openMore(user, filters);
    const options = await openOptions(user, within(more).getByRole('combobox', { name: '里程碑' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['所有里程碑', '沒有里程碑', 'v1.0 上線']);
  });

  it('專案沒有週期、沒有里程碑時，篩選裡不出現那一個', async () => {
    // Arrange / Act
    const { filters } = await openAgy('/projects/AGY', { cycles: [], milestones: [] });

    // Assert：沒有週期、里程碑，也沒有隱藏的條件，就沒有「更多條件」
    expect(within(filters).queryByRole('combobox', { name: '週期' })).not.toBeInTheDocument();
    expect(within(filters).queryByRole('combobox', { name: '里程碑' })).not.toBeInTheDocument();
    expect(within(filters).queryByRole('button', { name: '更多條件' })).not.toBeInTheDocument();
  });

  it('只有週期沒有里程碑：「更多條件」裡只出現週期', async () => {
    // Arrange
    const { user, filters } = await openAgy('/projects/AGY', { milestones: [] });

    // Act
    const more = await openMore(user, filters);

    // Assert
    expect(within(more).getByRole('combobox', { name: '週期' })).toBeInTheDocument();
    expect(within(more).queryByRole('combobox', { name: '里程碑' })).not.toBeInTheDocument();
  });

  it('寬螢幕上週期與里程碑不排在列上：列夠寬時到期與優先度照樣在列上，週期與里程碑只在「更多條件」裡', async () => {
    // Arrange / Act
    const { user, filters } = await openAgy();

    // Assert
    expect(within(filters).getByRole('combobox', { name: '到期' })).toBeInTheDocument();
    expect(within(filters).getByRole('combobox', { name: '優先度' })).toBeInTheDocument();
    expect(within(filters).queryByRole('combobox', { name: '週期' })).not.toBeInTheDocument();
    expect(within(filters).queryByRole('combobox', { name: '里程碑' })).not.toBeInTheDocument();
    const more = await openMore(user, filters);
    expect(within(more).queryByRole('combobox', { name: '到期' })).not.toBeInTheDocument();
    expect(within(more).getByRole('combobox', { name: '週期' })).toBeInTheDocument();
    expect(within(more).getByRole('combobox', { name: '里程碑' })).toBeInTheDocument();
  });

  it('選「目前的週期」：網址記 current，送給 API 的是今天所在的週期編號', async () => {
    // Arrange
    const { user, requests, filters, router } = await openAgy();

    // Act
    const more = await openMore(user, filters);
    await chooseOption(user, within(more).getByRole('combobox', { name: '週期' }), '目前的週期');

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('cycle')).toBe(SPRINT_2);
    });
    expect(urlParams(router).get('cycle')).toBe('current');
  });

  it('沒有進行中的週期時：結果是空的，並說明現在沒有進行中的週期', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', [cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: SPRINT_1 })]);
    serveMilestones('AGY', []);
    const requests = serveItems('AGY', []);

    // Act
    renderApp('/projects/AGY?cycle=current');

    // Assert：不送項目的請求（不送全 0 的假編號），空白狀態說明
    expect(await screen.findByText('現在沒有進行中的週期。')).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });

  it('選某個週期、沒有週期、某個里程碑、沒有里程碑，各自換成對的參數', async () => {
    // Arrange
    const { user, requests, filters } = await openAgy();
    const more = await openMore(user, filters);
    const cycle = within(more).getByRole('combobox', { name: '週期' });
    const milestone = within(more).getByRole('combobox', { name: '里程碑' });

    // Act
    await chooseOption(user, cycle, '衝刺 1');
    await waitFor(() => {
      expect(lastParams(requests).get('cycle')).toBe(SPRINT_1);
    });
    await chooseOption(user, cycle, '沒有週期');
    await waitFor(() => {
      expect(lastParams(requests).get('cycle')).toBe('none');
    });
    await chooseOption(user, milestone, 'v1.0 上線');
    await waitFor(() => {
      expect(lastParams(requests).get('milestone')).toBe(V1);
    });
    await chooseOption(user, milestone, '沒有里程碑');

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('milestone')).toBe('none');
    });
  });

  it('網址上的 cycle 與 milestone 一開始就套用；不是編號的略過', async () => {
    // Arrange / Act
    const { user, requests, filters } = await openAgy(`/projects/AGY?cycle=${SPRINT_1}&milestone=${V1}`);

    // Assert
    expect(lastParams(requests).get('cycle')).toBe(SPRINT_1);
    expect(lastParams(requests).get('milestone')).toBe(V1);
    const more = await openMore(user, filters);
    expect(within(more).getByRole('combobox', { name: '週期' })).toHaveTextContent('衝刺 1');
    expect(within(more).getByRole('combobox', { name: '里程碑' })).toHaveTextContent('v1.0 上線');
  });

  it('網址上手打的 cycle 不是編號時略過，不會被後端擋下', async () => {
    // Arrange / Act
    const { requests } = await openAgy('/projects/AGY?cycle=sprint-2');

    // Assert
    expect(lastParams(requests).get('cycle')).toBeNull();
  });

  it('週期已經刪除、網址還指著它：下拉說「已刪除的週期」，而且選得回「所有週期」', async () => {
    // Arrange
    const { user, filters } = await openAgy(`/projects/AGY?cycle=${SPRINT_1}`, { cycles: CYCLES.slice(1) });
    const cycle = within(await openMore(user, filters)).getByRole('combobox', { name: '週期' });

    // Act / Assert
    expect(cycle).toHaveTextContent('已刪除的週期');
    await chooseOption(user, cycle, '所有週期');
    expect(cycle).toHaveTextContent('所有週期');
  });

  it('有週期的條件時出現「清除條件」，清除後兩個條件都拿掉', async () => {
    // Arrange
    const { user, filters, router } = await openAgy(`/projects/AGY?cycle=none&milestone=${V1}`);

    // Act
    await user.click(within(filters).getByRole('button', { name: '清除條件' }));

    // Assert
    expect(urlParams(router).toString()).toBe('');
  });

  it('沒有符合的項目時，空白狀態是「沒有符合條件的項目」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', CYCLES);
    serveMilestones('AGY', []);
    serveItems('AGY', []);

    // Act
    renderApp('/projects/AGY?cycle=none');

    // Assert
    expect(await screen.findByText('沒有符合條件的項目')).toBeInTheDocument();
  });
});

describe('週期與里程碑是次要條件', () => {
  const BAR = '[role=group][aria-label="篩選項目"]';

  it('放不下時和到期、優先度一起收進「更多條件」，按鈕上的數字把它們算進去', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', CYCLES);
    serveMilestones('AGY', MILESTONES);
    serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const resize = stubResizeObserver();
    const { user } = renderApp(`/projects/AGY?due=week&cycle=none`);
    await screen.findByRole('table', { name: '項目' });
    const bar = () => screen.getByRole('group', { name: '篩選項目' });
    await within(bar()).findByRole('button', { name: '更多條件' });

    // Act
    resize.report(BAR, 500);

    // Assert
    expect(within(bar()).queryByRole('combobox', { name: '到期' })).not.toBeInTheDocument();
    expect(within(bar()).queryByRole('combobox', { name: '週期' })).not.toBeInTheDocument();
    const button = within(bar()).getByRole('button', { name: '更多條件' });
    expect(button).toHaveAccessibleDescription('目前有 2 個條件');
    await user.click(button);
    const popover = await screen.findByRole('dialog', { name: '更多條件' });
    expect(within(popover).getByRole('combobox', { name: '週期' })).toHaveTextContent('沒有週期');
    expect(within(popover).getByRole('combobox', { name: '里程碑' })).toBeInTheDocument();
  });

  it('列上欄位數不含週期與里程碑：同樣的寬度，到期與優先度照樣排在列上，「更多條件」裡只有週期與里程碑', async () => {
    // Arrange：AGY 列上六個欄位加預留的「清除條件」與「更多條件」要 1008px，1100px 排得下；
    // 如果把週期與里程碑也算進去（八個欄位）要 1280px，到期與優先度就被擠進「更多條件」了。
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', CYCLES);
    serveMilestones('AGY', MILESTONES);
    serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const resize = stubResizeObserver();
    renderApp('/projects/AGY');
    await screen.findByRole('table', { name: '項目' });
    const bar = () => screen.getByRole('group', { name: '篩選項目' });
    await within(bar()).findByRole('button', { name: '更多條件' });

    // Act
    resize.report(BAR, 1100);

    // Assert
    expect(within(bar()).getByRole('combobox', { name: '到期' })).toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '優先度' })).toBeInTheDocument();
    expect(within(bar()).getByRole('button', { name: '更多條件' })).toBeInTheDocument();
  });

  it('專案有週期與里程碑時「更多條件」一直在列上，要算它的寬度：放不下全部時從優先度開始收，不是整組收', async () => {
    // Arrange：AGY 六個欄位加「清除條件」與「更多條件」要 1008px；950px 放不下，收掉優先度（872px）就放得下。
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', CYCLES);
    serveMilestones('AGY', MILESTONES);
    serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const resize = stubResizeObserver();
    renderApp('/projects/AGY?priority=high');
    await screen.findByRole('table', { name: '項目' });
    const bar = () => screen.getByRole('group', { name: '篩選項目' });
    await within(bar()).findByRole('button', { name: '更多條件' });

    // Act
    resize.report(BAR, 950);

    // Assert
    expect(within(bar()).queryByRole('combobox', { name: '優先度' })).not.toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '到期' })).toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '負責人' })).toBeInTheDocument();
    expect(within(bar()).getByRole('button', { name: '更多條件' })).toHaveAccessibleDescription('目前有 1 個條件');
  });

  it('窄螢幕的「篩選」浮層裡有週期與里程碑，算在條件的數字裡', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === COMPACT_QUERY, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', CYCLES);
    serveMilestones('AGY', MILESTONES);
    serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const { user } = renderApp(`/projects/AGY?cycle=current&milestone=none`);
    const button = await screen.findByRole('button', { name: '篩選' });
    expect(button).toHaveAccessibleDescription('目前有 2 個條件');

    // Act
    await user.click(button);

    // Assert
    const popover = await screen.findByRole('dialog', { name: '篩選' });
    expect(within(popover).getByRole('combobox', { name: '週期' })).toHaveTextContent('目前的週期');
    expect(within(popover).getByRole('combobox', { name: '里程碑' })).toHaveTextContent('沒有里程碑');
  });
});

describe('看板的週期與里程碑篩選', () => {
  it('每一欄都帶這個條件，目前的週期換成編號', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', CYCLES);
    serveMilestones('AGY', MILESTONES);
    const board = serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);

    // Act
    renderApp(`/projects/AGY?view=board&cycle=current&milestone=${V1}`);
    await screen.findByRole('region', { name: '看板' });

    // Assert
    await waitFor(() => {
      expect(board.requests.length).toBeGreaterThan(0);
    });
    board.requests.forEach((url) => {
      expect(url.searchParams.get('cycle')).toBe(SPRINT_2);
      expect(url.searchParams.get('milestone')).toBe(V1);
    });
  });

  it('看板的工具列也有週期（在「更多條件」裡），選了寫進網址', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveCycles('AGY', CYCLES);
    serveMilestones('AGY', []);
    serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const { user, router } = renderApp('/projects/AGY?view=board');
    await screen.findByRole('region', { name: '看板' });
    const bar = screen.getByRole('group', { name: '篩選看板' });

    // Act
    await user.click(await within(bar).findByRole('button', { name: '更多條件' }));
    const more = await screen.findByRole('dialog', { name: '更多條件' });
    await chooseOption(user, within(more).getByRole('combobox', { name: '週期' }), '沒有週期');

    // Assert
    expect(urlParams(router).get('cycle')).toBe('none');
    expect(urlParams(router).get('view')).toBe('board');
  });
});
