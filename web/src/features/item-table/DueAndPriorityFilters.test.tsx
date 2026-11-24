import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setWeekStart, WEEK_START_STORAGE_KEY } from '../../lib/weekStart';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';

// 2026-11-18 是週三：這一週到 2026-11-22（週日）。
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function lastParams(requests: readonly URL[]): URLSearchParams {
  return requests.at(-1)?.searchParams ?? new URLSearchParams();
}

function urlParams(router: ReturnType<typeof renderApp>['router']): URLSearchParams {
  return new URLSearchParams(router.state.location.search);
}

async function openAgy(path = '/projects/AGY') {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  return { ...view, requests, filters: screen.getByRole('group', { name: '篩選項目' }) };
}

describe('表格的到期與優先度篩選', () => {
  it('到期的選項：所有到期日、已過期、今天到期、本週到期、沒有到期日', async () => {
    // Arrange
    const { user, filters } = await openAgy();

    // Act
    const options = await openOptions(user, within(filters).getByRole('combobox', { name: '到期' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['所有到期日', '已過期', '今天到期', '本週到期', '沒有到期日']);
  });

  it('優先度的選項：所有優先度、緊急、高、中、低、未設定（和項目上顯示的一樣）', async () => {
    // Arrange
    const { user, filters } = await openAgy();

    // Act
    const options = await openOptions(user, within(filters).getByRole('combobox', { name: '優先度' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['所有優先度', '緊急', '高', '中', '低', '未設定']);
  });

  it('已過期：昨天以前，而且只算還沒完成的狀態類別', async () => {
    // Arrange
    const { user, requests, filters, router } = await openAgy();

    // Act
    await chooseOption(user, within(filters).getByRole('combobox', { name: '到期' }), '已過期');

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('dueTo')).toBe('2026-11-17');
    });
    const sent = lastParams(requests);
    expect(sent.getAll('stateCategory')).toEqual(['backlog', 'not_started', 'in_progress']);
    expect(sent.get('dueFrom')).toBeNull();
    expect(urlParams(router).get('due')).toBe('overdue');
  });

  it('今天到期、本週到期、沒有到期日各自換成對的日期參數', async () => {
    // Arrange
    const { user, requests, filters } = await openAgy();
    const due = within(filters).getByRole('combobox', { name: '到期' });

    // Act
    await chooseOption(user, due, '今天到期');
    await waitFor(() => {
      expect(lastParams(requests).get('dueFrom')).toBe('2026-11-18');
    });
    const today = lastParams(requests);
    await chooseOption(user, due, '本週到期');
    await waitFor(() => {
      expect(lastParams(requests).get('dueTo')).toBe('2026-11-22');
    });
    const week = lastParams(requests);
    await chooseOption(user, due, '沒有到期日');
    await waitFor(() => {
      expect(lastParams(requests).get('noDue')).toBe('true');
    });

    // Assert
    expect(today.get('dueTo')).toBe('2026-11-18');
    expect(week.get('dueFrom')).toBe('2026-11-18');
    expect(lastParams(requests).get('dueFrom')).toBeNull();
  });

  it('偏好是週日開始：本週到期是今天到這週的週六（11/21），網址仍是 due=week', async () => {
    // Arrange
    localStorage.setItem(WEEK_START_STORAGE_KEY, 'sunday');
    const { requests } = await openAgy('/projects/AGY?due=week');

    // Act
    await waitFor(() => {
      expect(lastParams(requests).get('dueTo')).toBe('2026-11-21');
    });

    // Assert
    expect(lastParams(requests).get('dueFrom')).toBe('2026-11-18');
  });

  it('畫面開著時改成週日開始，本週到期用新的範圍重新查（到 11/21），不留舊的結果', async () => {
    // Arrange：預設週一開始，本週到期查到週日 11/22。
    const { requests } = await openAgy('/projects/AGY?due=week');
    await waitFor(() => {
      expect(lastParams(requests).get('dueTo')).toBe('2026-11-22');
    });

    // Act
    act(() => {
      setWeekStart('sunday');
    });

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('dueTo')).toBe('2026-11-21');
    });
    expect(lastParams(requests).get('dueFrom')).toBe('2026-11-18');
  });

  it('優先度帶出 priority 參數，也寫進網址；選回「所有優先度」就拿掉', async () => {
    // Arrange
    const { user, requests, filters, router } = await openAgy();
    const priority = within(filters).getByRole('combobox', { name: '優先度' });

    // Act
    await chooseOption(user, priority, '緊急');
    await waitFor(() => {
      expect(lastParams(requests).getAll('priority')).toEqual(['urgent']);
    });
    const written = urlParams(router).toString();
    await chooseOption(user, priority, '所有優先度');

    // Assert
    expect(written).toBe('priority=urgent');
    expect(urlParams(router).toString()).toBe('');
    expect(priority).toHaveTextContent('所有優先度');
  });

  it('網址帶著條件打開時直接用那些條件，不認得的值略過', async () => {
    // Arrange / Act
    const { requests, filters } = await openAgy('/projects/AGY?due=week&priority=high');

    // Assert
    expect(lastParams(requests).get('dueTo')).toBe('2026-11-22');
    expect(lastParams(requests).getAll('priority')).toEqual(['high']);
    expect(within(filters).getByRole('combobox', { name: '到期' })).toHaveTextContent('本週到期');
    expect(within(filters).getByRole('combobox', { name: '優先度' })).toHaveTextContent('高');
  });

  it('清除條件把到期與優先度也清掉', async () => {
    // Arrange
    const { user, requests, filters, router } = await openAgy('/projects/AGY?due=today&priority=low');

    // Act
    await user.click(within(filters).getByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).has('dueFrom')).toBe(false);
    });
    expect(lastParams(requests).has('priority')).toBe(false);
    expect(urlParams(router).toString()).toBe('');
  });
});

describe('看板的到期與優先度篩選', () => {
  it('每一欄都帶著條件，切換到表格也帶著', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const board = serveItemsByQuery('SMK', [smkItem({ number: 1, title: '首頁設計稿', priority: 'high', dueDate: '2026-11-20' })]);
    const { user, router } = renderApp('/projects/SMK?view=board');
    await screen.findByRole('region', { name: '看板' });
    const toolbar = screen.getByRole('group', { name: '篩選看板' });

    // Act
    await chooseOption(user, within(toolbar).getByRole('combobox', { name: '到期' }), '本週到期');
    await chooseOption(user, within(toolbar).getByRole('combobox', { name: '優先度' }), '高');

    // Assert
    await waitFor(() => {
      expect(lastParams(board.requestsFor('todo')).getAll('priority')).toEqual(['high']);
    });
    const column = lastParams(board.requestsFor('todo'));
    expect(column.get('dueFrom')).toBe('2026-11-18');
    expect(column.get('dueTo')).toBe('2026-11-22');
    expect(urlParams(router).toString()).toBe(new URLSearchParams('view=board&due=week&priority=high').toString());
    await user.click(within(screen.getByRole('navigation', { name: '版面' })).getByRole('link', { name: '表格' }));
    await screen.findByRole('table', { name: '項目' });
    expect(urlParams(router).toString()).toBe(new URLSearchParams('due=week&priority=high').toString());
  });
});

describe('窄螢幕的篩選', () => {
  it('到期與優先度在「篩選」裡，按鈕的說明算進條件的數量', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveItems('SMK', [smkItem({ number: 1 })]);
    const { user } = renderApp('/projects/SMK?due=overdue');
    await screen.findByRole('list', { name: '項目' });

    // Act
    await user.click(screen.getByRole('button', { name: '篩選' }));
    const panel = screen.getByRole('dialog', { name: '篩選' });
    await chooseOption(user, within(panel).getByRole('combobox', { name: '優先度' }), '低');

    // Assert
    expect(within(panel).getByRole('combobox', { name: '到期' })).toHaveTextContent('已過期');
    await waitFor(() => {
      expect(lastParams(requests).getAll('priority')).toEqual(['low']);
    });
    expect(screen.getByRole('button', { name: '篩選' })).toHaveAccessibleDescription('目前有 2 個條件');
  });
});

describe('「今天」跟著日期走', () => {
  it('分頁開過夜，回到分頁後「今天到期」用新的一天重新查', async () => {
    // Arrange
    const { user, requests, filters } = await openAgy();
    await chooseOption(user, within(filters).getByRole('combobox', { name: '到期' }), '今天到期');
    await waitFor(() => {
      expect(lastParams(requests).get('dueFrom')).toBe('2026-11-18');
    });

    // Act
    vi.setSystemTime(new Date(2026, 10, 19, 9));
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('dueFrom')).toBe('2026-11-19');
    });
  });
});
