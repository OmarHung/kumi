import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveDatedItems } from '../../test/calendar';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveCycles, serveMilestones } from '../../test/planning';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { currentPath, renderApp } from '../../test/renderApp';
import { savedViewOf, serveSavedViews } from '../../test/savedViews';
import { chooseOption, openOptions } from '../../test/select';
import { readProjectView } from '../projects/projectViewPreference';

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

async function openAgy(path: string, views = [savedViewOf('預設')]) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveCycles('AGY', []);
  serveMilestones('AGY', []);
  serveDatedItems('AGY', [agyItem({ number: 1, dueDate: '2026-11-18' })]);
  const backend = serveSavedViews('AGY', views);
  const app = renderApp(path);
  await screen.findByRole('navigation', { name: '版面' });
  return { ...app, backend };
}

function tabs(): HTMLElement {
  return screen.getByRole('navigation', { name: '版面' });
}

describe('版面的四個分頁', () => {
  it('依序是表格、看板、行事曆、時間軸，目前的版面 aria-current="page"', async () => {
    // Arrange / Act
    await openAgy('/projects/AGY?view=calendar');

    // Assert
    expect(within(tabs()).getAllByRole('link').map((link) => link.textContent)).toEqual(['表格', '看板', '行事曆', '時間軸']);
    expect(within(tabs()).getByRole('link', { name: '行事曆' })).toHaveAttribute('aria-current', 'page');
  });

  it('從表格切到行事曆：帶過類型、搜尋與負責人，排序不帶；切回表格也帶著', async () => {
    // Arrange
    const { user, router } = await openAgy('/projects/AGY?q=官網&assignee=me&type=requirement&sort=title:desc');

    // Act
    await user.click(within(tabs()).getByRole('link', { name: '行事曆' }));

    // Assert
    await screen.findByRole('region', { name: '行事曆' });
    expect(currentPath(router)).toBe(`/projects/AGY?${new URLSearchParams('view=calendar&type=requirement&q=官網&assignee=me').toString()}`);
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveValue('官網');
    await user.click(within(tabs()).getByRole('link', { name: '表格' }));
    await screen.findByRole('table', { name: '項目' });
    expect(currentPath(router)).toBe(`/projects/AGY?${new URLSearchParams('type=requirement&q=官網&assignee=me').toString()}`);
  });

  it('行事曆切到看板：月份、類型與狀態不帶（看板依狀態分欄），其他條件帶著', async () => {
    // Arrange
    const { user, router } = await openAgy('/projects/AGY?view=calendar&month=2026-12&type=task&state=doing&priority=high');
    await screen.findByRole('region', { name: '行事曆' });

    // Act
    await user.click(within(tabs()).getByRole('link', { name: '看板' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY?view=board&priority=high');
  });
});

describe('記住上次的版面', () => {
  it('打開行事曆就記下，下次連到這個專案照它開', async () => {
    // Arrange / Act
    await openAgy('/projects/AGY?view=calendar');
    await screen.findByRole('region', { name: '行事曆' });

    // Assert
    await waitFor(() => {
      expect(readProjectView('AGY')).toBe('calendar');
    });
  });

  it('在專案頁切換到行事曆也記下；切回表格就忘了', async () => {
    // Arrange
    const { user } = await openAgy('/projects/AGY');

    // Act
    await user.click(within(tabs()).getByRole('link', { name: '行事曆' }));
    await screen.findByRole('region', { name: '行事曆' });

    // Assert
    expect(readProjectView('AGY')).toBe('calendar');
    await user.click(within(tabs()).getByRole('link', { name: '表格' }));
    expect(readProjectView('AGY')).toBe('table');
  });
});

describe('儲存的檢視的版面', () => {
  it('行事曆的檢視：套用時切到行事曆，只帶行事曆有的條件', async () => {
    // Arrange
    const views = [savedViewOf('我的檔期', { assignee: 'me', priority: 'urgent' }, { layout: 'calendar' })];
    const { user, router } = await openAgy('/projects/AGY', views);

    // Act
    await user.click(await screen.findByRole('button', { name: /^檢視/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: '我的檔期' }));

    // Assert
    await screen.findByRole('region', { name: '行事曆' });
    expect(currentPath(router)).toBe(`/projects/AGY?${new URLSearchParams('view=calendar&assignee=me&priority=urgent').toString()}`);
  });

  it('行事曆上另存：存的是 calendar 版面，名稱預設有「行事曆」，月份不存', async () => {
    // Arrange
    const { user, backend } = await openAgy('/projects/AGY?view=calendar&month=2026-12&assignee=me', []);
    await screen.findByRole('region', { name: '行事曆' });

    // Act
    await user.click(await screen.findByRole('button', { name: /^檢視/ }));
    await user.click(await screen.findByRole('menuitem', { name: '另存成檢視…' }));
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('行事曆・我');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(backend.created).toHaveLength(1);
    });
    expect(backend.created[0]?.body).toMatchObject({ layout: 'calendar', query: { assignee: 'me', sort: null } });
  });
});

describe('手機（48rem 以下）的版面是下拉', () => {
  function stubCompactScreen() {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
  }

  async function openCompact(path: string) {
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveCycles('AGY', []);
    serveMilestones('AGY', []);
    serveDatedItems('AGY', [agyItem({ number: 1, dueDate: '2026-11-18' })]);
    serveSavedViews('AGY', [savedViewOf('預設')]);
    const app = renderApp(path);
    const select = await screen.findByRole('combobox', { name: '版面' });
    return { ...app, select };
  }

  it('不畫分頁，畫一個名稱是「版面」的下拉：標籤視覺上隱藏，目前的版面是它的值', async () => {
    // Arrange / Act
    const { select } = await openCompact('/projects/AGY?view=calendar');

    // Assert
    expect(screen.queryByRole('navigation', { name: '版面' })).not.toBeInTheDocument();
    expect(select).toHaveTextContent('行事曆');
    expect(screen.getByText('版面', { selector: 'label' })).toHaveClass('visually-hidden');
  });

  it('選項依序是表格、看板、行事曆、時間軸', async () => {
    // Arrange
    const { user, select } = await openCompact('/projects/AGY');

    // Act
    const options = await openOptions(user, select);

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['表格', '看板', '行事曆', '時間軸']);
  });

  it('選了就換版面：帶著兩邊都有的條件，並記住上次的版面', async () => {
    // Arrange
    const { user, router, select } = await openCompact('/projects/AGY?q=官網&assignee=me&type=requirement&sort=title:desc');

    // Act
    await chooseOption(user, select, '行事曆');

    // Assert
    await screen.findByRole('region', { name: '行事曆' });
    expect(currentPath(router)).toBe(`/projects/AGY?${new URLSearchParams('view=calendar&type=requirement&q=官網&assignee=me').toString()}`);
    expect(readProjectView('AGY')).toBe('calendar');
    await chooseOption(user, screen.getByRole('combobox', { name: '版面' }), '表格');
    await screen.findByRole('list', { name: '項目' });
    expect(readProjectView('AGY')).toBe('table');
  });

  it('寬螢幕仍然是分頁，沒有下拉', async () => {
    // Arrange / Act
    vi.unstubAllGlobals();
    await openAgy('/projects/AGY');

    // Assert
    expect(screen.queryByRole('combobox', { name: '版面' })).not.toBeInTheDocument();
    expect(within(tabs()).getAllByRole('link')).toHaveLength(4);
  });
});
