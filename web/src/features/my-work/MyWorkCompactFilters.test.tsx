import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

const task = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });

describe('窄螢幕的我的工作工具列', () => {
  it('版面是下拉、條件收進「篩選」：浮層裡是標籤看得到的所有條件，表格時最後多「分組」與「排序」', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [task] });
    const { user } = renderApp('/my-work');
    expect(await screen.findByRole('combobox', { name: '版面' })).toHaveTextContent('表格');
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();

    // Act
    await user.click(await screen.findByRole('button', { name: '篩選' }));

    // Assert
    const group = await screen.findByRole('group', { name: '篩選項目' });
    const fields = within(group).getAllByRole('combobox');
    expect(within(group).getByRole('searchbox', { name: '搜尋' })).toBeInTheDocument();
    for (const name of ['狀態', '到期', '優先度', '專案', '分組', '排序']) {
      expect(within(group).getByRole('combobox', { name })).toBeInTheDocument();
    }
    expect(fields.at(-2)).toHaveAccessibleName('分組');
    expect(fields.at(-1)).toHaveAccessibleName('排序');
  });

  it('換版面的下拉導到同樣條件的網址；條件的數字在「篩選」按鈕上；清除條件之後焦點回到「篩選」', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [task] });
    const { user, router } = renderApp('/my-work?priority=high&category=done');
    await chooseOption(user, await screen.findByRole('combobox', { name: '版面' }), '看板');
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?view=board&category=done&priority=high');
    });
    const filter = await screen.findByRole('button', { name: '篩選' });
    expect(filter).toHaveAccessibleDescription('目前有 2 個條件');

    // Act
    await user.click(filter);
    await user.click(await within(await screen.findByRole('group', { name: '篩選項目' })).findByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?view=board');
    });
    expect(screen.getByRole('button', { name: '篩選' })).toHaveFocus();
  });

  it('只有表格有「分組」', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [task] });
    const { user } = renderApp('/my-work?view=calendar');

    // Act
    await user.click(await screen.findByRole('button', { name: '篩選' }));

    // Assert
    const group = await screen.findByRole('group', { name: '篩選項目' });
    expect(within(group).queryByRole('combobox', { name: '分組' })).not.toBeInTheDocument();
  });
});
