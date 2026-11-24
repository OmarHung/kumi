import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { makeChangeSet, serveChangeSets } from '../../test/changesets';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

describe('專案的設定入口', () => {
  it('有 blueprint.read 時專案頁有「設定」，點了換到設定頁並記在網址上', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);
    serveChangeSets('SMK', []);
    const { user, router } = renderApp('/projects/SMK');

    // Act
    // 側欄也有一個「設定」（設定中心）：專案的入口在頁面本體的頁列。
    await user.click(await within(await screen.findByRole('main')).findByRole('link', { name: '設定' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '設定' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    expect(screen.getByRole('link', { name: '冒煙測試' })).toHaveAttribute('href', '/projects/SMK');
    expect(screen.getByRole('list', { name: '「任務流程」的狀態' })).toBeInTheDocument();
    await user.click(within(screen.getByRole('navigation', { name: '設定的區塊' })).getByRole('link', { name: '欄位' }));
    expect(screen.getByRole('list', { name: '自訂欄位' })).toBeInTheDocument();
  });

  it('沒有 blueprint.read 時看不到入口，直接打網址說明沒有權限，也不讀變更集', async () => {
    // Arrange
    const detail = smkDetail({ myPermissions: ['item.read', 'comment.create'] });
    signedInAs(exampleOwner, { details: [detail] });
    serveItems('SMK', []);
    let changeSetRequests = 0;
    server.use(
      http.get('/v1/projects/SMK/changesets', () => {
        changeSetRequests += 1;
        return new Response(null, { status: 500 });
      }),
    );
    const { router } = renderApp('/projects/SMK');
    expect(await screen.findByRole('heading', { level: 1, name: '冒煙測試' })).toBeInTheDocument();
    expect(within(screen.getByRole('main')).queryByRole('link', { name: '設定' })).not.toBeInTheDocument();

    // Act
    await router.navigate('/projects/SMK/settings');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '沒有權限看這個專案的設定' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到專案' })).toHaveAttribute('href', '/projects/SMK');
    expect(changeSetRequests).toBe(0);
  });

  it('只有 blueprint.read 時唯讀：看得到狀態與欄位，但沒有任何可以改的地方', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read', 'blueprint.read'] })] });
    serveChangeSets('SMK', []);

    // Act
    const { user } = renderApp('/projects/SMK/settings');

    // Assert
    const states = await screen.findByRole('list', { name: '「任務流程」的狀態' });
    expect(within(states).getAllByRole('listitem').map((row) => row.textContent)).toEqual([
      expect.stringContaining('待辦'),
      expect.stringContaining('進行中'),
      expect.stringContaining('完成'),
    ]);
    expect(screen.getByText(/你的角色不能修改設定/)).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /新增|刪除|預覽變更/ })).not.toBeInTheDocument();
    for (const section of ['流程', '類型', '欄位', '角色']) {
      await user.click(within(screen.getByRole('navigation', { name: '設定的區塊' })).getByRole('link', { name: section }));
      const content = within(screen.getByRole('main'));
      expect(content.queryByRole('textbox')).not.toBeInTheDocument();
      expect(content.queryByRole('combobox')).not.toBeInTheDocument();
      expect(content.queryByRole('checkbox')).not.toBeInTheDocument();
      expect(content.queryByRole('button')).not.toBeInTheDocument();
    }
  });

  it('有等待核准的變更集時，設定頁說有幾份並連到清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', [
      makeChangeSet({ number: 3 }),
      makeChangeSet({ number: 2 }),
      makeChangeSet({ number: 1, status: 'applied', appliedVersion: 2 }),
    ]);

    // Act
    renderApp('/projects/SMK/settings');

    // Assert
    const link = await screen.findByRole('link', { name: '有 2 份變更等待核准' });
    expect(link).toHaveAttribute('href', '/projects/SMK/changesets');
    expect(screen.getByRole('link', { name: '所有變更集' })).toHaveAttribute('href', '/projects/SMK/changesets');
  });
});
