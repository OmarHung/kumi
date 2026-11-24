import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { created, serveActivity } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

function columnNames(board: HTMLElement): string[] {
  return within(board)
    .getAllByRole('heading', { level: 2 })
    .map((heading) => heading.textContent);
}

async function openBoard(path: string) {
  const view = renderApp(path);
  const board = await screen.findByRole('region', { name: '看板' });
  return { ...view, board };
}

describe('專案頁的檢視切換', () => {
  it('切到看板記在網址上，只帶搜尋與負責人；切回表格也一樣', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [smkItem({ number: 1, title: '首頁設計稿', state: 'doing', stateCategory: 'in_progress' })]);
    const { user, router } = renderApp('/projects/SMK?q=首頁&assignee=me&state=doing&sort=due_date:desc');
    await screen.findByRole('table', { name: '項目' });
    const views = screen.getByRole('navigation', { name: '版面' });
    expect(within(views).getByRole('link', { name: '表格' })).toHaveAttribute('aria-current', 'page');

    // Act
    await user.click(within(views).getByRole('link', { name: '看板' }));

    // Assert
    await screen.findByRole('region', { name: '看板' });
    expect(currentPath(router)).toBe(`/projects/SMK?${new URLSearchParams('view=board&q=首頁&assignee=me').toString()}`);
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveValue('首頁');
    expect(screen.getByRole('combobox', { name: '負責人' })).toHaveTextContent('我');
    expect(within(views).getByRole('link', { name: '看板' })).toHaveAttribute('aria-current', 'page');
    await user.click(within(views).getByRole('link', { name: '表格' }));
    expect(await screen.findByRole('table', { name: '項目' })).toBeInTheDocument();
    expect(currentPath(router)).toBe(`/projects/SMK?${new URLSearchParams('q=首頁&assignee=me').toString()}`);
  });

  it('網址上有 view=board 時直接打開看板（重新整理也還在）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', []);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    expect(board).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: '項目' })).not.toBeInTheDocument();
  });
});

describe('看板的欄', () => {
  it('一欄一個狀態，照流程的順序；標題是狀態名稱與已載入的數量', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [
      smkItem({ number: 1 }),
      smkItem({ number: 2 }),
      smkItem({ number: 3, state: 'done', stateCategory: 'done' }),
    ]);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    expect(columnNames(board)).toEqual(['待辦', '進行中', '完成']);
    const todo = within(board).getByRole('region', { name: '待辦' });
    expect(await within(todo).findByText('已載入 2 個項目')).toBeInTheDocument();
    expect(within(todo).getAllByRole('article')).toHaveLength(2);
    const doing = within(board).getByRole('region', { name: '進行中' });
    expect(await within(doing).findByText('這一欄沒有項目')).toBeInTheDocument();
    expect(within(within(board).getByRole('region', { name: '完成' })).getByText('1')).toBeInTheDocument();
  });

  it('還有更多時數量加「+」；每一欄各自載入更多', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const server = serveItemsByQuery(
      'SMK',
      [1, 2, 3].map((number) => smkItem({ number })).concat(smkItem({ number: 4, state: 'doing', stateCategory: 'in_progress' })),
      { pageSize: 2 },
    );
    const { user, board } = await openBoard('/projects/SMK?view=board');
    const todo = within(board).getByRole('region', { name: '待辦' });
    expect(await within(todo).findByText('2+')).toBeInTheDocument();
    expect(within(todo).getByText('已載入 2 個項目，還有更多')).toBeInTheDocument();

    // Act
    await user.click(within(todo).getByRole('button', { name: '載入更多「待辦」的項目' }));

    // Assert
    expect(await within(todo).findByText('3')).toBeInTheDocument();
    expect(within(todo).getAllByRole('article')).toHaveLength(3);
    expect(within(todo).queryByRole('button', { name: /載入更多/ })).not.toBeInTheDocument();
    expect(server.requestsFor('todo').map((url) => url.searchParams.get('cursor'))).toEqual([null, 'page-2']);
    expect(server.requestsFor('doing')).toHaveLength(1);
  });

  it('載入更多失敗時說明原因，已經載入的留著', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [1, 2, 3].map((number) => smkItem({ number })), { pageSize: 2 });
    const { user, board } = await openBoard('/projects/SMK?view=board');
    const todo = within(board).getByRole('region', { name: '待辦' });
    await within(todo).findByText('2+');
    server.use(http.get('/v1/projects/SMK/items', () => failure(503, 'server.not_ready', '服務正在啟動。'), { once: true }));

    // Act
    await user.click(within(todo).getByRole('button', { name: '載入更多「待辦」的項目' }));

    // Assert
    expect(await within(todo).findByRole('alert')).toHaveTextContent('載入更多失敗');
    expect(within(todo).getAllByRole('article')).toHaveLength(2);
  });

  it('每一欄照編號遞增讀，條件裡帶這個流程的類型', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const server = serveItemsByQuery('SMK', []);

    // Act
    await openBoard('/projects/SMK?view=board');

    // Assert
    await waitFor(() => {
      expect(server.requests).toHaveLength(3);
    });
    const todo = server.requestsFor('todo')[0];
    expect(todo?.searchParams.getAll('type')).toEqual(['task']);
    expect(todo?.searchParams.get('sort')).toBe('number:asc');
    expect(todo?.searchParams.get('limit')).toBe('50');
  });
});

describe('多個流程的看板', () => {
  const requirement = agyItem({ number: 1, title: '官網需求' });
  const change = agyItem({ number: 2, type: 'change_request', title: '加一頁', state: 'quoting' });
  const task = agyItem({ number: 3, type: 'task', title: '切版', state: 'todo' });

  it('預設是第一個類型用的流程，每一欄只讀用這個流程的類型', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const server = serveItemsByQuery('AGY', [requirement, change, task]);

    // Act
    const { board } = await openBoard('/projects/AGY?view=board');

    // Assert
    expect(screen.getByRole('combobox', { name: '流程' })).toHaveTextContent('交付流程');
    expect(columnNames(board)).toEqual(['需求確認', '報價', '設計', '開發', '內部驗收', '客戶驗收', '結案']);
    expect(await within(board).findByText('官網需求')).toBeInTheDocument();
    expect(await within(board).findByText('加一頁')).toBeInTheDocument();
    expect(within(board).queryByText('切版')).not.toBeInTheDocument();
    expect(server.requestsFor('confirming')[0]?.searchParams.getAll('type')).toEqual(['requirement', 'change_request']);
  });

  it('換流程記在網址上，欄換成那個流程的狀態', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [requirement, change, task]);
    const { user, router, board } = await openBoard('/projects/AGY?view=board');

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '流程' }), '任務流程');

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY?view=board&workflow=task_flow');
    expect(columnNames(board)).toEqual(['待辦', '進行中', '完成']);
    expect(await within(board).findByText('切版')).toBeInTheDocument();
    expect(within(board).queryByText('官網需求')).not.toBeInTheDocument();
  });

  it('流程是選了才換：打開清單用方向鍵移動反白、按 Esc 關上都不換；再打開移過去按 Enter 才換', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [requirement, change, task]);
    const { user, router, board } = await openBoard('/projects/AGY?view=board');
    const field = screen.getByRole('combobox', { name: '流程' });
    const options = await openOptions(user, field);

    // Act
    await user.keyboard('{ArrowDown}');
    await user.keyboard('{Escape}');

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['交付流程', '任務流程']);
    expect(field).toHaveAttribute('aria-expanded', 'false');
    expect(field).toHaveTextContent('交付流程');
    expect(field).toHaveFocus();
    expect(currentPath(router)).toBe('/projects/AGY?view=board');
    expect(columnNames(board)[0]).toBe('需求確認');
    await user.keyboard('{ArrowDown}');
    const reopened = await openOptions(user, field);
    expect(field).toHaveAttribute('aria-activedescendant', reopened[0]?.id);
    await user.keyboard('{ArrowDown}{Enter}');
    expect(currentPath(router)).toBe('/projects/AGY?view=board&workflow=task_flow');
    expect(field).toHaveTextContent('任務流程');
    expect(columnNames(board)).toEqual(['待辦', '進行中', '完成']);
  });

  it('項目面板開著時：流程的清單上按 Esc 只關清單，面板留著', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [requirement, change, task]);
    serveItem(detailOf(requirement));
    serveActivity(requirement.key, [created()]);
    const { user, router } = await openBoard('/projects/AGY?view=board&item=AGY-1');
    const panel = await screen.findByRole('complementary', { name: '項目 AGY-1' });
    const field = screen.getByRole('combobox', { name: '流程' });
    await openOptions(user, field);

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(field).toHaveAttribute('aria-expanded', 'false');
    expect(field).toHaveFocus();
    expect(screen.getByRole('complementary', { name: '項目 AGY-1' })).toBe(panel);
    expect(currentPath(router)).toBe('/projects/AGY?view=board&item=AGY-1');
  });

  it('網址上不認得的流程當成預設的', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', []);

    // Act
    const { board } = await openBoard('/projects/AGY?view=board&workflow=nope');

    // Assert
    expect(screen.getByRole('combobox', { name: '流程' })).toHaveTextContent('交付流程');
    expect(columnNames(board)[0]).toBe('需求確認');
  });

  it('只有一個流程時沒有流程選單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', []);

    // Act
    await openBoard('/projects/SMK?view=board');

    // Assert
    expect(screen.queryByRole('combobox', { name: '流程' })).not.toBeInTheDocument();
  });
});

describe('看板的篩選', () => {
  it('搜尋與負責人套用到每一欄，條件記在網址上；清除條件留著流程', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const server = serveItemsByQuery('AGY', [
      agyItem({ number: 1, type: 'task', state: 'todo', title: '切版' }),
      agyItem({ number: 2, type: 'task', state: 'doing', title: '首頁切版', assignee: { personId: exampleOwner.personId, displayName: '範例使用者' } }),
      agyItem({ number: 3, type: 'task', state: 'done', title: '寫文件' }),
    ]);
    const { user, router, board } = await openBoard('/projects/AGY?view=board&workflow=task_flow');
    await within(board).findByText('寫文件');

    // Act
    await user.type(screen.getByRole('searchbox', { name: '搜尋' }), '切版{Enter}');
    await chooseOption(user, screen.getByRole('combobox', { name: '負責人' }), '我');

    // Assert
    await waitFor(() => {
      expect(within(board).queryByText('切版')).not.toBeInTheDocument();
    });
    expect(within(board).getByText('首頁切版')).toBeInTheDocument();
    expect(within(board).queryByText('寫文件')).not.toBeInTheDocument();
    for (const state of ['todo', 'doing', 'done']) {
      const last = server.requestsFor(state).at(-1);
      expect(last?.searchParams.get('q')).toBe('切版');
      expect(last?.searchParams.get('assignee')).toBe('me');
    }
    expect(currentPath(router)).toBe(
      `/projects/AGY?${new URLSearchParams('view=board&workflow=task_flow&assignee=me&q=切版').toString()}`,
    );
    await user.click(screen.getByRole('button', { name: '清除條件' }));
    expect(currentPath(router)).toBe('/projects/AGY?view=board&workflow=task_flow');
  });

  it('表格的清單不受影響：看板用自己的請求', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const tableRequests = serveItems('SMK', [smkItem({ number: 1 })]);
    const { user } = renderApp('/projects/SMK');
    await screen.findByRole('table', { name: '項目' });
    serveItemsByQuery('SMK', [smkItem({ number: 1 })]);

    // Act
    await user.click(screen.getByRole('link', { name: '看板' }));

    // Assert
    const board = await screen.findByRole('region', { name: '看板' });
    expect(await within(board).findByText('首頁設計稿')).toBeInTheDocument();
    expect(tableRequests).toHaveLength(1);
  });
});
