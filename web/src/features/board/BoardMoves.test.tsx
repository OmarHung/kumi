import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { holdTransition, serveItemsByQuery } from '../../test/board';
import { created, serveActivity } from '../../test/activity';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, failTransition, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const inQa = agyItem({ number: 3, title: '官網需求', state: 'internal_qa', stateCategory: 'in_progress' });
const inClientQa = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress' });

async function openBoard(path: string) {
  const view = renderApp(path);
  const board = await screen.findByRole('region', { name: '看板' });
  return { ...view, board };
}

function column(board: HTMLElement, name: string): HTMLElement {
  return within(board).getByRole('region', { name });
}

async function openMenu(user: ReturnType<typeof renderApp>['user'], board: HTMLElement, item: Item) {
  const card = await within(board).findByRole('article', { name: item.title });
  await user.click(within(card).getByRole('button', { name: `${item.key} 移到…` }));
  return within(card).getByRole('menu', { name: `把 ${item.key} 移到` });
}

describe('「移到…」選單', () => {
  it('列出流程裡其他的狀態：走得到的在前；走不到的停用並寫出原因', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [inQa]);
    const { board, user } = await openBoard('/projects/AGY?view=board');

    // Act
    const menu = await openMenu(user, board, inQa);

    // Assert
    const items = within(menu).getAllByRole('menuitem');
    expect(items.map((item) => item.textContent)).toEqual([
      '開發',
      '需求確認流程沒有這條路',
      '報價流程沒有這條路',
      '設計流程沒有這條路',
      '客戶驗收需要的角色：專案經理',
      '結案流程沒有這條路',
    ]);
    expect(items[0]).toHaveAccessibleName('開發');
    expect(items[0]).not.toHaveAttribute('aria-disabled');
    const clientQa = within(menu).getByRole('menuitem', { name: '客戶驗收' });
    expect(clientQa).toHaveAttribute('aria-disabled', 'true');
    expect(clientQa).toHaveAccessibleDescription('需要的角色：專案經理');
    expect(within(menu).getByRole('separator')).toBeInTheDocument();
  });

  it('打開時焦點在第一個選項；方向鍵移動（走不到的也停得到），Esc 關上回到按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [inQa]);
    const { board, user } = await openBoard('/projects/AGY?view=board');
    const card = await within(board).findByRole('article', { name: inQa.title });
    within(card).getByRole('button', { name: 'AGY-3 移到…' }).focus();

    // Act
    await user.keyboard('{Enter}');
    const menu = within(card).getByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: '開發' })).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(within(menu).getByRole('menuitem', { name: '需求確認' })).toHaveFocus();
    await user.keyboard('{ArrowUp}{ArrowUp}');
    expect(within(menu).getByRole('menuitem', { name: '結案' })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(within(menu).getByRole('menuitem', { name: '開發' })).toHaveFocus();
    await user.keyboard('{End}{Enter}');
    expect(within(card).getByRole('menu')).toBeInTheDocument();
    await user.keyboard('{Escape}');

    // Assert
    expect(within(card).queryByRole('menu')).not.toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'AGY-3 移到…' })).toHaveFocus();
    expect(within(card).getByRole('button', { name: 'AGY-3 移到…' })).toHaveAttribute('aria-expanded', 'false');
  });

  it('Tab 關上選單，焦點照常往下走', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [inQa]);
    const { board, user } = await openBoard('/projects/AGY?view=board');
    await openMenu(user, board, inQa);

    // Act
    await user.tab();

    // Assert
    expect(within(board).queryByRole('menu')).not.toBeInTheDocument();
  });

  it('點在選單外面就關上', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [inQa]);
    const { board, user } = await openBoard('/projects/AGY?view=board');
    await openMenu(user, board, inQa);

    // Act
    await user.click(within(board).getByRole('heading', { name: '需求確認' }));

    // Assert
    expect(within(board).queryByRole('menu')).not.toBeInTheDocument();
  });
});

describe('看板上改狀態', () => {
  it('先把卡片移過去（送出中），成功後留在新的欄，焦點跟著卡片', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const boardServer = serveItemsByQuery('SMK', [item]);
    const moved = { ...item, state: 'doing', stateCategory: 'in_progress' as const };
    const held = holdTransition(item, () => ok(detailOf(moved)));
    const { board, user } = await openBoard('/projects/SMK?view=board');
    const menu = await openMenu(user, board, item);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '進行中' }));

    // Assert：送出中
    const doing = column(board, '進行中');
    const pendingCard = await within(doing).findByRole('article', { name: '首頁設計稿' });
    expect(pendingCard).toHaveAttribute('aria-busy', 'true');
    expect(within(pendingCard).getByText('送出中…')).toBeInTheDocument();
    expect(within(column(board, '待辦')).queryByRole('article')).not.toBeInTheDocument();
    expect(within(pendingCard).getByRole('button', { name: 'SMK-1 移到…' })).toHaveFocus();
    expect(within(board).getByText('正在把 SMK-1 移到「進行中」…')).toBeInTheDocument();
    await waitFor(() => {
      expect(held.bodies).toEqual([{ to: 'doing' }]);
    });

    // Act：後端回應
    held.release();

    // Assert：成功
    await waitFor(() => {
      expect(within(doing).getByRole('article', { name: '首頁設計稿' })).not.toHaveAttribute('aria-busy');
    });
    expect(within(board).getByText('已把 SMK-1 移到「進行中」。')).toBeInTheDocument();
    expect(within(doing).getByRole('button', { name: 'SMK-1 移到…' })).toHaveFocus();
    expect(boardServer.requestsFor('doing')).toHaveLength(1);
  });

  it('成功後表格與項目頁看到的是同一份：用回傳的項目更新快取，不必重抓', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const boardServer = serveItemsByQuery('SMK', [item]);
    serveItem(detailOf(item));
    serveActivity(item.key, [created()]);
    const { user } = renderApp('/projects/SMK');
    await screen.findByRole('table', { name: '項目' });
    await user.click(screen.getByRole('link', { name: '看板' }));
    const board = await screen.findByRole('region', { name: '看板' });
    const held = holdTransition(item, () => ok(detailOf({ ...item, state: 'done', stateCategory: 'done' })));
    const menu = await openMenu(user, board, item);
    await user.click(within(menu).getByRole('menuitem', { name: '完成' }));
    held.release();
    await within(board).findByText('已把 SMK-1 移到「完成」。');
    const tableRequests = boardServer.requests.filter((url) => !url.searchParams.has('sort')).length;

    // Act
    await user.click(screen.getByRole('link', { name: '表格' }));

    // Assert
    const table = await screen.findByRole('table', { name: '項目' });
    expect(within(table).getByRole('button', { name: '狀態：完成' })).toBeInTheDocument();
    expect(boardServer.requests.filter((url) => !url.searchParams.has('sort'))).toHaveLength(tableRequests);
    await user.click(within(table).getByRole('link', { name: '首頁設計稿' }));
    const fields = await screen.findByRole('region', { name: '欄位' });
    expect(within(fields).getByRole('button', { name: '狀態：完成' })).toBeInTheDocument();
  });

  it('成功後「我的工作」標成過期，回去看時重抓', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    // 我的工作的清單是 /v1/items（沒給 items 時用 myWork）；側欄的數量讀 /v1/my-work，下面數它被讀了幾次。
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [item] });
    let myWorkRequests = 0;
    server.use(
      http.get('/v1/my-work', () => {
        myWorkRequests += 1;
        return ok({ items: [item] });
      }),
    );
    serveItemsByQuery('SMK', [item]);
    const { user } = renderApp('/my-work');
    await screen.findByRole('link', { name: item.title });
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    await user.click(within(nav).getByRole('link', { name: /冒煙測試/ }));
    await user.click(await screen.findByRole('link', { name: '看板' }));
    const board = await screen.findByRole('region', { name: '看板' });
    const held = holdTransition(item, () => ok(detailOf({ ...item, state: 'doing', stateCategory: 'in_progress' })));
    await user.click(within(await openMenu(user, board, item)).getByRole('menuitem', { name: '進行中' }));
    held.release();
    await within(board).findByText('已把 SMK-1 移到「進行中」。');

    // Act
    await user.click(within(nav).getByRole('link', { name: '我的工作' }));

    // Assert
    await waitFor(() => {
      expect(myWorkRequests).toBe(2);
    });
  });

  it('流程沒有這條路：卡片回到原欄，上方說明原因（螢幕閱讀器會唸），焦點回到卡片', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [inQa]);
    failTransition(inQa, 422, 'item.transition_not_allowed', '「內部驗收」不能直接轉到「開發」。', []);
    const { board, user } = await openBoard('/projects/AGY?view=board');
    const menu = await openMenu(user, board, inQa);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '開發' }));

    // Assert
    const alert = await within(board).findByRole('alert');
    expect(alert).toHaveTextContent('AGY-3「官網需求」沒有移到「開發」，留在「內部驗收」。');
    expect(alert).toHaveTextContent('「內部驗收」不能直接轉到「開發」。');
    const qa = column(board, '內部驗收');
    expect(within(qa).getByRole('article', { name: '官網需求' })).toBeInTheDocument();
    expect(within(column(board, '開發')).queryByRole('article')).not.toBeInTheDocument();
    expect(within(qa).getByRole('button', { name: 'AGY-3 移到…' })).toHaveFocus();
  });

  it('角色不對：列出需要的角色與我的角色，可以到「成員」調整', async () => {
    // Arrange
    const detail = agyDetail({ project: { ...agyDetail().project, myRole: 'project_manager' } });
    signedInAs(exampleOwner, { details: [detail] });
    serveItemsByQuery('AGY', [inQa]);
    failTransition(inQa, 403, 'item.transition_role_required', '從「內部驗收」轉到「客戶驗收」只有「專案經理」可以做。', [
      { code: 'item.transition_role_required', path: 'role', message: '「專案經理」（project_manager）' },
    ]);
    const { board, user } = await openBoard('/projects/AGY?view=board');
    const menu = await openMenu(user, board, inQa);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '客戶驗收' }));

    // Assert
    const alert = await within(board).findByRole('alert');
    expect(within(alert).getByText('需要的角色').nextElementSibling).toHaveTextContent('專案經理');
    expect(within(alert).getByText('你的角色').nextElementSibling).toHaveTextContent('專案經理');
    await user.click(within(alert).getByRole('button', { name: '到「成員」調整角色' }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  // G 輪（U3）：要先填欄位時不再只在看板上方說明，改成卡片回到原欄、開對話框補填（細節在 BoardFill.test.tsx）。
  it('要先填的欄位：卡片回到原本的欄，開對話框讓人補填', async () => {
    // Arrange
    const detail = agyDetail({ project: { ...agyDetail().project, myRole: 'project_manager' } });
    signedInAs(exampleOwner, { details: [detail] });
    serveItemsByQuery('AGY', [inClientQa]);
    failTransition(inClientQa, 422, 'item.transition_fields_required', '要先填「客戶驗收日」才能進入「結案」。', [
      { code: 'item.transition_fields_required', path: 'client_signoff_date', message: '要先填「客戶驗收日」。' },
    ]);
    const { board, user } = await openBoard('/projects/AGY?view=board');
    const menu = await openMenu(user, board, inClientQa);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '結案' }));
    const dialog = await screen.findByRole('dialog', { name: '移到「結案」之前要先填' });

    // Assert
    expect(within(dialog).getByLabelText('客戶驗收日')).toBeInTheDocument();
    expect(within(column(board, '客戶驗收')).getByRole('article', { name: '驗收中的需求' })).toBeInTheDocument();
    expect(within(board).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('送出中的卡片不能再動；兩張卡片可以同時在送出', async () => {
    // Arrange
    const first = smkItem({ number: 1, title: '第一張' });
    const second = smkItem({ number: 2, title: '第二張' });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [first, second]);
    const heldFirst = holdTransition(first, () => ok(detailOf({ ...first, state: 'doing', stateCategory: 'in_progress' })));
    const heldSecond = holdTransition(second, () => failure(422, 'item.transition_not_allowed', '不能這樣轉。'));
    const { board, user } = await openBoard('/projects/SMK?view=board');

    // Act
    await user.click(within(await openMenu(user, board, first)).getByRole('menuitem', { name: '進行中' }));
    await user.click(within(await openMenu(user, board, second)).getByRole('menuitem', { name: '完成' }));

    // Assert：兩張都在送出，送出中的不能再打開選單
    const doing = column(board, '進行中');
    const done = column(board, '完成');
    expect(await within(doing).findByRole('article', { name: '第一張' })).toHaveAttribute('aria-busy', 'true');
    expect(within(done).getByRole('article', { name: '第二張' })).toHaveAttribute('aria-busy', 'true');
    const busyButton = within(doing).getByRole('button', { name: 'SMK-1 移到…' });
    expect(busyButton).toHaveAttribute('aria-disabled', 'true');
    await user.click(busyButton);
    expect(within(doing).queryByRole('menu')).not.toBeInTheDocument();
    await waitFor(() => {
      expect([...heldFirst.bodies, ...heldSecond.bodies]).toHaveLength(2);
    });

    // Act：各自回應
    heldSecond.release();
    heldFirst.release();

    // Assert
    expect(await within(column(board, '待辦')).findByRole('article', { name: '第二張' })).toBeInTheDocument();
    await waitFor(() => {
      expect(within(doing).getByRole('article', { name: '第一張' })).not.toHaveAttribute('aria-busy');
    });
    expect(within(board).getByRole('alert')).toHaveTextContent('SMK-2「第二張」沒有移到「完成」');
  });
});
