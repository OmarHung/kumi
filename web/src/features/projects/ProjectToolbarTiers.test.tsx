import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BELOW_WIDE_QUERY, COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

function stubScreen(matching: readonly string[]) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: matching.includes(query),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openTable() {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp('/projects/AGY');
  await screen.findByRole('heading', { level: 1 });
  return view;
}

describe('專案頁工具列的三種排法', () => {
  it('平板：版面照舊是分頁，搜尋框留在列上，「篩選」浮層裡沒有第二個搜尋框', async () => {
    // Arrange
    stubScreen([BELOW_WIDE_QUERY]);
    const { user } = await openTable();

    // Act
    await user.click(await screen.findByRole('button', { name: '篩選' }));

    // Assert
    expect(screen.getByRole('navigation', { name: '版面' })).toBeInTheDocument();
    expect(screen.getAllByRole('searchbox', { name: '搜尋' })).toHaveLength(1);
    const group = await screen.findByRole('group', { name: '篩選項目' });
    expect(within(group).queryByRole('searchbox')).not.toBeInTheDocument();
    expect(within(group).getByRole('combobox', { name: '狀態' })).toBeInTheDocument();
  });

  it('手機：版面是下拉，搜尋跟著條件收進「篩選」浮層，列上沒有搜尋框', async () => {
    // Arrange
    stubScreen([COMPACT_QUERY, BELOW_WIDE_QUERY]);
    const { user } = await openTable();
    expect(await screen.findByRole('combobox', { name: '版面' })).toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();

    // Act
    await user.click(await screen.findByRole('button', { name: '篩選' }));

    // Assert
    const group = await screen.findByRole('group', { name: '篩選項目' });
    expect(within(group).getByRole('searchbox', { name: '搜尋' })).toBeInTheDocument();
  });

  it('寬螢幕：版面分頁後面有一條細直線，條件整排在列上，沒有「篩選」浮層', async () => {
    // Arrange / Act
    await openTable();
    const views = screen.getByRole('navigation', { name: '版面' });

    // Assert
    expect(views.nextElementSibling).toHaveAttribute('aria-hidden', 'true');
    expect(screen.queryByRole('button', { name: '篩選' })).not.toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toBeInTheDocument();
  });

  it.each([
    ['手機', [COMPACT_QUERY, BELOW_WIDE_QUERY]],
    ['平板', [BELOW_WIDE_QUERY]],
  ])('%s：右下角有「新增項目」，打開同一個對話框', async (_name, queries) => {
    // Arrange
    stubScreen(queries);
    const { user } = await openTable();

    // Act
    await user.click(await screen.findByRole('button', { name: '新增項目' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: '新增項目' })).toBeInTheDocument();
  });

  it('寬螢幕：沒有浮動的「新增項目」按鈕', async () => {
    // Arrange / Act
    await openTable();

    // Assert
    expect(screen.queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
  });
});
