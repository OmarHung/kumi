import { act, fireEvent, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { serveActivity, created } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const ONE = smkItem({ number: 1, title: '首頁設計稿' });

async function openTable() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [ONE]);
  serveItem(detailOf(ONE, '說明'));
  serveActivity(ONE.key, [created()]);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  return { ...view, table };
}

describe('表格的標題格：是打開項目的連結（編號是純文字），不就地編輯', () => {
  it('標題是連到項目頁的連結，表格裡沒有標題的編輯按鈕', async () => {
    // Arrange / Act
    const { table } = await openTable();

    // Assert
    expect(within(table).getByRole('link', { name: '首頁設計稿' })).toHaveAttribute('href', '/projects/SMK/items/SMK-1');
    expect(within(table).queryByRole('button', { name: /^標題：/ })).not.toBeInTheDocument();
  });

  it('點標題：在旁邊開項目面板，標題連結標 aria-current', async () => {
    // Arrange
    const { user, table } = await openTable();

    // Act
    await user.click(within(table).getByRole('link', { name: '首頁設計稿' }));

    // Assert
    expect(await screen.findByRole('complementary', { name: '項目 SMK-1' })).toBeInTheDocument();
    expect(within(table).getByRole('link', { name: '首頁設計稿' })).toHaveAttribute('aria-current', 'true');
    expect(within(table).queryByRole('textbox', { name: '標題' })).not.toBeInTheDocument();
  });

  it('標題連結取得焦點後按 Enter 打開項目', async () => {
    // Arrange
    const { user, table } = await openTable();
    act(() => {
      within(table).getByRole('link', { name: '首頁設計稿' }).focus();
    });

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('complementary', { name: '項目 SMK-1' })).toBeInTheDocument();
  });

  it('⌘ 點擊照瀏覽器原本的行為（不攔下、不開面板）', async () => {
    // Arrange
    const { table } = await openTable();
    const link = within(table).getByRole('link', { name: '首頁設計稿' });

    // Act
    const isNotPrevented = fireEvent.click(link, { metaKey: true });

    // Assert
    expect(isNotPrevented).toBe(true);
    expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
  });
});
