import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import { queryKeys } from '../../api/queryKeys';
import { appProject, exampleMember, exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

describe('專案清單', () => {
  it('列出代號、名稱與我的角色', async () => {
    // Arrange
    const unjoined = { ...appProject, key: 'OPS', name: '維運', myRole: null };
    signedInAs(exampleOwner, { projects: [webProject, unjoined] });

    // Act
    renderApp('/projects');

    // Assert
    const table = await screen.findByRole('table', { name: '專案' });
    const webRow = within(table).getByRole('row', { name: /WEB/ });
    expect(within(webRow).getByRole('link', { name: '官網改版' })).toHaveAttribute(
      'href',
      '/projects/WEB',
    );
    expect(within(webRow).getByText('管理員')).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /OPS/ })).getByText('未加入')).toBeInTheDocument();
  });

  it('側欄也列出專案', async () => {
    // Arrange
    signedInAs(exampleMember, { projects: [webProject, appProject] });

    // Act
    renderApp('/my-work');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    expect(await within(nav).findByRole('link', { name: /官網改版/ })).toHaveAttribute(
      'href',
      '/projects/WEB',
    );
    expect(within(nav).getByRole('link', { name: /會員 App/ })).toBeInTheDocument();
  });

  it('組織管理員看到「新增專案」，沒有專案時提示從這裡開始', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [] });

    // Act
    renderApp('/projects');

    // Assert
    expect(await within(await screen.findByRole('main')).findByText('還沒有專案')).toBeInTheDocument();
    expect(screen.getByText(/按「新增專案」/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增專案' })).toBeInTheDocument();
  });

  it('一般成員看不到「新增專案」與「人員」', async () => {
    // Arrange
    signedInAs(exampleMember, { projects: [] });

    // Act
    renderApp('/projects');

    // Assert
    expect(await within(await screen.findByRole('main')).findByText('還沒有專案')).toBeInTheDocument();
    expect(screen.getByText(/請專案的管理員把你加進專案/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增專案' })).not.toBeInTheDocument();
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    expect(within(nav).queryByRole('link', { name: '人員' })).not.toBeInTheDocument();
  });

  it('組織管理員在側欄看得到「人員」', async () => {
    // Arrange
    signedInAs({ ...exampleOwner, role: 'admin' }, { projects: [] });

    // Act
    renderApp('/projects');

    // Assert
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    expect(within(nav).getByRole('link', { name: '人員' })).toHaveAttribute('href', '/people');
  });

  it('載入失敗時顯示訊息並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    server.use(
      http.get('/v1/projects', () => failure(429, 'request.rate_limited', 'slow down'), {
        once: true,
      }),
    );
    const { user } = renderApp('/projects');

    // Act
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(alert).toHaveTextContent('請求太頻繁，請等幾秒再試。');
    expect(await screen.findByRole('table', { name: '專案' })).toBeInTheDocument();
  });
});

describe('專案清單：背景重抓失敗', () => {
  it('已經有清單時重抓失敗，頁面的表格與側欄的連結都留著，另外提示', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject] });
    const { queryClient } = renderApp('/projects');
    const table = await screen.findByRole('table', { name: '專案' });
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    await within(nav).findByRole('link', { name: /官網改版/ });
    server.use(http.get('/v1/projects', () => failure(503, 'server.not_ready', '服務正在啟動。')));

    // Act
    await queryClient.refetchQueries({ queryKey: queryKeys.projects, exact: true });

    // Assert
    expect(await screen.findByText('無法更新專案清單，畫面上可能是舊的。')).toBeInTheDocument();
    expect(within(table).getByRole('link', { name: '官網改版' })).toBeInTheDocument();
    expect(within(nav).getByRole('link', { name: /官網改版/ })).toBeInTheDocument();
    expect(within(nav).getByText(/專案清單無法更新/)).toBeInTheDocument();
    expect(within(nav).queryByText(/專案清單載入失敗/)).not.toBeInTheDocument();
  });
});
