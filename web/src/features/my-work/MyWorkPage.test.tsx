import { act, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, ok, signedInAs } from '../../test/api';
import {
  appProject,
  daysFromToday,
  exampleOwner,
  makeItem,
  webProject,
} from '../../test/fixtures';
import { formatDate } from '../../domain/dates';
import { serveActivity } from '../../test/activity';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { removeItemFromCache } from '../item-table/itemCache';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const overdue = makeItem({
  number: 3,
  type: 'requirement',
  title: '首頁改版需求',
  state: 'internal_qa',
  stateCategory: 'in_progress',
  priority: 'high',
  dueDate: daysFromToday(-2),
});

const later = makeItem({
  projectKey: 'APP',
  number: 2,
  title: '登入流程',
  state: 'todo',
  stateCategory: 'not_started',
  priority: 'urgent',
  dueDate: daysFromToday(3),
});

const undated = makeItem({
  number: 5,
  title: '整理素材',
  state: 'doing',
  stateCategory: 'in_progress',
});

function rowFor(itemKey: string): HTMLElement {
  return screen.getByRole('row', { name: new RegExp(itemKey) });
}

describe('我的工作', () => {
  it('列出編號、標題、專案、狀態、優先度與到期日', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: [overdue, later] });

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('table', { name: '已過期' })).toBeInTheDocument();
    const row = rowFor('WEB-3');
    expect(within(row).getByRole('link', { name: '首頁改版需求' })).toBeInTheDocument();
    expect(await within(row).findByText('官網改版')).toBeInTheDocument();
    expect(within(row).getByText('內部驗收')).toBeInTheDocument();
    expect(within(row).getByText('高')).toBeInTheDocument();
    expect(within(row).getByText(formatDate(overdue.dueDate ?? ''))).toBeInTheDocument();
    expect(within(rowFor('APP-2')).getByText('緊急')).toBeInTheDocument();
  });

  it('過期的到期日標出來，還沒到期的不標', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: [later, overdue] });

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('table', { name: '已過期' });
    expect(within(rowFor('WEB-3')).getByText('已過期')).toBeInTheDocument();
    expect(within(rowFor('APP-2')).queryByText('已過期')).not.toBeInTheDocument();
  });

  it('依到期日排，過期的在最上面，沒有到期日的在最後', async () => {
    // Arrange
    signedInAs(exampleOwner, {
      projects: [webProject, appProject],
      myWork: [undated, later, overdue],
    });

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('table', { name: '已過期' });
    const keys = screen
      .getAllByRole('table')
      .flatMap((table) => within(table).getAllByRole('row').slice(1))
      .map((row) => within(row).getAllByRole('cell')[0]?.textContent);
    expect(keys).toEqual(['WEB-3', 'APP-2', 'WEB-5']);
  });

  it('專案的藍圖還沒拿到或拿不到時，狀態用類別的中文顯示', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdue] });
    server.use(
      http.get('/v1/projects/WEB', () => failure(403, 'work.forbidden', '沒有權限。')),
    );

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('table', { name: '已過期' });
    expect(await within(rowFor('WEB-3')).findByText('進行中')).toBeInTheDocument();
    expect(within(rowFor('WEB-3')).getByText('WEB')).toBeInTheDocument();
  });

  // F 輪（U1）：寬螢幕點標題改成在旁邊開項目面板（原本換到它的項目頁），連結本身照樣指向整頁。
  it('點項目的標題在旁邊開它的面板', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdue] });
    serveItem(detailOf(overdue, '首頁要改版的說明'));
    serveActivity(overdue.key, []);
    serveItems('WEB', []);
    const { user, router } = renderApp('/my-work');
    const row = await screen.findByRole('row', { name: /WEB-3/ });
    expect(within(row).getByRole('link', { name: '首頁改版需求' })).toHaveAttribute('href', '/projects/WEB/items/WEB-3');

    // Act
    await user.click(within(row).getByRole('link', { name: '首頁改版需求' }));

    // Assert
    const panel = await screen.findByRole('complementary', { name: '項目 WEB-3' });
    expect(currentPath(router)).toBe('/my-work?item=WEB-3');
    expect(await within(panel).findByText('首頁要改版的說明')).toBeInTheDocument();
    expect(within(row).getByRole('link', { name: '首頁改版需求' })).toHaveAttribute('aria-current', 'true');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('我的工作');
  });

  it('沒有工作時說明這裡會出現什麼', async () => {
    // Arrange
    signedInAs(exampleOwner, { myWork: [] });

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByText('目前沒有你負責的工作')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('載入中顯示狀態，失敗時顯示訊息並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject], myWork: [overdue] });
    server.use(
      http.get('/v1/items', () => failure(500, 'server.internal_error', 'boom'), {
        once: true,
      }),
    );
    const { user } = renderApp('/my-work');

    // Act
    expect(await screen.findByText('正在載入我的工作…')).toBeInTheDocument();
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(alert).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');
    expect(await screen.findByRole('table', { name: '已過期' })).toBeInTheDocument();
  });

  it('首頁直接進到我的工作', async () => {
    // Arrange
    signedInAs(exampleOwner, { myWork: [] });
    server.use(http.get('/v1/my-work', () => ok({ items: [] })));

    // Act
    const { router } = renderApp('/');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/my-work');
  });
});

describe('我的工作：背景重抓失敗', () => {
  const STALE = '無法更新我的工作，清單可能是舊的。';

  it('已經有清單時重抓失敗，留著原本的清單，提示可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: [overdue] });
    const { user, queryClient } = renderApp('/my-work');
    await screen.findByRole('table', { name: '已過期' });
    server.use(http.get('/v1/items', () => failure(503, 'server.not_ready', '服務正在啟動。'), { once: true }));

    // Act
    await queryClient.refetchQueries({ queryKey: ['my-work'] });

    // Assert
    const notice = await screen.findByText(STALE);
    expect(rowFor('WEB-3')).toBeInTheDocument();
    await user.click(within(notice.parentElement as HTMLElement).getByRole('button', { name: '重試' }));
    await waitFor(() => {
      expect(screen.queryByText(STALE)).not.toBeInTheDocument();
    });
  });
});

describe('我的工作：在項目面板裡刪除項目之後', () => {
  it('表格上那一列不見，別的列還在', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject], myWork: [overdue, later] });
    const { queryClient } = renderApp('/my-work');
    await screen.findByRole('table', { name: '已過期' });

    // Act
    act(() => {
      removeItemFromCache(queryClient, 'WEB', 'WEB-3');
    });

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('row', { name: /WEB-3/ })).not.toBeInTheDocument();
    });
    expect(rowFor('APP-2')).toBeInTheDocument();
  });
});
