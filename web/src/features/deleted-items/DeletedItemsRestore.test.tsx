import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { queryKeys } from '../../api/queryKeys';
import { signedInAs } from '../../test/api';
import { deletedItem, serveDeletedItems } from '../../test/deletedItems';
import { agyDetail, exampleOwner } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';

async function openDeleted(items = [deletedItem(9), deletedItem(8), deletedItem(7)], options: Parameters<typeof serveDeletedItems>[2] = {}) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  const served = serveDeletedItems('AGY', items, options);
  const view = renderApp('/projects/AGY/deleted');
  await screen.findByRole('heading', { level: 1, name: '已刪除的項目' });
  await screen.findByRole('rowheader', { name: items[0]?.key ?? '' });
  return { ...view, served };
}

function restoreButton(key: string) {
  return screen.getByRole('button', { name: `復原 ${key}` });
}

describe('已刪除的項目頁：復原一筆', () => {
  it('復原成功：那一列拿掉，上方提示「已復原」並附「開啟」連結', async () => {
    // Arrange
    const { user, router } = await openDeleted([deletedItem(8, { title: '官網需求' }), deletedItem(7)]);

    // Act
    await user.click(restoreButton('AGY-8'));

    // Assert
    expect(await screen.findByText('已復原 AGY-8「官網需求」。')).toBeInTheDocument();
    expect(screen.queryByRole('rowheader', { name: 'AGY-8' })).not.toBeInTheDocument();
    expect(screen.getByRole('rowheader', { name: 'AGY-7' })).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: '開啟' }));
    expect(currentPath(router)).toBe('/projects/AGY/items/AGY-8');
  });

  it('送出中那一顆顯示「復原中…」並停用，其他列照常', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { user } = await openDeleted([deletedItem(8), deletedItem(7)], { gate });

    // Act
    await user.click(restoreButton('AGY-8'));

    // Assert
    const pending = await screen.findByRole('button', { name: '復原 AGY-8' });
    expect(pending).toHaveTextContent('復原中…');
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(restoreButton('AGY-7')).not.toHaveAttribute('aria-disabled');
    release();
    await waitFor(() => {
      expect(screen.queryByRole('rowheader', { name: 'AGY-8' })).not.toBeInTheDocument();
    });
  });

  it('送出中再按一次不會重複送出', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { user, served } = await openDeleted([deletedItem(8)], { gate });

    // Act
    await user.click(restoreButton('AGY-8'));
    await user.click(await screen.findByRole('button', { name: '復原 AGY-8' }));
    release();

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('rowheader', { name: 'AGY-8' })).not.toBeInTheDocument();
    });
    expect(served.restored).toEqual(['AGY-8']);
  });

  it('焦點移到下一列的「復原」', async () => {
    // Arrange
    const { user } = await openDeleted();

    // Act
    await user.click(restoreButton('AGY-9'));

    // Assert
    await waitFor(() => {
      expect(restoreButton('AGY-8')).toHaveFocus();
    });
  });

  it('最後一列復原後焦點移到上一列的「復原」', async () => {
    // Arrange
    const { user } = await openDeleted();

    // Act
    await user.click(restoreButton('AGY-7'));

    // Assert
    await waitFor(() => {
      expect(restoreButton('AGY-8')).toHaveFocus();
    });
  });

  it('只剩一列、復原之後焦點移到頁面標題，並顯示空白狀態', async () => {
    // Arrange
    const { user } = await openDeleted([deletedItem(7)]);

    // Act
    await user.click(restoreButton('AGY-7'));

    // Assert
    expect(await screen.findByText('沒有已刪除的項目')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '已刪除的項目' })).toHaveFocus();
  });

  it('復原之後讓已刪除清單、專案的項目清單、我的工作與上層的選項失效（和批次、換頁提示的復原一致）', async () => {
    // Arrange
    const { user, queryClient } = await openDeleted();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    // Act
    await user.click(restoreButton('AGY-8'));
    await screen.findByText(/已復原 AGY-8/);

    // Assert
    const keys = invalidate.mock.calls.map(([filters]) => filters?.queryKey);
    expect(keys).toContainEqual(queryKeys.deletedItems('AGY'));
    expect(keys).toContainEqual(queryKeys.items('AGY'));
    expect(keys).toContainEqual(queryKeys.myWork);
    expect(keys).toContainEqual(queryKeys.parentCandidates('AGY'));
  });
});

describe('已刪除的項目頁：復原被擋下', () => {
  it('上層也刪除了：說明先復原上層，上層在這一頁時是連到那一列的按鈕', async () => {
    // Arrange
    const { user, served } = await openDeleted([deletedItem(8, { parentKey: 'AGY-3' }), deletedItem(3)]);
    served.rejectRestore('AGY-8', {
      status: 409,
      code: 'item.parent_deleted',
      message: '上層 AGY-3 也已刪除，先復原它。',
      details: [{ code: 'item.parent_deleted', path: 'parent', message: '上層 AGY-3 也已刪除，先復原它。' }],
    });

    // Act
    await user.click(restoreButton('AGY-8'));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('AGY-8 的上層 AGY-3 也已刪除：先復原 AGY-3。');
    expect(screen.getByRole('rowheader', { name: 'AGY-8' })).toBeInTheDocument();
    await user.click(within(alert).getByRole('button', { name: 'AGY-3' }));
    expect(restoreButton('AGY-3')).toHaveFocus();
  });

  it('上層不在已載入的清單裡時，只說明、不放按鈕', async () => {
    // Arrange
    const { user, served } = await openDeleted([deletedItem(8, { parentKey: 'AGY-3' })]);
    served.rejectRestore('AGY-8', { status: 409, code: 'item.parent_deleted', message: '上層 AGY-3 也已刪除，先復原它。' });

    // Act
    await user.click(restoreButton('AGY-8'));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('AGY-8 的上層 AGY-3 也已刪除：先復原 AGY-3。');
    expect(within(alert).queryByRole('button', { name: 'AGY-3' })).not.toBeInTheDocument();
  });

  it('藍圖已經沒有這個類型：顯示伺服器的訊息，那一列還在、按鈕恢復可按、焦點留著', async () => {
    // Arrange
    const { user, served } = await openDeleted([deletedItem(8, { type: 'legacy' })]);
    served.rejectRestore('AGY-8', { status: 409, code: 'item.type_removed', message: '藍圖已經沒有「legacy」這個類型，無法復原。' });

    // Act
    await user.click(restoreButton('AGY-8'));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('藍圖已經沒有「legacy」這個類型，無法復原。');
    expect(restoreButton('AGY-8')).not.toHaveAttribute('aria-disabled');
    expect(restoreButton('AGY-8')).toHaveTextContent('復原');
    expect(restoreButton('AGY-8')).toHaveFocus();
  });

  it('失敗之後也讓清單與我的工作失效，提示可以關掉、焦點回到標題', async () => {
    // Arrange
    const { user, served, queryClient } = await openDeleted([deletedItem(8)]);
    served.rejectRestore('AGY-8', { status: 403, code: 'work.forbidden', message: '你沒有權限復原這個項目。' });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    // Act
    await user.click(restoreButton('AGY-8'));
    const alert = await screen.findByRole('alert');
    await waitFor(() => {
      expect(invalidate.mock.calls.map(([filters]) => filters?.queryKey)).toContainEqual(queryKeys.myWork);
    });
    await user.click(within(alert).getByRole('button', { name: '知道了' }));

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '已刪除的項目' })).toHaveFocus();
  });
});
