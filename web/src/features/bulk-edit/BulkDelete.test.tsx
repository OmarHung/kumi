import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { queryKeys } from '../../api/queryKeys';
import type { Item } from '../../api/types';
import { signedInAs } from '../../test/api';
import { serveBulkDelete, serveBulkRestore } from '../../test/bulk';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { http } from 'msw';
import { failure } from '../../test/api';
import { server } from '../../test/server';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const ITEMS: readonly Item[] = [1, 2, 3].map((number) => smkItem({ number, title: `任務 ${String(number)}` }));

async function openSelected(select: readonly string[] = ['SMK-1', 'SMK-2']) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', ITEMS);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  for (const key of select) {
    await view.user.click(within(table).getByRole('checkbox', { name: `選取 ${key}` }));
  }
  const bar = await screen.findByRole('toolbar', { name: '批次操作' });
  return { ...view, table, bar };
}

describe('批次刪除', () => {
  it('不用確認：直接送出，表格拿掉它們，提示「已刪除 2 個項目。」加「復原」，動作列換回工具列、焦點回到表頭的勾選框', async () => {
    // Arrange
    const { user, table, bar, queryClient } = await openSelected();
    const deletes = serveBulkDelete('SMK', ITEMS);
    queryClient.setQueryData(queryKeys.deletedItems('SMK'), { items: [], nextCursor: null });

    // Act
    await user.click(within(bar).getByRole('button', { name: '刪除' }));

    // Assert
    const notice = (await screen.findByText('已刪除 2 個項目。')).closest('[data-tone]') as HTMLElement;
    expect(notice).toHaveAttribute('data-tone', 'success');
    expect(within(notice).getByRole('button', { name: '復原' })).toBeInTheDocument();
    expect(deletes.map((request) => request.body)).toEqual([{ keys: ['SMK-1', 'SMK-2'] }]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(table).queryByRole('link', { name: '任務 1' })).not.toBeInTheDocument();
    expect(within(table).getByRole('link', { name: '任務 3' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('toolbar', { name: '批次操作' })).not.toBeInTheDocument();
    });
    expect(within(table).getByRole('checkbox', { name: '選取全部已載入的項目' })).toHaveFocus();
    expect(queryClient.getQueryState(queryKeys.deletedItems('SMK'))?.isInvalidated).toBe(true);
  });

  it('按「復原」：送剛刪掉的那些，同一個位置換成「已復原 2 個項目。」，表格重抓之後它們回來了', async () => {
    // Arrange
    const { user, table, bar } = await openSelected();
    serveBulkDelete('SMK', ITEMS);
    await user.click(within(bar).getByRole('button', { name: '刪除' }));
    const undo = await screen.findByRole('button', { name: '復原' });
    const restores = serveBulkRestore('SMK', ITEMS);
    serveItems('SMK', ITEMS);

    // Act
    await user.click(undo);

    // Assert
    expect(await screen.findByText('已復原 2 個項目。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '復原' })).not.toBeInTheDocument();
    expect(restores.map((request) => request.body)).toEqual([{ keys: ['SMK-1', 'SMK-2'] }]);
    expect(await within(table).findByRole('link', { name: '任務 1' })).toBeInTheDocument();
    expect(within(table).getByRole('link', { name: '任務 2' })).toBeInTheDocument();
  });

  it('有沒刪掉的（還有下層）：警告一起列出原因，沒刪掉的留在選取裡；「復原」只送刪掉的', async () => {
    // Arrange
    const { user, table, bar } = await openSelected();
    serveBulkDelete('SMK', ITEMS, {
      failures: { 'SMK-1': { code: 'item.has_children', message: '還有下層項目，不能刪除。' } },
    });

    // Act
    await user.click(within(bar).getByRole('button', { name: '刪除' }));

    // Assert
    const notice = (await screen.findByText(/已刪除 1 個項目；1 個沒刪：/)).closest('[data-tone]') as HTMLElement;
    expect(notice).toHaveAttribute('data-tone', 'warning');
    expect(within(notice).getByRole('listitem')).toHaveTextContent('SMK-1：還有下層項目，不能刪除。');
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 1 個');
    expect(within(table).getByRole('checkbox', { name: '選取 SMK-1' })).toBeChecked();

    // Arrange
    const restores = serveBulkRestore('SMK', ITEMS);
    serveItems('SMK', ITEMS);

    // Act
    await user.click(within(notice).getByRole('button', { name: '復原' }));

    // Assert
    expect(await screen.findByText('已復原 1 個項目。')).toBeInTheDocument();
    expect(restores.map((request) => request.body)).toEqual([{ keys: ['SMK-2'] }]);
  });

  it('全部沒刪掉：錯誤的提示，沒有「復原」', async () => {
    // Arrange
    const { user, bar } = await openSelected();
    serveBulkDelete('SMK', ITEMS, {
      failures: {
        'SMK-1': { code: 'item.has_children', message: '還有下層項目，不能刪除。' },
        'SMK-2': { code: 'item.has_children', message: '還有下層項目，不能刪除。' },
      },
    });

    // Act
    await user.click(within(bar).getByRole('button', { name: '刪除' }));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('2 個都沒刪：');
    expect(within(alert).queryByRole('button', { name: '復原' })).not.toBeInTheDocument();
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 2 個');
  });

  it('復原時上層還在刪除狀態：說沒復原的原因', async () => {
    // Arrange
    const { user, bar } = await openSelected(['SMK-2']);
    serveBulkDelete('SMK', ITEMS);
    await user.click(within(bar).getByRole('button', { name: '刪除' }));
    const undo = await screen.findByRole('button', { name: '復原' });
    serveBulkRestore('SMK', ITEMS, {
      failures: { 'SMK-2': { code: 'item.parent_deleted', message: '上層 SMK-1 也已刪除，先復原它。' } },
    });

    // Act
    await user.click(undo);

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('1 個都沒復原：');
    expect(within(alert).getByRole('listitem')).toHaveTextContent('SMK-2：上層 SMK-1 也已刪除，先復原它。');
  });

  it('復原成功之後按鈕不見了：焦點移到結果的提示', async () => {
    // Arrange
    const { user, bar } = await openSelected();
    serveBulkDelete('SMK', ITEMS);
    await user.click(within(bar).getByRole('button', { name: '刪除' }));
    const undo = await screen.findByRole('button', { name: '復原' });
    serveBulkRestore('SMK', ITEMS);
    serveItems('SMK', ITEMS);

    // Act
    await user.click(undo);

    // Assert
    const restored = (await screen.findByText('已復原 2 個項目。')).closest('[data-tone]');
    await waitFor(() => {
      expect(restored).toHaveFocus();
    });
  });

  it('復原的請求失敗（網路、權限）：「已刪除」與「復原」照舊留著、多一行原因，焦點還在按鈕，再按一次可以重試', async () => {
    // Arrange
    const { user, bar } = await openSelected();
    serveBulkDelete('SMK', ITEMS);
    await user.click(within(bar).getByRole('button', { name: '刪除' }));
    const undo = await screen.findByRole('button', { name: '復原' });
    server.use(http.post('/v1/projects/SMK/items/bulk-restore', () => failure(503, 'server.unavailable', '伺服器暫時無法處理，請稍後再試。')));

    // Act
    await user.click(undo);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');
    expect(screen.getByText(/已刪除 2 個項目。/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '復原' })).toHaveFocus();

    // Arrange
    const restores = serveBulkRestore('SMK', ITEMS);
    serveItems('SMK', ITEMS);

    // Act
    await user.click(screen.getByRole('button', { name: '復原' }));

    // Assert
    expect(await screen.findByText('已復原 2 個項目。')).toBeInTheDocument();
    expect(restores.map((request) => request.body)).toEqual([{ keys: ['SMK-1', 'SMK-2'] }]);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
