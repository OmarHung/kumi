import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

async function openBoard(path: string) {
  const view = renderApp(path);
  const board = await screen.findByRole('region', { name: '看板' });
  return { ...view, board };
}

function serveCreate(projectKey: string, created: Item): unknown[] {
  const bodies: unknown[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items`, async ({ request }) => {
      bodies.push(await request.json());
      return ok(created, 201);
    }),
  );
  return bodies;
}

describe('看板的卡片', () => {
  it('顯示編號、標題（連到項目頁）、負責人、優先度、到期日；過期的標出來', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [
      smkItem({ number: 7, title: '首頁設計稿', priority: 'high', dueDate: daysFromToday(-2) }),
      smkItem({ number: 8, title: '寫文件', assignee: null, dueDate: daysFromToday(3) }),
    ]);

    // Act
    const { board, user, router } = await openBoard('/projects/SMK?view=board');

    // Assert
    const card = await within(board).findByRole('article', { name: '首頁設計稿' });
    expect(within(card).getByText('SMK-7')).toBeInTheDocument();
    expect(within(card).getByText('範例使用者')).toBeInTheDocument();
    expect(within(card).getByText('高')).toBeInTheDocument();
    expect(within(card).getByText('已過期')).toBeInTheDocument();
    expect(within(card).queryByText('任務')).not.toBeInTheDocument();
    const other = within(board).getByRole('article', { name: '寫文件' });
    expect(within(other).getByText('沒有負責人')).toBeInTheDocument();
    expect(within(other).queryByText('已過期')).not.toBeInTheDocument();
    // 點標題是在旁邊開項目面板，不是拖曳（Pointer 感應器要移動一點距離才開始）。
    // F 輪（U1）：原本是換到項目頁；寬螢幕改成開面板，連結本身照樣指向整頁。
    serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
    serveActivity('SMK-7', [created()]);
    const title = within(card).getByRole('link', { name: '首頁設計稿' });
    await user.click(title);
    expect(currentPath(router)).toBe('/projects/SMK?view=board&item=SMK-7');
    expect(await screen.findByRole('complementary', { name: '項目 SMK-7' })).toBeInTheDocument();
    expect(title).toHaveAttribute('href', '/projects/SMK/items/SMK-7');
    expect(title).toHaveAttribute('aria-current', 'true');
    expect(screen.queryByText(/開始拖曳/)).not.toBeInTheDocument();
  });

  it('完成的項目過了到期日也不算過期', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [smkItem({ number: 1, state: 'done', stateCategory: 'done', dueDate: daysFromToday(-5) })]);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    const card = await within(board).findByRole('article', { name: '首頁設計稿' });
    expect(within(card).queryByText('已過期')).not.toBeInTheDocument();
  });

  it('流程有多種類型時，卡片寫出類型', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' }), agyItem({ number: 2, type: 'change_request', title: '加一頁' })]);

    // Act
    const { board } = await openBoard('/projects/AGY?view=board');

    // Assert
    expect(within(await within(board).findByRole('article', { name: '官網需求' })).getByText('需求')).toBeInTheDocument();
    expect(within(board).getByRole('article', { name: '加一頁' })).toHaveTextContent('需求變更單');
  });
});

describe('看板的快速新增', () => {
  it('在第一欄新增：只有一種類型時只要標題，新項目出現在第一欄最上面，可以接著打', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const boardServer = serveItemsByQuery('SMK', [smkItem({ number: 1, title: '既有的項目' })]);
    const bodies = serveCreate('SMK', smkItem({ number: 2, title: '新的任務' }));
    const { board, user } = await openBoard('/projects/SMK?view=board');
    const todo = within(board).getByRole('region', { name: '待辦' });
    await within(todo).findByText('既有的項目');
    expect(within(within(board).getByRole('region', { name: '進行中' })).queryByRole('button', { name: /新增項目/ })).not.toBeInTheDocument();

    // Act
    await user.click(within(todo).getByRole('button', { name: '新增項目' }));
    const form = within(todo).getByRole('form', { name: '新增項目' });
    expect(within(form).queryByRole('combobox', { name: '類型' })).not.toBeInTheDocument();
    const title = within(form).getByRole('textbox', { name: '標題' });
    expect(title).toHaveFocus();
    await user.type(title, '新的任務{Enter}');

    // Assert
    await within(todo).findByRole('article', { name: '新的任務' });
    expect(within(todo).getAllByRole('article').map((card) => card.getAttribute('data-card'))).toEqual(['SMK-2', 'SMK-1']);
    expect(bodies).toEqual([{ type: 'task', title: '新的任務' }]);
    expect(title).toHaveValue('');
    expect(title).toHaveFocus();
    expect(within(form).getByRole('status')).toHaveTextContent('已新增 SMK-2');
    expect(boardServer.requestsFor('todo')).toHaveLength(1);
  });

  it('流程有多種類型時要選類型，只列用這個流程的類型；Esc 收起來、焦點回到按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', []);
    const bodies = serveCreate('AGY', agyItem({ number: 5, type: 'change_request', title: '加一頁' }));
    const { board, user } = await openBoard('/projects/AGY?view=board');
    const first = within(board).getByRole('region', { name: '需求確認' });
    await user.click(within(first).getByRole('button', { name: '新增項目' }));
    const form = within(first).getByRole('form', { name: '新增項目' });
    const type = within(form).getByRole('combobox', { name: '類型' });

    // Act
    expect((await openOptions(user, type)).map((option) => option.textContent)).toEqual(['需求', '需求變更單']);
    await chooseOption(user, type, '需求變更單');
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '加一頁{Enter}');
    await within(first).findByRole('article', { name: '加一頁' });
    await user.keyboard('{Escape}');

    // Assert
    expect(bodies).toEqual([{ type: 'change_request', title: '加一頁' }]);
    expect(within(first).queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
    expect(within(first).getByRole('button', { name: '新增項目' })).toHaveFocus();
  });

  it('後端的錯誤對到欄位', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', []);
    server.use(
      http.post('/v1/projects/SMK/items', () =>
        failure(422, 'item.title_too_long', '標題太長了。', [{ code: 'item.title_too_long', path: 'title', message: '標題最多 200 個字。' }]),
      ),
    );
    const { board, user } = await openBoard('/projects/SMK?view=board');
    const todo = within(board).getByRole('region', { name: '待辦' });
    await user.click(within(todo).getByRole('button', { name: '新增項目' }));

    // Act
    await user.type(within(todo).getByRole('textbox', { name: '標題' }), '很長的標題{Enter}');

    // Assert
    expect(await within(todo).findByText('標題最多 200 個字。')).toBeInTheDocument();
    await user.click(within(todo).getByRole('button', { name: '取消' }));
    expect(within(todo).getByRole('button', { name: '新增項目' })).toHaveFocus();
  });
});

describe('看板的權限', () => {
  it('沒有 item.create 沒有快速新增；沒有 item.transition 卡片不能拖、也沒有「移到…」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });
    serveItemsByQuery('SMK', [smkItem({ number: 1 })]);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    const card = await within(board).findByRole('article', { name: '首頁設計稿' });
    expect(within(board).queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /移到/ })).not.toBeInTheDocument();
    expect(card).not.toHaveAttribute('aria-describedby');
    fireEvent.pointerDown(card, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 0, clientY: 0 });
    fireEvent.pointerMove(document, { clientX: 40, clientY: 0 });
    fireEvent.pointerUp(document);
    expect(screen.queryByText(/開始拖曳/)).not.toBeInTheDocument();
  });
});

describe('看板的讀取失敗', () => {
  it('第一次就讀不到的欄顯示錯誤，可以重試；其他欄照常', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [smkItem({ number: 1 })]);
    let failures = 0;
    server.use(
      http.get('/v1/projects/SMK/items', ({ request }) => {
        if (new URL(request.url).searchParams.get('state') !== 'doing' || failures > 0) return undefined;
        failures += 1;
        return failure(503, 'server.not_ready', '服務正在啟動。');
      }),
    );
    const { board, user } = await openBoard('/projects/SMK?view=board');
    const doing = within(board).getByRole('region', { name: '進行中' });

    // Act
    const alert = await within(doing).findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(alert).toHaveTextContent('這一欄載入失敗');
    expect(await within(doing).findByText('這一欄沒有項目')).toBeInTheDocument();
    expect(within(board).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
  });

  it('背景重抓失敗時卡片留著，欄裡多一行提示可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [smkItem({ number: 1 })]);
    const { board, user, queryClient } = await openBoard('/projects/SMK?view=board');
    const todo = within(board).getByRole('region', { name: '待辦' });
    await within(todo).findByRole('article', { name: '首頁設計稿' });
    server.use(http.get('/v1/projects/SMK/items', () => failure(503, 'server.not_ready', '服務正在啟動。'), { once: true }));

    // Act
    await queryClient.refetchQueries({
      predicate: (query) => query.queryKey[4] === 'board-column' && JSON.stringify(query.queryKey[3]).includes('"todo"'),
    });

    // Assert
    const notice = await within(todo).findByText('無法更新這一欄的最新資料，畫面上可能是舊的。');
    expect(within(todo).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
    expect(within(todo).queryByText('這一欄載入失敗')).not.toBeInTheDocument();
    await user.click(within(notice.parentElement as HTMLElement).getByRole('button', { name: '重試' }));
    await waitFor(() => {
      expect(within(todo).queryByText('無法更新這一欄的最新資料，畫面上可能是舊的。')).not.toBeInTheDocument();
    });
  });
});
