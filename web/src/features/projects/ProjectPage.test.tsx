import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import { appProject, exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

describe('專案頁', () => {
  it('顯示專案名稱、代號、我的角色、成員按鈕與項目表格', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveItems('WEB', [makeItem()]);

    // Act
    renderApp('/projects/WEB');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '官網改版' })).toBeInTheDocument();
    const main = screen.getByRole('main');
    expect(within(main).getByText('WEB')).toBeInTheDocument();
    expect(within(main).getByText('我的角色：管理員')).toBeInTheDocument();
    expect(within(main).getByRole('button', { name: '成員' })).toBeInTheDocument();
    expect(within(main).getByRole('region', { name: '項目' })).toBeInTheDocument();
    expect(await within(main).findByRole('table', { name: '項目' })).toBeInTheDocument();
  });

  it('在側欄換到別的專案時，表格換成那個專案的項目', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject] });
    serveItems('WEB', [makeItem({ title: '官網的項目' })]);
    serveItems('APP', [makeItem({ projectKey: 'APP', title: 'App 的項目' })]);
    const { user } = renderApp('/projects/WEB');
    expect(await screen.findByText('官網的項目')).toBeInTheDocument();

    // Act
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    await user.click(within(nav).getByRole('link', { name: /會員 App/ }));

    // Assert
    expect(await screen.findByText('App 的項目')).toBeInTheDocument();
    expect(screen.queryByText('官網的項目')).not.toBeInTheDocument();
  });

  it('自訂角色用藍圖裡的名稱', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [appProject] });
    serveItems('APP', []);

    // Act
    renderApp('/projects/APP');

    // Assert
    expect(await screen.findByText('我的角色：專案經理')).toBeInTheDocument();
  });

  it('專案不存在時顯示找不到，可以回到專案清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, router } = renderApp('/projects/NOPE');

    // Act
    expect(
      await screen.findByRole('heading', { level: 1, name: '找不到這個專案' }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: '回到專案清單' }));

    // Assert
    expect(currentPath(router)).toBe('/projects');
  });

  it('其他錯誤顯示訊息並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveItems('WEB', []);
    server.use(
      http.get('/v1/projects/WEB', () => failure(503, 'server.not_ready', 'starting'), {
        once: true,
      }),
    );
    const { user } = renderApp('/projects/WEB');

    // Act
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(alert).toHaveTextContent('專案載入失敗');
    expect(await screen.findByRole('heading', { level: 1, name: '官網改版' })).toBeInTheDocument();
  });
});

describe('專案頁：背景重抓失敗', () => {
  const STALE_PROJECT = '無法更新專案的最新資料，畫面上可能是舊的。';

  it('專案內容重抓失敗時，表格與編輯到一半的格子都留著，另外提示可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveItems('WEB', [makeItem({ title: '官網的項目' })]);
    const { user, queryClient } = renderApp('/projects/WEB');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('button', { name: '估計：未填' }));
    await user.type(within(table).getByRole('textbox', { name: '估計' }), '12');
    server.use(http.get('/v1/projects/WEB', () => failure(503, 'server.not_ready', '服務正在啟動。'), { once: true }));

    // Act
    await queryClient.refetchQueries({ queryKey: ['project', 'WEB'], exact: true });

    // Assert
    const notice = await screen.findByText(STALE_PROJECT);
    expect(screen.queryByText('專案載入失敗')).not.toBeInTheDocument();
    expect(within(table).getByRole('textbox', { name: '估計' })).toHaveValue('12');
    await user.click(within(notice.parentElement as HTMLElement).getByRole('button', { name: '重試' }));
    await waitFor(() => {
      expect(screen.queryByText(STALE_PROJECT)).not.toBeInTheDocument();
    });
    expect(within(table).getByRole('textbox', { name: '估計' })).toHaveValue('12');
  });
});
