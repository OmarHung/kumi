import { screen, waitFor, within } from '@testing-library/react';
import { http, type JsonBodyType } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { detailOf } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const inClientQa = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress' });
const closed: Item = { ...inClientQa, state: 'closed', stateCategory: 'done', fields: { client_signoff_date: '2026-11-20' } };
const DIALOG = '移到「結案」之前要先填';
const SUBMIT = '填好並移到「結案」';

type Respond = () => Response;

const signoffRequired: Respond = () => {
  const details: JsonBodyType = [
    { code: 'item.transition_fields_required', path: 'client_signoff_date', message: '要先填「客戶驗收日」。' },
  ];
  return failure(422, 'item.transition_fields_required', '要先填「客戶驗收日」才能進入「結案」。', details);
};

/** 這張卡片的 PATCH 與改狀態照順序記下來；改狀態照 transitions 的順序回應，用完之後一直回最後一個。 */
function serveFill(item: Item, transitions: readonly Respond[]): string[] {
  const log: string[] = [];
  server.use(
    http.patch(`/v1/items/${item.key}`, async ({ request }) => {
      const body = (await request.json()) as { readonly fields?: Item['fields'] };
      log.push('PATCH');
      return ok(detailOf({ ...item, fields: { ...item.fields, ...body.fields } }));
    }),
    http.post(`/v1/items/${item.key}/transitions`, () => {
      log.push('POST');
      const index = log.filter((entry) => entry === 'POST').length - 1;
      const respond = transitions[Math.min(index, transitions.length - 1)] ?? (() => ok(detailOf(closed)));
      return respond();
    }),
  );
  return log;
}

/** 專案經理才能從「客戶驗收」移到「結案」：「移到…」選單裡這一項才不會停用。 */
async function openBoard() {
  const detail = agyDetail({ project: { ...agyDetail().project, myRole: 'project_manager' } });
  signedInAs(exampleOwner, { details: [detail] });
  const boardServer = serveItemsByQuery('AGY', [inClientQa]);
  const view = renderApp('/projects/AGY?view=board');
  const board = await screen.findByRole('region', { name: '看板' });
  return { ...view, board, boardServer };
}

function column(board: HTMLElement, name: string): HTMLElement {
  return within(board).getByRole('region', { name });
}

async function moveWithMenu(user: ReturnType<typeof renderApp>['user'], board: HTMLElement) {
  const card = await within(board).findByRole('article', { name: inClientQa.title });
  await user.click(within(card).getByRole('button', { name: 'AGY-4 移到…' }));
  await user.click(within(card).getByRole('menuitem', { name: '結案' }));
  return screen.findByRole('dialog', { name: DIALOG });
}

describe('補填再前進：看板', () => {
  it('被擋下時卡片回到原本的欄、開對話框；填好之後才移過去，焦點跟著卡片', async () => {
    // Arrange
    const { user, board, boardServer } = await openBoard();
    const log = serveFill(inClientQa, [signoffRequired, () => ok(detailOf(closed))]);

    // Act
    const dialog = await moveWithMenu(user, board);

    // Assert：卡片在原本的欄，看板上沒有另外的說明
    expect(within(column(board, '客戶驗收')).getByRole('article', { name: inClientQa.title })).toBeInTheDocument();
    expect(within(column(board, '結案')).queryByRole('article')).not.toBeInTheDocument();
    expect(within(board).queryByRole('alert')).not.toBeInTheDocument();

    // Act：填好送出
    boardServer.set([closed]);
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    expect(await within(board).findByText('已把 AGY-4 移到「結案」。')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const done = column(board, '結案');
    await waitFor(() => {
      expect(within(done).getByRole('button', { name: 'AGY-4 移到…' })).toHaveFocus();
    });
    expect(within(column(board, '客戶驗收')).queryByRole('article')).not.toBeInTheDocument();
    expect(log).toEqual(['POST', 'PATCH', 'POST']);
  });

  it('欄位存好了但改狀態又被擋下：說明留在對話框，卡片還在原本的欄，看板上沒有另一則說明', async () => {
    // Arrange
    const { user, board } = await openBoard();
    const notAllowed: Respond = () => failure(422, 'item.transition_not_allowed', '流程剛被改過，「客戶驗收」不能直接轉到「結案」。');
    const log = serveFill(inClientQa, [signoffRequired, notAllowed]);
    const dialog = await moveWithMenu(user, board);
    await user.type(within(dialog).getByLabelText('客戶驗收日'), '2026-11-20');

    // Act
    await user.click(within(dialog).getByRole('button', { name: SUBMIT }));

    // Assert
    const alert = await within(dialog).findByRole('alert');
    await waitFor(() => {
      expect(alert).toHaveTextContent('流程剛被改過，「客戶驗收」不能直接轉到「結案」。');
    });
    expect(alert).toHaveTextContent('欄位已經存好');
    expect(log).toEqual(['POST', 'PATCH', 'POST']);
    await waitFor(() => {
      expect(within(column(board, '客戶驗收')).getByRole('article', { name: inClientQa.title })).not.toHaveAttribute('aria-busy');
    });
    expect(within(board).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('取消：卡片留在原本的欄，焦點回到那張卡片的「移到…」', async () => {
    // Arrange
    const { user, board } = await openBoard();
    const log = serveFill(inClientQa, [signoffRequired]);
    const dialog = await moveWithMenu(user, board);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const qa = column(board, '客戶驗收');
    expect(within(qa).getByRole('button', { name: 'AGY-4 移到…' })).toHaveFocus();
    expect(log).toEqual(['POST']);
  });
});

describe('補填再前進：看板的佇列與退路', () => {
  const second = agyItem({ number: 5, title: '會員系統驗收', state: 'client_qa', stateCategory: 'in_progress' });

  /** 兩張卡片的改狀態都先不回應，再一起放行：兩個錯誤接連到達，第一個對話框還沒畫出來。 */
  function holdBoth(items: readonly Item[]): () => void {
    let open: () => void = () => undefined;
    const opened = new Promise<void>((resolve) => {
      open = resolve;
    });
    items.forEach((item) => {
      server.use(
        http.post(`/v1/items/${item.key}/transitions`, async () => {
          await opened;
          return signoffRequired();
        }),
      );
    });
    return open;
  }

  async function openBoardWith(items: readonly Item[], permissions?: readonly string[]) {
    const base = agyDetail({ project: { ...agyDetail().project, myRole: 'project_manager' } });
    const detail = permissions === undefined ? base : { ...base, myPermissions: permissions as typeof base.myPermissions };
    signedInAs(exampleOwner, { details: [detail] });
    serveItemsByQuery('AGY', items);
    const view = renderApp('/projects/AGY?view=board');
    const board = await screen.findByRole('region', { name: '看板' });
    return { ...view, board };
  }

  async function chooseClosed(user: ReturnType<typeof renderApp>['user'], board: HTMLElement, item: Item) {
    const card = await within(board).findByRole('article', { name: item.title });
    await user.click(within(card).getByRole('button', { name: `${item.key} 移到…` }));
    await user.click(within(card).getByRole('menuitem', { name: '結案' }));
  }

  it('兩張同時被擋下：第二張排隊，卡片留在原欄；第一個對話框關上後才開第二個', async () => {
    // Arrange
    const { user, board } = await openBoardWith([inClientQa, second]);
    const release = holdBoth([inClientQa, second]);
    await chooseClosed(user, board, inClientQa);
    await chooseClosed(user, board, second);

    // Act
    release();

    // Assert：先開第一張的，第二張回到原欄等著
    const first = await screen.findByRole('dialog', { name: DIALOG });
    expect(first).toHaveAccessibleDescription('填好之後接著把 AGY-4「驗收中的需求」移到「結案」。');
    await waitFor(() => {
      expect(within(column(board, '客戶驗收')).getByRole('article', { name: second.title })).not.toHaveAttribute('aria-busy');
    });
    expect(screen.getAllByRole('dialog')).toHaveLength(1);

    // Act：關上第一個
    await user.click(within(first).getByRole('button', { name: '取消' }));

    // Assert：接著開第二張的，關上之後焦點回到第二張卡片
    const next = await screen.findByRole('dialog', { name: DIALOG });
    expect(next).toHaveAccessibleDescription('填好之後接著把 AGY-5「會員系統驗收」移到「結案」。');
    await user.click(within(next).getByRole('button', { name: '取消' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(within(column(board, '客戶驗收')).getByRole('button', { name: 'AGY-5 移到…' })).toHaveFocus();
    expect(within(board).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('沒有修改項目的權限：不開對話框，看板上方照舊列出要先填的欄位，並說要找誰', async () => {
    // Arrange
    const { user, board } = await openBoardWith([inClientQa], ['item.read', 'item.transition']);
    serveFill(inClientQa, [signoffRequired]);

    // Act
    await chooseClosed(user, board, inClientQa);

    // Assert
    const alert = await within(board).findByRole('alert');
    expect(within(alert).getByText('要先填的欄位').nextElementSibling).toHaveTextContent('客戶驗收日');
    expect(alert).toHaveTextContent('請有權限修改項目的人先填好');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('被擋下時別的對話框開著：不在上面再開一個，看板上方照舊說明', async () => {
    // Arrange
    const { user, board } = await openBoardWith([inClientQa]);
    server.use(http.get('/v1/people', () => ok([])));
    const release = holdBoth([inClientQa]);
    await chooseClosed(user, board, inClientQa);
    await user.click(screen.getByRole('button', { name: '成員' }));
    await screen.findByRole('dialog', { name: '成員' });

    // Act
    release();

    // Assert
    const alert = await within(board).findByRole('alert');
    expect(within(alert).getByText('要先填的欄位').nextElementSibling).toHaveTextContent('客戶驗收日');
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
  });
});
