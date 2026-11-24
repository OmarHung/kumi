import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item, NewItemRequest } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { serveGroupedItems } from '../../test/boardGroup';
import { agyDetail, agyItem, exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const owner = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const member = { personId: exampleMember.personId, displayName: exampleMember.displayName };
const nameOf = (personId: string) => (personId === exampleMember.personId ? exampleMember.displayName : exampleOwner.displayName);

const bugOne = smkItem({ number: 1, title: '修錯字', fields: { category: 'bug' }, assignee: owner });
const featureTwo = smkItem({ number: 2, title: '新首頁', fields: { category: 'feature' }, assignee: member, state: 'doing', stateCategory: 'in_progress' });
const plainThree = smkItem({ number: 3, title: '整理文件', assignee: null });
const doneFour = smkItem({ number: 4, title: '已完成的', state: 'done', stateCategory: 'done', fields: { category: 'bug' } });

async function openBoard(path: string) {
  const view = renderApp(path);
  const board = await screen.findByRole('region', { name: '看板' });
  return { ...view, board };
}

function lane(board: HTMLElement, name: string): HTMLElement {
  return within(board).getByRole('region', { name });
}

function cardKeys(element: HTMLElement): (string | null)[] {
  return within(element).queryAllByRole('article').map((card) => card.getAttribute('data-card'));
}

async function openMoveMenu(user: ReturnType<typeof renderApp>['user'], board: HTMLElement, item: Item) {
  const card = await within(board).findByRole('article', { name: item.title });
  await user.click(within(card).getByRole('button', { name: `${item.key} 移到…` }));
  return { card, menu: within(card).getByRole('menu', { name: `把 ${item.key} 移到` }) };
}

describe('工具列的「分組」', () => {
  it('選項是狀態、負責人與每一個單選欄位；選了寫進網址，分欄跟著換', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveGroupedItems('SMK', [bugOne, plainThree], nameOf);
    const { board, user } = await openBoard('/projects/SMK?view=board');
    const select = await screen.findByRole('combobox', { name: '分組' });

    // Act
    await user.click(select);
    const options = (await screen.findAllByRole('option')).map((option) => option.textContent);
    await user.keyboard('{Escape}');
    await chooseOption(user, select, '依「分類」分組');

    // Assert
    expect(options).toEqual(['依狀態分組', '依負責人分組', '依「分類」分組']);
    expect(await within(board).findByRole('region', { name: '沒有填' })).toBeInTheDocument();
    expect(within(board).queryByRole('region', { name: '待辦' })).not.toBeInTheDocument();
    expect(select).toHaveTextContent('依「分類」分組');
  });

  it('網址上的分組欄位已經不在藍圖裡：回到依狀態', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveGroupedItems('SMK', [bugOne], nameOf);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board&group=field.gone');

    // Assert
    expect(await within(board).findByRole('region', { name: '待辦' })).toBeInTheDocument();
    expect(await screen.findByRole('combobox', { name: '分組' })).toHaveTextContent('依狀態分組');
  });

  it('依負責人、欄位分組時流程的切換藏起來；回到依狀態又出現', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveGroupedItems('AGY', [], nameOf);
    const { user } = await openBoard('/projects/AGY?view=board');
    expect(await screen.findByRole('combobox', { name: '流程' })).toBeInTheDocument();

    // Act
    await chooseOption(user, await screen.findByRole('combobox', { name: '分組' }), '依負責人分組');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('combobox', { name: '流程' })).not.toBeInTheDocument();
    });
    await chooseOption(user, screen.getByRole('combobox', { name: '分組' }), '依狀態分組');
    expect(await screen.findByRole('combobox', { name: '流程' })).toBeInTheDocument();
  });
});

describe('依負責人分組', () => {
  it('一次查一份沒結束的清單（limit 200）；每位成員一欄加「沒有負責人」，卡片自己帶狀態', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const server = serveGroupedItems('SMK', [bugOne, featureTwo, plainThree, doneFour], nameOf);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board&group=assignee');
    await within(board).findByRole('article', { name: '修錯字' });

    // Assert
    expect(cardKeys(lane(board, exampleOwner.displayName))).toEqual(['SMK-1']);
    expect(cardKeys(lane(board, exampleMember.displayName))).toEqual(['SMK-2']);
    expect(cardKeys(lane(board, '沒有負責人'))).toEqual(['SMK-3']);
    expect(within(board).queryByText('已完成的')).not.toBeInTheDocument();
    expect(within(within(board).getByRole('article', { name: '新首頁' })).getByText('進行中')).toBeInTheDocument();
    expect(server.requests).toHaveLength(1);
    expect(server.requests[0]?.searchParams.getAll('stateCategory')).toEqual(['backlog', 'not_started', 'in_progress']);
    expect(server.requests[0]?.searchParams.get('limit')).toBe('200');
    expect(server.requests[0]?.searchParams.has('state')).toBe(false);
  });

  it('沒有項目的成員也有一欄（才拖得過去）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveGroupedItems('SMK', [bugOne], nameOf);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board&group=assignee');

    // Assert
    expect(await within(board).findByRole('region', { name: exampleMember.displayName })).toBeInTheDocument();
    expect(within(lane(board, exampleMember.displayName)).getByText('這一欄沒有項目')).toBeInTheDocument();
  });

  it('「移到…」換負責人：送 assigneeId，卡片換欄，說「已把…移到…」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const served = serveGroupedItems('SMK', [bugOne], nameOf);
    const { board, user } = await openBoard('/projects/SMK?view=board&group=assignee');
    const { menu } = await openMoveMenu(user, board, bugOne);

    // Assert：目前的負責人不在選單裡，最後是「沒有負責人」
    expect(within(menu).getAllByRole('menuitem').map((entry) => entry.textContent)).toEqual([exampleMember.displayName, '沒有負責人']);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: exampleMember.displayName }));

    // Assert
    await within(board).findByText(`已把 SMK-1 移到「${exampleMember.displayName}」。`);
    expect(served.patches).toEqual([{ key: 'SMK-1', body: { assigneeId: exampleMember.personId } }]);
    expect(cardKeys(lane(board, exampleMember.displayName))).toEqual(['SMK-1']);
    expect(cardKeys(lane(board, exampleOwner.displayName))).toEqual([]);
  });

  it('移到「沒有負責人」是把負責人清掉（null）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const served = serveGroupedItems('SMK', [bugOne], nameOf);
    const { board, user } = await openBoard('/projects/SMK?view=board&group=assignee');
    const { menu } = await openMoveMenu(user, board, bugOne);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '沒有負責人' }));

    // Assert
    await within(board).findByText('已把 SMK-1 移到「沒有負責人」。');
    expect(served.patches).toEqual([{ key: 'SMK-1', body: { assigneeId: null } }]);
  });

  it('在欄裡新增：新的項目預設帶那一欄的負責人；「沒有負責人」那一欄沒有新增', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveGroupedItems('SMK', [], nameOf);
    const bodies: NewItemRequest[] = [];
    server.use(
      http.post('/v1/projects/SMK/items', async ({ request }) => {
        const body = (await request.json()) as NewItemRequest;
        bodies.push(body);
        return ok({ ...smkItem({ number: 9, title: body.title, assignee: member }), description: null }, 201);
      }),
    );
    const { board, user } = await openBoard('/projects/SMK?view=board&group=assignee');
    const memberLane = await within(board).findByRole('region', { name: exampleMember.displayName });

    // Act
    await user.click(within(memberLane).getByRole('button', { name: '新增項目' }));
    await user.type(within(memberLane).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');

    // Assert
    expect(await within(memberLane).findByRole('article', { name: '新的任務' })).toBeInTheDocument();
    expect(bodies).toEqual([{ type: 'task', title: '新的任務', assigneeId: exampleMember.personId }]);
    expect(within(lane(board, '沒有負責人')).queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
  });

  it('改失敗：卡片回到原欄，上方說明原因，「知道了」之後說明不見', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveGroupedItems('SMK', [bugOne], nameOf, {
      failPatch: { 'SMK-1': () => failure(409, 'item.assignee_not_member', '這個人不是專案成員。') },
    });
    const { board, user } = await openBoard('/projects/SMK?view=board&group=assignee');
    const { menu } = await openMoveMenu(user, board, bugOne);

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: exampleMember.displayName }));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(`SMK-1「修錯字」沒有移到「${exampleMember.displayName}」，留在「${exampleOwner.displayName}」。`);
    expect(alert).toHaveTextContent('這個人不是專案成員。');
    expect(cardKeys(lane(board, exampleOwner.displayName))).toEqual(['SMK-1']);
    await user.click(within(alert).getByRole('button', { name: '知道了（SMK-1）' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('沒有修改項目的權限：沒有「移到…」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read', 'item.transition'] })] });
    serveGroupedItems('SMK', [bugOne], nameOf);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board&group=assignee');
    const card = await within(board).findByRole('article', { name: '修錯字' });

    // Assert
    expect(within(card).queryByRole('button', { name: 'SMK-1 移到…' })).not.toBeInTheDocument();
  });
});

describe('依單選欄位分組', () => {
  it('只列有項目的選項（照選項的順序），最後是「沒有填」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveGroupedItems('SMK', [bugOne, featureTwo, plainThree], nameOf);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board&group=field.category');
    await within(board).findByRole('article', { name: '修錯字' });

    // Assert
    expect(within(board).getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['新功能', '錯誤', '沒有填']);
    expect(cardKeys(lane(board, '錯誤'))).toEqual(['SMK-1']);
    expect(cardKeys(lane(board, '沒有填'))).toEqual(['SMK-3']);
  });

  it('「移到…」列出所有選項（沒有欄的也能選），送 fields；移到沒有填是 null', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const served = serveGroupedItems('SMK', [bugOne], nameOf);
    const { board, user } = await openBoard('/projects/SMK?view=board&group=field.category');
    const first = await openMoveMenu(user, board, bugOne);
    expect(within(first.menu).getAllByRole('menuitem').map((entry) => entry.textContent)).toEqual(['新功能', '雜務', '沒有填']);

    // Act
    await user.click(within(first.menu).getByRole('menuitem', { name: '雜務' }));
    await within(board).findByText('已把 SMK-1 移到「雜務」。');
    const second = await openMoveMenu(user, board, bugOne);
    await user.click(within(second.menu).getByRole('menuitem', { name: '沒有填' }));

    // Assert
    await within(board).findByText('已把 SMK-1 移到「沒有填」。');
    expect(served.patches.map((patch) => patch.body)).toEqual([{ fields: { category: 'chore' } }, { fields: { category: null } }]);
  });

  it('類型沒有這個欄位的卡片：在「沒有填」，選單裡的選項停用並寫出原因', async () => {
    // Arrange：AGY 只有「需求變更單」有「影響程度」
    const requirement = agyItem({ number: 1, title: '官網需求', type: 'requirement', state: 'developing', stateCategory: 'in_progress' });
    const change = agyItem({ number: 2, title: '多一頁', type: 'change_request', fields: { impact: 'high' }, state: 'developing', stateCategory: 'in_progress' });
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveGroupedItems('AGY', [requirement, change], nameOf);
    const { board, user } = await openBoard('/projects/AGY?view=board&group=field.impact');
    await within(board).findByRole('article', { name: '官網需求' });

    // Act
    const { menu } = await openMoveMenu(user, board, requirement);

    // Assert
    expect(cardKeys(lane(board, '沒有填'))).toEqual(['AGY-1']);
    expect(cardKeys(lane(board, '高'))).toEqual(['AGY-2']);
    const low = within(menu).getByRole('menuitem', { name: '低' });
    expect(low).toHaveAttribute('aria-disabled', 'true');
    expect(low).toHaveAccessibleDescription('這個類型沒有「影響程度」');
  });

  it('在選項的欄裡新增：帶那一欄的值，類型只能是有這個欄位的（只有一種就不用選）', async () => {
    // Arrange
    const change = agyItem({ number: 2, title: '多一頁', type: 'change_request', fields: { impact: 'high' }, state: 'developing', stateCategory: 'in_progress' });
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveGroupedItems('AGY', [change], nameOf);
    const bodies: NewItemRequest[] = [];
    server.use(
      http.post('/v1/projects/AGY/items', async ({ request }) => {
        const body = (await request.json()) as NewItemRequest;
        bodies.push(body);
        return ok({ ...agyItem({ number: 5, title: body.title, type: 'change_request', fields: { impact: 'high' } }), description: null }, 201);
      }),
    );
    const { board, user } = await openBoard('/projects/AGY?view=board&group=field.impact');
    const high = await within(board).findByRole('region', { name: '高' });

    // Act
    await user.click(within(high).getByRole('button', { name: '新增項目' }));
    const form = within(high).getByRole('form', { name: '新增項目' });
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的變更{Enter}');

    // Assert
    expect(within(form).queryByRole('combobox', { name: '類型' })).not.toBeInTheDocument();
    await within(form).findByRole('status');
    expect(bodies).toEqual([{ type: 'change_request', title: '新的變更', fields: { impact: 'high' } }]);
  });
});

describe('一份清單的「載入更多」', () => {
  it('還有下一頁時最下面有「載入更多」，載完重新分欄', async () => {
    // Arrange
    const items = [1, 2, 3].map((number) => smkItem({ number, title: `項目${String(number)}`, assignee: owner }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const served = serveGroupedItems('SMK', items, nameOf, { pageSize: 2 });
    const { board, user } = await openBoard('/projects/SMK?view=board&group=assignee');
    await within(board).findByRole('article', { name: '項目2' });
    expect(within(board).queryByRole('article', { name: '項目3' })).not.toBeInTheDocument();
    expect(lane(board, exampleOwner.displayName)).toHaveTextContent('2+');

    // Act
    await user.click(await within(board).findByRole('button', { name: '載入更多' }));

    // Assert
    expect(await within(board).findByRole('article', { name: '項目3' })).toBeInTheDocument();
    expect(cardKeys(lane(board, exampleOwner.displayName))).toEqual(['SMK-1', 'SMK-2', 'SMK-3']);
    expect(within(board).queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    expect(served.requests).toHaveLength(2);
    expect(served.requests[1]?.searchParams.get('cursor')).toBe('page-2');
  });
});
