import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { serveStorageUsage, storageUsageOf } from '../../test/storage';

const GIB = 1024 ** 3;

describe('儲存空間頁', () => {
  it('管理員看到總用量與上限、兩個分類各自的大小與檔案數、每人每日的上限', async () => {
    // Arrange
    signedInAs({ ...exampleOwner, role: 'admin' });
    serveStorageUsage();

    // Act
    renderApp('/storage-usage');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '儲存空間' })).toBeInTheDocument();
    expect(await screen.findByText('已用 1.2 GiB／20 GiB')).toBeInTheDocument();
    const bar = screen.getByRole('progressbar', { name: '組織的附件用量' });
    expect(bar).toHaveAttribute('max', String(20 * GIB));
    expect(bar).toHaveAttribute('value', '1288490188');
    const items = screen.getByRole('region', { name: '項目的附件' });
    expect(items).toHaveTextContent('1.0 GiB');
    expect(items).toHaveTextContent('312 個檔案');
    const knowledge = screen.getByRole('region', { name: '知識庫的附件' });
    expect(knowledge).toHaveTextContent('204.8 MiB');
    expect(knowledge).toHaveTextContent('40 個檔案');
    expect(screen.getByRole('region', { name: '每人每日的上傳量' })).toHaveTextContent('2 GiB');
    expect(screen.getByText(/Attachments:Quota:\*/)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByText(/已經超過上限/)).not.toBeInTheDocument();
  });

  it('上限是 null 寫「不限制」，不畫用量條', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveStorageUsage(storageUsageOf({ organizationLimitBytes: null, personDailyLimitBytes: null }));

    // Act
    renderApp('/storage-usage');

    // Assert
    expect(await screen.findByText('已用 1.2 GiB，不限制')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: '每人每日的上傳量' })).getByText('不限制')).toBeInTheDocument();
  });

  it('超過上限時用警告說明超過多少，新的上傳會被擋、讀與刪照常', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveStorageUsage(storageUsageOf({ organizationLimitBytes: GIB, totalBytes: 1.5 * GIB }));

    // Act
    renderApp('/storage-usage');

    // Assert
    const notice = await screen.findByText(/已經超過上限 512\.0 MiB/);
    expect(notice).toHaveTextContent('新的上傳會被擋下');
    expect(notice).toHaveTextContent('下載與刪除照常');
  });

  it('剛好等於上限時也警告：伺服器在等於時就擋新的上傳', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveStorageUsage(storageUsageOf({ organizationLimitBytes: GIB, totalBytes: GIB }));

    // Act
    renderApp('/storage-usage');

    // Assert
    const notice = await screen.findByText(/已經達到上限/);
    expect(notice).toHaveTextContent('新的上傳會被擋下');
    expect(screen.queryByText(/已經超過上限/)).not.toBeInTheDocument();
  });

  it('不認得的分類照原字串顯示', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveStorageUsage(storageUsageOf({ areas: [{ area: 'future_area' as never, bytes: 1024, files: 1 }] }));

    // Act
    renderApp('/storage-usage');

    // Assert
    expect(await screen.findByRole('region', { name: 'future_area' })).toBeInTheDocument();
  });

  it('非管理員打網址是沒有權限的畫面，也不去讀用量', async () => {
    // Arrange
    signedInAs(exampleMember);
    const requests = serveStorageUsage();

    // Act
    renderApp('/storage-usage');

    // Assert
    expect(await screen.findByText('只有組織的擁有者與管理員可以看儲存空間的用量。')).toBeInTheDocument();
    expect(requests()).toBe(0);
  });

  it('後端回 403 時也是沒有權限的畫面', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveStorageUsage(storageUsageOf(), { status: 403, code: 'storage.forbidden', message: '只有組織管理員。' });

    // Act
    renderApp('/storage-usage');

    // Assert
    expect(await screen.findByText('沒有權限')).toBeInTheDocument();
  });

  it('次導覽：管理員在「寄信設定」旁邊看得到，成員看不到', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveStorageUsage();

    // Act
    renderApp('/storage-usage');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '設定' });
    const names = within(nav).getAllByRole('link').map((link) => link.textContent);
    expect(names.indexOf('儲存空間')).toBe(names.indexOf('寄信設定') + 1);
    expect(within(nav).getByRole('link', { name: '儲存空間' })).toHaveAttribute('aria-current', 'page');
  });
});
