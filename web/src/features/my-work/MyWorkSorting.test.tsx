import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { appProject, daysFromToday, exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

// 已過期三筆（優先度、標題都不同），沒有到期日一筆：排序只在每一組裡面排，組照舊。
const urgent = makeItem({ number: 1, title: '寄出報價', priority: 'urgent', dueDate: daysFromToday(-3) });
const low = makeItem({ number: 2, title: '整理素材', priority: 'low', dueDate: daysFromToday(-2) });
const high = makeItem({ projectKey: 'APP', number: 7, title: '登入流程', priority: 'high', dueDate: daysFromToday(-1) });
const undated = makeItem({ number: 5, title: '上線檢查', priority: 'urgent' });
const all = [urgent, low, high, undated];

function rowKeys(tableName: string): (string | null | undefined)[] {
  return within(screen.getByRole('table', { name: tableName }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0]?.textContent);
}

function sortButton(tableName: string, label: string): HTMLElement {
  const header = within(screen.getByRole('table', { name: tableName })).getByRole('columnheader', { name: label });
  return within(header).getByRole('button', { name: label });
}

async function openMyWork(path = '/my-work') {
  signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: all });
  const view = renderApp(path);
  await screen.findByRole('table', { name: '已過期' });
  return view;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('我的工作：點表頭排序（docs/DESIGN.md 8.3「我的工作」）', () => {
  it('沒有排序時照到期日：到期日的表頭標成遞增，其他欄沒有標', async () => {
    // Arrange / Act
    await openMyWork();

    // Assert
    const table = screen.getByRole('table', { name: '已過期' });
    expect(within(table).getByRole('columnheader', { name: '到期日' })).toHaveAttribute('aria-sort', 'ascending');
    expect(within(table).getByRole('columnheader', { name: '優先度' })).not.toHaveAttribute('aria-sort');
    expect(rowKeys('已過期')).toEqual(['WEB-1', 'WEB-2', 'APP-7']);
  });

  it('點優先度：每一組裡由低到緊急，再點反過來，再點回到預設；組照舊，網址記著排序', async () => {
    // Arrange
    const { user, router } = await openMyWork();

    // Act
    await user.click(sortButton('已過期', '優先度'));

    // Assert
    expect(rowKeys('已過期')).toEqual(['WEB-2', 'APP-7', 'WEB-1']);
    expect(rowKeys('沒有到期日')).toEqual(['WEB-5']);
    expect(currentPath(router)).toBe('/my-work?sort=priority%3Aasc');
    // 所有組的表頭標的是同一個排序。
    for (const name of ['已過期', '沒有到期日']) {
      expect(within(screen.getByRole('table', { name })).getByRole('columnheader', { name: '優先度' })).toHaveAttribute('aria-sort', 'ascending');
      expect(within(screen.getByRole('table', { name })).getByRole('columnheader', { name: '到期日' })).not.toHaveAttribute('aria-sort');
    }

    // Act
    await user.click(sortButton('沒有到期日', '優先度'));

    // Assert
    expect(rowKeys('已過期')).toEqual(['WEB-1', 'APP-7', 'WEB-2']);

    // Act
    await user.click(sortButton('已過期', '優先度'));

    // Assert
    expect(rowKeys('已過期')).toEqual(['WEB-1', 'WEB-2', 'APP-7']);
    expect(currentPath(router)).toBe('/my-work');
  });

  it('點到期日（預設已經是遞增）：變成遞減，再點回到預設', async () => {
    // Arrange
    const { user, router } = await openMyWork();

    // Act
    await user.click(sortButton('已過期', '到期日'));

    // Assert
    expect(rowKeys('已過期')).toEqual(['APP-7', 'WEB-2', 'WEB-1']);
    expect(currentPath(router)).toBe('/my-work?sort=due_date%3Adesc');

    // Act
    await user.click(sortButton('已過期', '到期日'));

    // Assert
    expect(currentPath(router)).toBe('/my-work');
  });

  it('網址帶著排序打開就照它排（重新整理、分享）；換條件時排序留著', async () => {
    // Arrange
    const { user, router } = await openMyWork('/my-work?sort=title:asc');

    // Assert：上線檢查、寄出報價、登入流程、整理素材（中文照筆畫）。已過期這一組：寄出報價、登入流程、整理素材
    expect(rowKeys('已過期')).toEqual(['WEB-1', 'APP-7', 'WEB-2']);
    expect(within(screen.getByRole('table', { name: '已過期' })).getByRole('columnheader', { name: '標題' })).toHaveAttribute('aria-sort', 'ascending');

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '優先度' }), '緊急');

    // Assert
    await waitFor(() => {
      expect(new URLSearchParams(router.state.location.search).get('sort')).toBe('title:asc');
    });
  });

  it('窄螢幕沒有表頭：排序是「篩選」裡的下拉，預設是到期日（遞增）', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: all });
    const { user, router } = renderApp('/my-work');
    await user.click(await screen.findByRole('button', { name: '篩選' }));
    const sort = await screen.findByRole('combobox', { name: '排序' });
    expect(sort).toHaveTextContent('到期日（遞增）');

    // Act
    await chooseOption(user, sort, '優先度（遞減）');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?sort=priority%3Adesc');
    });
    const overdue = screen.getByRole('list', { name: '已過期' });
    const titles = within(overdue)
      .getAllByRole('link')
      .map((link) => link.textContent);
    expect(titles.map((text) => text.includes('寄出報價'))).toEqual([true, false, false]);
    expect(titles[1]).toContain('登入流程');
  });
});
