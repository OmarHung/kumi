import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatInstant } from '../../domain/time';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { memberRef } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { deletedItem, serveDeletedItems } from '../../test/deletedItems';
import { agyDetail, exampleOwner } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

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

async function openDeleted(items = [deletedItem(7), deletedItem(8)], detail = agyDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  const served = serveDeletedItems('AGY', items);
  const view = renderApp('/projects/AGY/deleted');
  await screen.findByRole('heading', { level: 1, name: '已刪除的項目' });
  return { ...view, served };
}

describe('已刪除的項目頁：內容', () => {
  it('每一筆列出編號、標題、類型名稱、刪除的人與刪除時間', async () => {
    // Arrange & Act
    await openDeleted([deletedItem(7, { title: '官網需求', deletedBy: memberRef })]);

    // Assert
    const table = await screen.findByRole('table', { name: '已刪除的項目' });
    const row = within(table).getByRole('row', { name: /AGY-7/ });
    expect(within(row).getByRole('rowheader', { name: 'AGY-7' })).toBeInTheDocument();
    expect(within(row).getByText('官網需求')).toBeInTheDocument();
    expect(within(row).getByText('需求')).toBeInTheDocument();
    expect(within(row).getByText(memberRef.displayName)).toBeInTheDocument();
    expect(within(row).getByTitle(formatInstant('2026-11-16T09:00:00Z'))).toHaveAttribute('datetime', '2026-11-16T09:00:00Z');
  });

  it('藍圖已經沒有的類型顯示 key 加「（已移除的類型）」', async () => {
    // Arrange & Act
    await openDeleted([deletedItem(7, { type: 'legacy' })]);

    // Assert
    expect(await screen.findByText('legacy（已移除的類型）')).toBeInTheDocument();
  });

  it('舊資料沒有記錄刪除的人時寫「未記錄」', async () => {
    // Arrange & Act
    await openDeleted([deletedItem(7, { deletedBy: null })]);

    // Assert
    expect(await screen.findByText('未記錄')).toBeInTheDocument();
  });

  it('頁列有麵包屑「AGY」連回專案；清單頁不放說明（docs/DESIGN.md 6.2）', async () => {
    // Arrange & Act
    const { user, router } = await openDeleted();
    await screen.findByRole('table', { name: '已刪除的項目' });

    // Assert
    expect(screen.queryByText(/不會出現在清單/)).not.toBeInTheDocument();
    await user.click(within(screen.getByRole('navigation', { name: '位置' })).getByRole('link', { name: 'AGY' }));
    expect(currentPath(router)).toMatch(/^\/projects\/AGY/);
    expect(currentPath(router)).not.toContain('deleted');
  });

  it('沒有已刪除的項目時顯示空白狀態', async () => {
    // Arrange & Act
    await openDeleted([]);

    // Assert：要說的話放在空白狀態裡（清單頁不放說明）。
    expect(await screen.findByText('沒有已刪除的項目')).toBeInTheDocument();
    expect(screen.getByText('刪除的項目不會出現在清單、看板與我的工作，會先放在這裡，需要時可以復原。')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('讀取失敗時顯示錯誤與「重試」，重試後出現清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    let isFailing = true;
    const served = serveDeletedItems('AGY', [deletedItem(7)]);
    server.use(
      http.get('/v1/projects/AGY/items/deleted', () =>
        isFailing ? failure(500, 'server.error', '伺服器暫時無法處理。') : undefined,
      ),
    );
    const { user } = renderApp('/projects/AGY/deleted');

    // Act
    const retry = await screen.findByRole('button', { name: '重試' });
    isFailing = false;
    await user.click(retry);

    // Assert
    expect(await screen.findByRole('rowheader', { name: 'AGY-7' })).toBeInTheDocument();
    expect(served.requests.length).toBeGreaterThan(0);
  });

  it('沒有 item.read 的人看到沒有權限，不送出請求', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail({ myPermissions: [] })] });
    const served = serveDeletedItems('AGY', [deletedItem(7)]);

    // Act
    renderApp('/projects/AGY/deleted');

    // Assert
    expect(await screen.findByText('沒有權限看這個專案的已刪除項目')).toBeInTheDocument();
    expect(served.requests).toHaveLength(0);
  });
});

describe('已刪除的項目頁：載入更多', () => {
  it('還有下一頁時按「載入更多」接在後面，載完就不再有按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const served = serveDeletedItems('AGY', [deletedItem(9), deletedItem(8), deletedItem(7)], { pageSize: 2 });
    const { user } = renderApp('/projects/AGY/deleted');
    await screen.findByRole('rowheader', { name: 'AGY-9' });
    expect(screen.queryByRole('rowheader', { name: 'AGY-7' })).not.toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByRole('rowheader', { name: 'AGY-7' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    expect(served.requests.at(-1)?.searchParams.get('cursor')).toBe('offset-2');
  });
});

describe('已刪除的項目頁：沒有 item.delete', () => {
  it('看得到清單，但沒有「復原」', async () => {
    // Arrange & Act
    await openDeleted([deletedItem(7)], agyDetail({ myPermissions: ['item.read'] }));

    // Assert
    expect(await screen.findByRole('rowheader', { name: 'AGY-7' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /復原/ })).not.toBeInTheDocument();
  });
});

describe('已刪除的項目頁：窄螢幕', () => {
  it('換成清單，一筆有編號、標題、類型、刪除的人與「復原」', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    await openDeleted([deletedItem(7, { title: '官網需求' })]);

    // Assert
    const list = await screen.findByRole('list', { name: '已刪除的項目' });
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    const row = within(list).getByRole('listitem');
    expect(within(row).getByText('AGY-7')).toBeInTheDocument();
    expect(within(row).getByText('官網需求')).toBeInTheDocument();
    expect(within(row).getByText('需求')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '復原 AGY-7' })).toBeInTheDocument();
  });
});
