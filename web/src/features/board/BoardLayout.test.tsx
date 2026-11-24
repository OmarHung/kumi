import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { failTransition, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

/** 窄螢幕：讓 COMPACT_QUERY 符合（測試環境沒有 matchMedia，預設是寬螢幕）。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

async function openBoard(path: string) {
  const view = renderApp(path);
  const board = await screen.findByRole('region', { name: '看板' });
  return { ...view, board };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('看板的版面', () => {
  it('篩選在頁面的工具列上：和檢視的分頁同一列，不在欄的上方另外放一列', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);

    // Act
    const { board } = await openBoard('/projects/AGY?view=board');

    // Assert
    const views = screen.getByRole('navigation', { name: '版面' });
    const filters = screen.getByRole('group', { name: '篩選看板' });
    expect(within(filters).getByRole('combobox', { name: '流程' })).toBeInTheDocument();
    expect(within(filters).getByRole('searchbox', { name: '搜尋' })).toBeInTheDocument();
    expect(within(filters).getByRole('combobox', { name: '負責人' })).toBeInTheDocument();
    // 順序：檢視的分頁 → 篩選 → 第一欄（篩選緊接在分頁後面，在欄的上方，不在任何一欄裡）。
    const firstColumn = within(board).getByRole('region', { name: '需求確認' });
    expect(views.compareDocumentPosition(filters) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(filters.compareDocumentPosition(firstColumn) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(firstColumn).queryByRole('searchbox')).not.toBeInTheDocument();
  });

  it('從表格切到看板：工具列上的分頁是同一個元素，焦點留在「看板」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);
    const { user } = renderApp('/projects/SMK');
    await screen.findByRole('table', { name: '項目' });
    const views = screen.getByRole('navigation', { name: '版面' });
    serveItemsByQuery('SMK', []);

    // Act
    await user.click(within(views).getByRole('link', { name: '看板' }));

    // Assert
    const board = await screen.findByRole('region', { name: '看板' });
    expect(screen.getByRole('navigation', { name: '版面' })).toBe(views);
    // 「看板」區域只包看板的內容，工具列（檢視的分頁、篩選）在它外面，和表格的「項目」一樣。
    expect(within(board).queryByRole('navigation', { name: '版面' })).not.toBeInTheDocument();
    expect(within(views).getByRole('link', { name: '看板' })).toHaveFocus();
  });

  it('「清除條件」按了就不見：焦點到搜尋框，流程留著', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', []);
    const { user, router } = await openBoard('/projects/AGY?view=board&workflow=task_flow&assignee=me');

    // Act
    await user.click(screen.getByRole('button', { name: '清除條件' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/AGY?view=board&workflow=task_flow');
    expect(screen.queryByRole('button', { name: '清除條件' })).not.toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveFocus();
  });
});

describe('窄螢幕的看板', () => {
  it('篩選收進「篩選」：按鈕上是條件的數字（流程不算），裡面是流程、搜尋、負責人', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', []);
    const { user } = await openBoard('/projects/AGY?view=board&workflow=task_flow&assignee=me');
    const toggle = screen.getByRole('button', { name: '篩選' });
    expect(toggle).toHaveAccessibleDescription('目前有 1 個條件');
    expect(screen.queryByRole('searchbox', { name: '搜尋' })).not.toBeInTheDocument();

    // Act
    await user.click(toggle);

    // Assert
    const popover = screen.getByRole('dialog', { name: '篩選' });
    const filters = within(popover).getByRole('group', { name: '篩選看板' });
    expect(within(filters).getByRole('combobox', { name: '流程' })).toHaveTextContent('任務流程');
    expect(within(filters).getByRole('searchbox', { name: '搜尋' })).toBeInTheDocument();
    expect(within(filters).getByRole('combobox', { name: '負責人' })).toHaveTextContent('我');
  });

  it('「清除條件」之後焦點到「篩選」按鈕，數字的說明跟著不見', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', []);
    const { user, router } = await openBoard('/projects/SMK?view=board&q=首頁');
    const toggle = screen.getByRole('button', { name: '篩選' });
    await user.click(toggle);

    // Act
    await user.click(within(screen.getByRole('dialog', { name: '篩選' })).getByRole('button', { name: '清除條件' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK?view=board');
    expect(toggle).toHaveFocus();
    expect(toggle).not.toHaveAccessibleDescription();
  });
});

describe('被擋下來的說明關掉之後，焦點有去處', () => {
  const inQa = agyItem({ number: 3, title: '官網需求', state: 'internal_qa', stateCategory: 'in_progress' });
  const cut = agyItem({ number: 7, type: 'task', state: 'todo', title: '切版' });
  const write = agyItem({ number: 8, type: 'task', state: 'todo', title: '寫文件' });

  async function moveVia(user: ReturnType<typeof renderApp>['user'], board: HTMLElement, title: string, key: string, to: string) {
    const card = await within(board).findByRole('article', { name: title });
    await user.click(within(card).getByRole('button', { name: `${key} 移到…` }));
    await user.click(within(within(card).getByRole('menu')).getByRole('menuitem', { name: to }));
  }

  it('焦點在那則說明裡：交給那張卡片的「移到…」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [inQa]);
    failTransition(inQa, 422, 'item.transition_not_allowed', '「內部驗收」不能直接轉到「開發」。', []);
    const { board, user } = await openBoard('/projects/AGY?view=board');
    await moveVia(user, board, '官網需求', 'AGY-3', '開發');
    const alert = await within(board).findByRole('alert');

    // Act
    await user.click(within(alert).getByRole('button', { name: '知道了（AGY-3）' }));

    // Assert
    expect(within(board).queryByRole('alert')).not.toBeInTheDocument();
    const card = within(board).getByRole('article', { name: '官網需求' });
    expect(within(card).getByRole('button', { name: 'AGY-3 移到…' })).toHaveFocus();
  });

  it('那張卡片不在畫面上（換了流程）：交給下一則說明的「知道了」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [cut, write]);
    failTransition(cut, 422, 'item.transition_not_allowed', '不能這樣轉。', []);
    failTransition(write, 422, 'item.transition_not_allowed', '不能這樣轉。', []);
    const { board, user } = await openBoard('/projects/AGY?view=board&workflow=task_flow');
    await moveVia(user, board, '切版', 'AGY-7', '完成');
    await within(board).findByRole('alert');
    await moveVia(user, board, '寫文件', 'AGY-8', '完成');
    await waitFor(() => {
      expect(within(board).getAllByRole('alert')).toHaveLength(2);
    });
    await chooseOption(user, screen.getByRole('combobox', { name: '流程' }), '交付流程');
    await within(board).findByRole('heading', { name: '需求確認' });

    // Act
    await user.click(within(board).getByRole('button', { name: '知道了（AGY-7）' }));

    // Assert
    expect(within(board).getByRole('button', { name: '知道了（AGY-8）' })).toHaveFocus();
  });

  it('卡片不在畫面上、也沒有別的說明：交給工具列的搜尋框', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [cut]);
    failTransition(cut, 422, 'item.transition_not_allowed', '不能這樣轉。', []);
    const { board, user } = await openBoard('/projects/AGY?view=board&workflow=task_flow');
    await moveVia(user, board, '切版', 'AGY-7', '完成');
    await within(board).findByRole('alert');
    await chooseOption(user, screen.getByRole('combobox', { name: '流程' }), '交付流程');
    await within(board).findByRole('heading', { name: '需求確認' });

    // Act
    await user.click(within(board).getByRole('button', { name: '知道了（AGY-7）' }));

    // Assert
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveFocus();
  });
});

describe('還沒有流程的專案', () => {
  function withoutWorkflows(myPermissions = smkDetail().myPermissions) {
    const base = smkDetail();
    return smkDetail({ blueprint: { ...base.blueprint, itemTypes: [], workflows: [] }, myPermissions });
  }

  it('看得到設定的人：說明之後要做什麼，並給一顆到設定的連結', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [withoutWorkflows()] });

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    expect(within(board).getByText('這個專案還沒有流程')).toBeInTheDocument();
    expect(within(board).getByText('設定裡有項目類型與流程之後，這裡才會有欄。')).toBeInTheDocument();
    expect(within(board).getByRole('link', { name: '到設定加上流程' })).toHaveAttribute('href', '/projects/SMK/settings');
  });

  it('看不到設定的人：說要請有權限的人設定，沒有連結', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [withoutWorkflows(['item.read'])] });

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    expect(within(board).getByText('請有設定權限的人在設定裡加上項目類型與流程，這裡才會有欄。')).toBeInTheDocument();
    expect(within(board).queryByRole('link', { name: '到設定加上流程' })).not.toBeInTheDocument();
  });
});

describe('看板的欄與卡片', () => {
  it('第一次載入時每一欄是骨架（只給報讀軟體「載入中…」），還沒有卡片', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/projects/SMK/items', async () => {
        await held;
        return ok({ items: [smkItem({ number: 1 })], nextCursor: null });
      }),
    );

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    const todo = within(board).getByRole('region', { name: '待辦' });
    expect(within(todo).getByRole('status')).toHaveTextContent('載入中…');
    expect(within(todo).queryByRole('article')).not.toBeInTheDocument();
    release();
    expect(await within(todo).findByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
  });

  it('快速新增的按鈕在第一欄的標題列：說明開著沒有，再按一次收起來、焦點留在按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', []);
    const { board, user } = await openBoard('/projects/SMK?view=board');
    const todo = within(board).getByRole('region', { name: '待辦' });
    const toggle = within(todo).getByRole('button', { name: '新增項目' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    // Act
    await user.click(toggle);
    const form = within(todo).getByRole('form', { name: '新增項目' });
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(toggle).toHaveAttribute('aria-controls', form.id);
    await user.click(toggle);

    // Assert
    expect(within(todo).queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle).toHaveFocus();
  });

  it('卡片底下一列只放有值的：沒有優先度、到期日時只有負責人', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [
      smkItem({ number: 1, title: '有負責人', priority: 'none', dueDate: null }),
      smkItem({ number: 2, title: '沒有人', assignee: null, priority: 'urgent', dueDate: null }),
    ]);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    const assigned = await within(board).findByRole('article', { name: '有負責人' });
    expect(within(assigned).getAllByRole('term').map((term) => term.textContent)).toEqual(['負責人']);
    expect(within(assigned).getByRole('definition')).toHaveTextContent('範例使用者');
    const unassigned = within(board).getByRole('article', { name: '沒有人' });
    expect(within(unassigned).getAllByRole('term').map((term) => term.textContent)).toEqual(['優先度', '負責人']);
    expect(within(unassigned).getAllByRole('definition').at(-1)).toHaveTextContent('沒有負責人');
  });

  it('負責人：頭像旁看得到名字（觸控也看得到），報讀軟體只唸一次；滑過頭像的說明在不唸的頭像上', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [smkItem({ number: 1, title: '有負責人' })]);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    const card = await within(board).findByRole('article', { name: '有負責人' });
    const assignee = within(card).getAllByRole('definition').at(-1) as HTMLElement;
    expect(within(assignee).getByText('範例使用者')).not.toHaveAttribute('aria-hidden');
    expect(assignee).not.toHaveAttribute('title');
    expect(within(assignee).getByTitle('範例使用者')).toHaveAttribute('aria-hidden', 'true');
  });

  it('「移到…」是圖示按鈕：名稱照舊，滑鼠滑過看得到同一句說明', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [smkItem({ number: 1 })]);

    // Act
    const { board } = await openBoard('/projects/SMK?view=board');

    // Assert
    const card = await within(board).findByRole('article', { name: '首頁設計稿' });
    const button = within(card).getByRole('button', { name: 'SMK-1 移到…' });
    expect(button).toHaveAttribute('title', 'SMK-1 移到…');
    expect(button).toHaveAttribute('aria-haspopup', 'menu');
    await waitFor(() => {
      expect(within(card).queryByText('移到…')).not.toBeInTheDocument();
    });
  });
});
