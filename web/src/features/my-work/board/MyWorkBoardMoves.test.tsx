import { act, screen, waitFor, within } from '@testing-library/react';
import { http, type JsonBodyType } from 'msw';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { failure, ok, signedInAs } from '../../../test/api';
import { simpleTasksBlueprint } from '../../../test/blueprints';
import { agyDetail, agyItem, agyProject, exampleOwner, smkDetail, smkItem } from '../../../test/fixtures';
import { detailOf } from '../../../test/items';
import { removeItemFromCache } from '../../item-table/itemCache';
import { server } from '../../../test/server';
import { dragCardToColumn, dragStart, serveMovable, stubBoardLayout } from './boardTestSupport';
import { renderMyWorkBoard } from './renderMyWorkBoard';

const mine = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const todo = smkItem({ number: 1, title: '首頁設計稿', assignee: mine });
const inQa = agyItem({ number: 3, title: '內部驗收的需求', state: 'internal_qa', stateCategory: 'in_progress', assignee: mine });
const clientQa = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress', assignee: mine });

/** 簡單任務的流程再加一個「等客戶」：進行中這個類別有兩個狀態。 */
const withWaiting = smkDetail({
  blueprint: {
    ...simpleTasksBlueprint,
    workflows: simpleTasksBlueprint.workflows.map((workflow) => ({
      ...workflow,
      states: [...workflow.states.slice(0, 2), { key: 'waiting', name: '等客戶', category: 'in_progress' as const }, ...workflow.states.slice(2)],
    })),
  },
});

/** 簡單任務的流程再加一個「不處理」（已取消）：預設的條件（未結束的狀態）看板沒有「已取消」那一欄。 */
const withCanceled = smkDetail({
  blueprint: {
    ...simpleTasksBlueprint,
    workflows: simpleTasksBlueprint.workflows.map((workflow) => ({
      ...workflow,
      states: [...workflow.states, { key: 'wontdo', name: '不處理', category: 'canceled' as const }],
    })),
  },
});

const asProjectManager = agyDetail({ project: { ...agyProject, myRole: 'project_manager' } });

/** dnd-kit 放下之後擋掉點擊的時間（50ms）再多一點。 */
const AFTER_DROP_MS = 100;

let restoreLayout: () => void = () => undefined;
beforeEach(() => {
  restoreLayout = stubBoardLayout();
});
afterEach(() => {
  restoreLayout();
});

function column(name: string): HTMLElement {
  return screen.getByRole('region', { name });
}

async function cardNamed(name: string) {
  const card = await screen.findByRole('article', { name });
  await waitFor(() => {
    expect(within(card).getByRole('button', { name: /移到…/ })).toBeInTheDocument();
  });
  return card;
}

describe('「移到…」選單', () => {
  it('列那張卡片自己專案的流程：走得到的在前、依類別分組；走不到的停用並寫出原因', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [inQa, todo] });
    const { user } = renderMyWorkBoard();
    const card = await cardNamed('內部驗收的需求');

    // Act
    await user.click(within(card).getByRole('button', { name: 'AGY-3 移到…' }));

    // Assert
    const menu = within(card).getByRole('menu', { name: '把 AGY-3 移到' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      '開發',
      '需求確認流程沒有這條路',
      '報價流程沒有這條路',
      '設計流程沒有這條路',
      '客戶驗收需要的角色：專案經理',
      '結案流程沒有這條路',
    ]);
    expect(within(menu).getByRole('menuitem', { name: '客戶驗收' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('只有鍵盤：Enter 打開、方向鍵選、Enter 移動，焦點跟著卡片到新的欄；Esc 取消回到按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], items: [todo] });
    const backend = serveMovable([todo]);
    const { user } = renderMyWorkBoard();
    const card = await cardNamed('首頁設計稿');
    within(card).getByRole('button', { name: 'SMK-1 移到…' }).focus();

    // Act：先 Esc 取消
    await user.keyboard('{Enter}');
    expect(within(card).getByRole('menuitem', { name: '進行中' })).toHaveFocus();
    await user.keyboard('{Escape}');
    expect(within(card).getByRole('button', { name: 'SMK-1 移到…' })).toHaveFocus();
    expect(backend.transitions).toEqual([]);
    await user.keyboard('{Enter}{ArrowDown}{Enter}');

    // Assert
    await waitFor(() => {
      expect(backend.transitions).toEqual([{ key: 'SMK-1', body: { to: 'done' } }]);
    });
    expect(await screen.findByText('已把 SMK-1 移到「完成」。')).toBeInTheDocument();
    const moved = within(column('完成')).getByRole('article', { name: '首頁設計稿' });
    await waitFor(() => {
      expect(within(moved).getByRole('button', { name: 'SMK-1 移到…' })).toHaveFocus();
    });
  });

  it('補填之後移動：缺的欄位開對話框（用那個項目自己專案的欄位），填好再前進，卡片移到新的欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [asProjectManager], items: [clientQa] });
    let posts = 0;
    const fieldsRequired: JsonBodyType = [{ code: 'item.transition_fields_required', path: 'client_signoff_date', message: '要先填「客戶驗收日」。' }];
    const patches: unknown[] = [];
    server.use(
      http.get('/v1/items', () => ok({ items: [clientQa], nextCursor: null })),
      http.patch('/v1/items/AGY-4', async ({ request }) => {
        patches.push(await request.json());
        return ok(detailOf({ ...clientQa, updatedAt: '2026-11-16T09:00:00Z' }));
      }),
      http.post('/v1/items/AGY-4/transitions', () => {
        posts += 1;
        return posts === 1
          ? failure(422, 'item.transition_fields_required', '要先填「客戶驗收日」才能進入「結案」。', fieldsRequired)
          : ok(detailOf({ ...clientQa, state: 'closed', stateCategory: 'done', updatedAt: '2026-11-16T09:01:00Z' }));
      }),
    );
    const { user } = renderMyWorkBoard();
    const card = await cardNamed('驗收中的需求');
    await user.click(within(card).getByRole('button', { name: 'AGY-4 移到…' }));
    await user.click(within(card).getByRole('menuitem', { name: '結案' }));
    const dialog = await screen.findByRole('dialog', { name: '移到「結案」之前要先填' });

    // Act
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: '填好並移到「結案」' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(patches).toHaveLength(1);
    expect(within(column('完成')).getByRole('article', { name: '驗收中的需求' })).toBeInTheDocument();
    expect(await screen.findByText('已把 AGY-4 移到「結案」。')).toBeInTheDocument();
    expect(within(column('進行中')).queryByRole('article')).not.toBeInTheDocument();
  });

  it('失敗：卡片回到原欄，原因在看板上方（角色不夠時說需要的角色）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], items: [todo] });
    server.use(
      http.get('/v1/items', () => ok({ items: [todo], nextCursor: null })),
      http.post('/v1/items/SMK-1/transitions', () => failure(403, 'item.transition_role_required', '你的角色不能做這個轉換。')),
    );
    const { user } = renderMyWorkBoard();
    const card = await cardNamed('首頁設計稿');

    // Act
    await user.click(within(card).getByRole('button', { name: 'SMK-1 移到…' }));
    await user.click(within(card).getByRole('menuitem', { name: '進行中' }));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('SMK-1「首頁設計稿」沒有移到「進行中」，留在「待辦」。')).toBeInTheDocument();
    expect(within(alert).getByText('你的角色不能做這個轉換。')).toBeInTheDocument();
    expect(within(column('未開始')).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
    expect(within(column('進行中')).queryByRole('article')).not.toBeInTheDocument();
    await user.click(within(alert).getByRole('button', { name: '知道了（SMK-1）' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});

describe('說明關上時卡片不在畫面上', () => {
  it('焦點照頁面給的順序放到搜尋框，不掉回頁面本體', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], items: [todo] });
    server.use(
      http.get('/v1/items', () => ok({ items: [todo], nextCursor: null })),
      http.post('/v1/items/SMK-1/transitions', () => failure(403, 'item.transition_role_required', '你的角色不能做這個轉換。')),
    );
    const { user, queryClient } = renderMyWorkBoard(undefined, { hasFallback: true });
    const card = await cardNamed('首頁設計稿');
    await user.click(within(card).getByRole('button', { name: 'SMK-1 移到…' }));
    await user.click(within(card).getByRole('menuitem', { name: '進行中' }));
    const alert = await screen.findByRole('alert');
    act(() => {
      removeItemFromCache(queryClient, 'SMK', 'SMK-1');
    });
    await waitFor(() => {
      expect(screen.queryByRole('article', { name: '首頁設計稿' })).not.toBeInTheDocument();
    });

    // Act
    await user.click(within(alert).getByRole('button', { name: '知道了（SMK-1）' }));

    // Assert
    expect(screen.getByRole('textbox', { name: '頁面的搜尋框' })).toHaveFocus();
  });
});

describe('拖到有好幾個走得到的狀態的欄', () => {
  it('在卡片旁開選單「移到哪一個狀態？」，鍵盤選一個就移到那個狀態', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [withWaiting], items: [todo] });
    const backend = serveMovable([todo]);
    const { user } = renderMyWorkBoard();
    const card = await cardNamed('首頁設計稿');

    // Act
    dragCardToColumn(card, 2);
    const menu = await within(card).findByRole('menu', { name: '移到哪一個狀態？' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['進行中', '等客戶']);
    expect(within(menu).getByRole('menuitem', { name: '進行中' })).toHaveFocus();
    expect(backend.transitions).toEqual([]);
    // 放下之後 dnd-kit 會擋掉 50ms 內的點擊（拖完不是點擊）：真人選選項比這慢。
    await new Promise((resolve) => setTimeout(resolve, AFTER_DROP_MS));
    await user.keyboard('{ArrowDown}{Enter}');

    // Assert
    await waitFor(() => {
      expect(backend.transitions).toEqual([{ key: 'SMK-1', body: { to: 'waiting' } }]);
    });
    expect(await screen.findByText('已把 SMK-1 移到「等客戶」。')).toBeInTheDocument();
    expect(within(column('進行中')).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
  });

  it('Esc 取消：卡片留在原欄，不送請求，焦點回到卡片的「移到…」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [withWaiting], items: [todo] });
    const backend = serveMovable([todo]);
    const { user } = renderMyWorkBoard();
    const card = await cardNamed('首頁設計稿');
    dragCardToColumn(card, 2);
    await within(card).findByRole('menu', { name: '移到哪一個狀態？' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(within(card).queryByRole('menu')).not.toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'SMK-1 移到…' })).toHaveFocus();
    expect(within(column('未開始')).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
    expect(backend.transitions).toEqual([]);
  });
});

describe('沒有權限', () => {
  it('沒有 item.transition 的專案：卡片沒有「移到…」、不能拖', async () => {
    // Arrange
    const readOnly = smkDetail({ myPermissions: ['item.read'] });
    signedInAs(exampleOwner, { details: [readOnly], items: [todo] });
    const backend = serveMovable([todo]);
    renderMyWorkBoard();
    const card = await screen.findByRole('article', { name: '首頁設計稿' });
    await waitFor(() => {
      expect(within(card).getByText('待辦')).toBeInTheDocument();
    });

    // Act
    dragCardToColumn(card, 2);

    // Assert
    expect(within(card).queryByRole('button', { name: /移到/ })).not.toBeInTheDocument();
    expect(card).not.toHaveAttribute('aria-describedby');
    expect(screen.queryByText(/開始拖曳/)).not.toBeInTheDocument();
    expect(backend.transitions).toEqual([]);
  });

  it('同一個看板裡，有權限的專案照樣能動', async () => {
    // Arrange
    const readOnly = smkDetail({ myPermissions: ['item.read'] });
    signedInAs(exampleOwner, { details: [readOnly, agyDetail()], items: [todo, inQa] });
    renderMyWorkBoard();

    // Act
    const agyCard = await cardNamed('內部驗收的需求');
    const smkCard = await screen.findByRole('article', { name: '首頁設計稿' });

    // Assert
    expect(within(agyCard).getByRole('button', { name: 'AGY-3 移到…' })).toBeInTheDocument();
    expect(within(smkCard).queryByRole('button', { name: /移到/ })).not.toBeInTheDocument();
    dragStart(smkCard);
  });
});

describe('移到這一頁沒有那一欄的類別', () => {
  it('預設的條件沒有「已取消」那一欄：移過去之後多出那一欄，卡片留在裡面，不會不見', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [withCanceled], items: [todo] });
    serveMovable([todo], { categories: { wontdo: 'canceled' } });
    const { user } = renderMyWorkBoard();
    const card = await cardNamed('首頁設計稿');
    expect(screen.queryByRole('region', { name: '已取消' })).not.toBeInTheDocument();

    // Act
    await user.click(within(card).getByRole('button', { name: 'SMK-1 移到…' }));
    await user.click(within(await within(card).findByRole('menu', { name: '把 SMK-1 移到' })).getByRole('menuitem', { name: '不處理' }));

    // Assert
    expect(await screen.findByText('已把 SMK-1 移到「不處理」。')).toBeInTheDocument();
    expect(within(column('已取消')).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['待規劃', '未開始', '進行中', '完成', '已取消']);
  });
});
