import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, agyDetail, agyItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';

const first = agyItem({ number: 1, title: '首頁設計稿' });
const second = agyItem({ number: 2, title: '聯絡表單' });
const third = agyItem({ number: 3, title: '上線檢查' });

function serveProject(items: readonly Item[] = [first, second, third]) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveItems('AGY', items);
  items.forEach((item) => {
    serveItem(detailOf(item));
    serveActivity(item.key, [created()]);
  });
}

/**
 * 從表格打開 AGY-1 的面板，再從「更多動作」打開刪除的對話框。刪除沒有確認框（可以復原），
 * 只有「還有下層」時才有對話框：假的清單不分上下層，每個查詢都回這三個，所以 AGY-1 已經知道有下層。
 */
async function openDeleteDialog() {
  serveProject();
  const view = renderApp('/projects/AGY');
  const table = await screen.findByRole('table', { name: '項目' });
  await view.user.click(within(table).getByRole('link', { name: '首頁設計稿' }));
  const panel = await screen.findByRole('complementary', { name: '項目 AGY-1' });
  await view.user.click(await within(panel).findByRole('button', { name: 'AGY-1 的更多動作' }));
  await view.user.click(screen.getByRole('menuitem', { name: '刪除' }));
  const dialog = await screen.findByRole('dialog', { name: '刪除 AGY-1？' });
  return { ...view, panel, dialog };
}

describe('項目面板與對話框', () => {
  it('對話框開著時 Alt+↓ 不會換掉背後面板的項目（對話框不會變成刪下一個）', async () => {
    // Arrange
    const { user, router, panel } = await openDeleteDialog();

    // Act
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY?item=AGY-1');
    expect(screen.getByRole('dialog', { name: '刪除 AGY-1？' })).toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: '項目 AGY-1' })).toBe(panel);
  });

  it('焦點掉到頁面本體時按 Esc 也不關背後的面板', async () => {
    // Arrange
    const { user, router } = await openDeleteDialog();
    (document.activeElement as HTMLElement | null)?.blur();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY?item=AGY-1');
    expect(screen.getByRole('complementary', { name: '項目 AGY-1' })).toBeInTheDocument();
  });

  it('面板換了項目（例如瀏覽器的上一頁），開著的對話框跟著原本的項目一起關掉', async () => {
    // Arrange
    const { router } = await openDeleteDialog();

    // Act
    await router.navigate('/projects/AGY?item=AGY-2', { replace: true });

    // Assert
    await screen.findByRole('button', { name: 'AGY-2 的更多動作' });
    expect(screen.queryByRole('dialog', { name: '刪除 AGY-1？' })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('對話框裡的 Esc 只關對話框，面板留著', async () => {
    // Arrange
    const { user, router, panel } = await openDeleteDialog();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('complementary', { name: '項目 AGY-1' })).toBe(panel);
    expect(currentPath(router)).toBe('/projects/AGY?item=AGY-1');
  });
});
