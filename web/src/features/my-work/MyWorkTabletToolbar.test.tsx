import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BELOW_WIDE_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

/** 平板（48～64rem）：只有「不到 64rem」成立。 */
function stubTabletScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === BELOW_WIDE_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

const task = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });

describe('平板的我的工作工具列', () => {
  it('版面與範圍照舊是分頁，搜尋框留在列上，條件收進「篩選」（裡面沒有第二個搜尋框、分組與排序）', async () => {
    // Arrange
    stubTabletScreen();
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [task] });
    const { user } = renderApp('/my-work');

    // Act
    expect(await screen.findByRole('navigation', { name: '版面' })).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: '篩選' }));

    // Assert
    expect(screen.getAllByRole('searchbox', { name: '搜尋' })).toHaveLength(1);
    const group = await screen.findByRole('group', { name: '篩選項目' });
    expect(within(group).queryByRole('searchbox')).not.toBeInTheDocument();
    expect(within(group).getByRole('combobox', { name: '狀態' })).toBeInTheDocument();
    expect(within(group).queryByRole('combobox', { name: '排序' })).not.toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: '範圍' })).toBeInTheDocument();
  });

  it('寬螢幕：兩組分頁後面有一條細直線，沒有「篩選」浮層', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [task] });

    // Act
    renderApp('/my-work');
    const scope = await screen.findByRole('navigation', { name: '範圍' });

    // Assert
    expect(scope.nextElementSibling).toHaveAttribute('aria-hidden', 'true');
    expect(screen.queryByRole('button', { name: '篩選' })).not.toBeInTheDocument();
  });
});
