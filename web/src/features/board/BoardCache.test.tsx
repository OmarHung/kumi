import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Item, ProjectDetail } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { agencyBlueprint } from '../../test/blueprints';
import { holdTransition, serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, servePatch, serveTransition } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

type User = ReturnType<typeof renderApp>['user'];

async function openBoard(path: string) {
  const view = renderApp(path);
  const board = await screen.findByRole('region', { name: '看板' });
  return { ...view, board };
}

function column(board: HTMLElement, name: string): HTMLElement {
  return within(board).getByRole('region', { name });
}

function cardKeys(lane: HTMLElement): (string | null)[] {
  return within(lane)
    .queryAllByRole('article')
    .map((card) => card.getAttribute('data-card'));
}

async function moveVia(user: User, board: HTMLElement, item: Item, to: string) {
  const card = await within(board).findByRole('article', { name: item.title });
  await user.click(within(card).getByRole('button', { name: `${item.key} 移到…` }));
  await user.click(within(within(card).getByRole('menu')).getByRole('menuitem', { name: to }));
}

function inDoing(item: Item): Item {
  return { ...item, state: 'doing', stateCategory: 'in_progress' };
}

function numbered(number: number): Item {
  return smkItem({ number, title: `項目${String(number)}` });
}

describe('移動之後各欄和伺服器一致', () => {
  it('從還有下一頁的欄移走一張：那一欄重抓，「載入更多」不會漏掉原本的下一張', async () => {
    // Arrange：「待辦」一頁兩張，有四張
    const first = numbered(1);
    const rest = [2, 3, 4].map(numbered);
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const boardServer = serveItemsByQuery('SMK', [first, ...rest], { pageSize: 2 });
    const { board, user } = await openBoard('/projects/SMK?view=board');
    const todo = column(board, '待辦');
    await within(todo).findByText('項目2');
    serveTransition(first, detailOf(inDoing(first)));
    boardServer.set([inDoing(first), ...rest]);

    // Act
    await moveVia(user, board, first, '進行中');
    await within(board).findByText('已把 SMK-1 移到「進行中」。');
    await within(todo).findByText('項目3');
    await user.click(within(todo).getByRole('button', { name: /載入更多/ }));

    // Assert
    await within(todo).findByText('項目4');
    expect(cardKeys(todo)).toEqual(['SMK-2', 'SMK-3', 'SMK-4']);
    expect(cardKeys(column(board, '進行中'))).toEqual(['SMK-1']);
  });

  it('負責人選「我」：別人的項目移到別欄後切回「我」，不會出現在「我」的欄，那一欄照伺服器重抓', async () => {
    // Arrange
    const mine = smkItem({ number: 1, title: '我的' });
    const others = smkItem({ number: 2, title: '別人的', assignee: null });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const boardServer = serveItemsByQuery('SMK', [mine, others]);
    const { board, user } = await openBoard('/projects/SMK?view=board&assignee=me');
    await within(board).findByText('我的');
    await chooseOption(user, screen.getByRole('combobox', { name: '負責人' }), '所有人');
    await within(board).findByText('別人的');
    serveTransition(others, detailOf(inDoing(others)));
    boardServer.set([mine, inDoing(others)]);
    await moveVia(user, board, others, '進行中');
    await within(board).findByText('已把 SMK-2 移到「進行中」。');
    const doingRequests = boardServer.requestsFor('doing').length;

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '負責人' }), '我');

    // Assert
    await waitFor(() => {
      expect(boardServer.requestsFor('doing').length).toBeGreaterThan(doingRequests);
    });
    expect(within(column(board, '進行中')).queryByText('別人的')).not.toBeInTheDocument();
    expect(within(column(board, '待辦')).getByText('我的')).toBeInTheDocument();
  });

  it('在表格只改了估計：看板已載入的那一欄不會多出它（狀態沒變，只在原位換掉）', async () => {
    // Arrange：「待辦」只載入 #1、#2，還有下一頁
    const edited = numbered(9);
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [...[1, 2, 3].map(numbered), edited], { pageSize: 2 });
    const { board, user, router } = await openBoard('/projects/SMK?view=board');
    await within(board).findByText('項目2');
    await router.navigate('/projects/SMK?q=項目9');
    const table = await screen.findByRole('table', { name: '項目' });
    servePatch(edited);

    // Act
    await user.click(await within(table).findByRole('button', { name: '估計：未填' }));
    await user.type(within(table).getByRole('textbox', { name: '估計' }), '5{Enter}');
    await within(table).findByRole('button', { name: '估計：5' });
    await router.navigate('/projects/SMK?view=board');

    // Assert
    const todo = column(await screen.findByRole('region', { name: '看板' }), '待辦');
    await within(todo).findByText('項目1');
    expect(cardKeys(todo)).toEqual(['SMK-1', 'SMK-2']);
  });

  it('送出中背景重抓帶回舊的位置，也只有一張；成功後留在新的欄', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [item]);
    const held = holdTransition(item, () => ok(detailOf(inDoing(item))));
    const { board, user, queryClient } = await openBoard('/projects/SMK?view=board');
    await moveVia(user, board, item, '進行中');
    await waitFor(() => {
      expect(held.bodies).toHaveLength(1);
    });

    // Act
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK', 'items'] });

    // Assert
    expect(within(board).getAllByRole('article', { name: '首頁設計稿' })).toHaveLength(1);
    expect(cardKeys(column(board, '進行中'))).toEqual(['SMK-1']);
    held.release();
    await within(board).findByText('已把 SMK-1 移到「進行中」。');
    expect(within(board).getAllByRole('article', { name: '首頁設計稿' })).toHaveLength(1);
    expect(cardKeys(column(board, '進行中'))).toEqual(['SMK-1']);
  });
});

describe('送出中換條件', () => {
  it('送出中切換負責人：送出中的卡片要符合新的條件才畫在欄裡', async () => {
    // Arrange
    const mine = smkItem({ number: 1, title: '我的' });
    const others = smkItem({ number: 2, title: '別人的', assignee: null });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [mine, others]);
    const heldMine = holdTransition(mine, () => ok(detailOf(inDoing(mine))));
    const heldOthers = holdTransition(others, () => ok(detailOf(inDoing(others))));
    const { board, user } = await openBoard('/projects/SMK?view=board');
    await moveVia(user, board, mine, '進行中');
    await moveVia(user, board, others, '進行中');
    await waitFor(() => {
      expect([...heldMine.bodies, ...heldOthers.bodies]).toHaveLength(2);
    });

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '負責人' }), '我');

    // Assert
    const doing = column(board, '進行中');
    await waitFor(() => {
      expect(cardKeys(doing)).toEqual(['SMK-1']);
    });
    expect(within(board).queryByText('別人的')).not.toBeInTheDocument();
    heldMine.release();
    heldOthers.release();
    await within(board).findByText(/^已把 SMK-/);
  });

  it('送出中切換流程：另一個流程有同名的狀態，送出中的卡片也不會畫進那一欄', async () => {
    // Arrange：交付流程多一個和任務流程同名的「doing」
    const workflows = agencyBlueprint.workflows.map((workflow) =>
      workflow.key === 'delivery'
        ? { ...workflow, states: [...workflow.states, { key: 'doing', name: '處理中', category: 'in_progress' as const }] }
        : workflow,
    );
    const shared: ProjectDetail = agyDetail({ blueprint: { ...agencyBlueprint, workflows } });
    const task = agyItem({ number: 5, title: '切版', type: 'task', state: 'todo', stateCategory: 'not_started' });
    signedInAs(exampleOwner, { details: [shared] });
    serveItemsByQuery('AGY', [task]);
    const held = holdTransition(task, () => ok(detailOf(inDoing(task))));
    const { board, user } = await openBoard('/projects/AGY?view=board&workflow=task_flow');
    await moveVia(user, board, task, '進行中');
    await waitFor(() => {
      expect(held.bodies).toHaveLength(1);
    });

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '流程' }), '交付流程');

    // Assert
    const handling = column(board, '處理中');
    await within(handling).findByText('這一欄沒有項目');
    expect(cardKeys(handling)).toEqual([]);
    held.release();
    await within(board).findByText('已把 AGY-5 移到「進行中」。');
  });

  it('送出中切換流程，被擋下來的說明切回來還在', async () => {
    // Arrange
    const task = agyItem({ number: 5, title: '切版', type: 'task', state: 'todo', stateCategory: 'not_started' });
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [task]);
    const held = holdTransition(task, () => failure(422, 'item.transition_not_allowed', '現在不能改成「進行中」。'));
    const { board, user } = await openBoard('/projects/AGY?view=board&workflow=task_flow');
    await moveVia(user, board, task, '進行中');
    await waitFor(() => {
      expect(held.bodies).toHaveLength(1);
    });
    const workflowField = screen.getByRole('combobox', { name: '流程' });
    await chooseOption(user, workflowField, '交付流程');
    await within(board).findByRole('heading', { name: '需求確認' });

    // Act
    held.release();
    await waitFor(() => {
      expect(within(board).queryByText(/正在把/)).not.toBeInTheDocument();
    });
    await chooseOption(user, workflowField, '任務流程');

    // Assert
    const alert = await within(board).findByRole('alert');
    expect(alert).toHaveTextContent('AGY-5「切版」沒有移到「進行中」，留在「待辦」。');
    expect(alert).toHaveTextContent('現在不能改成「進行中」。');
    expect(within(column(board, '待辦')).getByRole('article', { name: '切版' })).toBeInTheDocument();
  });
});

describe('表格的清單', () => {
  it('篩選狀態時改了狀態、還有下一頁：清單重抓，「載入更多」不會漏掉', async () => {
    // Arrange：只看「待辦」，一頁兩個，有三個
    const first = numbered(1);
    const rest = [2, 3].map(numbered);
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const listServer = serveItemsByQuery('SMK', [first, ...rest], { pageSize: 2 });
    const { user } = renderApp('/projects/SMK?state=todo');
    const table = await screen.findByRole('table', { name: '項目' });
    const row = await within(table).findByRole('row', { name: /SMK-1/ });
    serveTransition(first, detailOf(inDoing(first)));
    listServer.set([inDoing(first), ...rest]);

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：待辦' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    expect(await within(table).findByRole('row', { name: /SMK-3/ })).toBeInTheDocument();
    expect(within(table).queryByRole('row', { name: /SMK-1/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
  });
});
