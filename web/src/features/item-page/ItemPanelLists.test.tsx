import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveDeleteItem, serveItem, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';

function serveDetails(items: readonly Item[]) {
  items.forEach((item) => {
    serveItem(detailOf(item, `${item.title}的說明`));
    serveActivity(item.key, [created()]);
  });
}

describe('項目面板：看板', () => {
  const first = smkItem({ number: 1, title: '首頁設計稿' });
  const doing = smkItem({ number: 2, title: '聯絡表單', state: 'doing', stateCategory: 'in_progress' });
  const third = smkItem({ number: 3, title: '上線檢查' });

  async function openBoard() {
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [first, doing, third]);
    serveDetails([first, doing, third]);
    const view = renderApp('/projects/SMK?view=board');
    const board = await screen.findByRole('region', { name: '看板' });
    const todo = await within(board).findByRole('region', { name: '待辦' });
    await within(todo).findByRole('article', { name: '上線檢查' });
    return { ...view, board, todo };
  }

  it('點卡片的標題開面板，那張卡片標成目前的；上一個／下一個在同一欄裡走', async () => {
    // Arrange
    const { user, router, todo } = await openBoard();
    const link = within(todo).getByRole('link', { name: '首頁設計稿' });

    // Act
    await user.click(link);
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    await user.click(within(panel).getByRole('button', { name: '下一個項目' }));

    // Assert
    expect(await within(panel).findByText('上線檢查的說明')).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SMK?view=board&item=SMK-3');
    expect(within(todo).getByRole('link', { name: '上線檢查' })).toHaveAttribute('aria-current', 'true');
    expect(link).not.toHaveAttribute('aria-current');
    // 同一欄的最後一張：不會跳到「進行中」那一欄。
    expect(within(panel).getByRole('button', { name: '下一個項目' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('Esc 一次就回到原本的看板：網址、條件照舊，焦點回到那張卡片', async () => {
    // Arrange
    const { user, router, todo } = await openBoard();
    await user.click(within(todo).getByRole('link', { name: '上線檢查' }));
    await within(await screen.findByRole('complementary', { name: '項目 SMK-3' })).findByText('上線檢查的說明');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(within(todo).getByRole('link', { name: '上線檢查' })).toHaveFocus();
    });
    expect(currentPath(router)).toBe('/projects/SMK?view=board');
    expect(screen.queryByRole('complementary', { name: /^項目 / })).not.toBeInTheDocument();
  });
});

describe('項目面板：我的工作', () => {
  const smkTask = smkItem({ number: 1, title: '首頁設計稿', dueDate: '2026-11-20' });
  const agyRequirement = agyItem({ number: 2, title: '官網需求', assignee: smkTask.assignee, dueDate: '2026-11-25' });

  it('跨專案：面板用那個項目自己的專案內容（類型、狀態的名稱），上一個／下一個照我的工作的順序', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()], myWork: [agyRequirement, smkTask] });
    serveDetails([smkTask, agyRequirement]);
    serveItems('AGY', []);
    const { user, router } = renderApp('/my-work');
    const link = await screen.findByRole('link', { name: '首頁設計稿' });

    // Act
    await user.click(link);
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    expect(await within(panel).findByRole('heading', { level: 2, name: '任務 SMK-1' })).toBeInTheDocument();
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');

    // Assert
    expect(await within(panel).findByRole('heading', { level: 2, name: '需求 AGY-2' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/my-work?item=AGY-2');
    const fields = await within(panel).findByRole('region', { name: '欄位' });
    expect(within(fields).getByRole('button', { name: '狀態：需求確認' })).toBeInTheDocument();
  });

  it('在面板裡刪除：留在我的工作，上方說明刪了什麼，焦點到頁面的標題', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [smkTask] });
    serveDetails([smkTask]);
    const { user, router } = renderApp('/my-work?item=SMK-1');
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    await user.click(await within(panel).findByRole('button', { name: 'SMK-1 的更多動作' }));
    serveDeleteItem(smkTask);

    // Act
    await user.click(screen.getByRole('menuitem', { name: '刪除' }));

    // Assert
    expect(await screen.findByText('已刪除 SMK-1「首頁設計稿」。')).toHaveAttribute('role', 'status');
    expect(currentPath(router)).toBe('/my-work');
    expect(screen.getByRole('heading', { level: 1, name: '我的工作' })).toHaveFocus();
  });
});

describe('項目面板：項目頁的下層項目', () => {
  const requirement = agyItem({ number: 1, title: '官網需求' });
  const childA = agyItem({ number: 5, type: 'task', title: '首頁切版', state: 'doing', stateCategory: 'in_progress', parentKey: 'AGY-1' });
  const childB = agyItem({ number: 6, type: 'task', title: '表單串接', state: 'todo', stateCategory: 'not_started', parentKey: 'AGY-1' });

  async function openRequirement() {
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveDetails([requirement, childA, childB]);
    serveItems('AGY', [childA, childB]);
    const view = renderApp('/projects/AGY/items/AGY-1');
    const list = await screen.findByRole('list', { name: '下層項目' });
    return { ...view, list };
  }

  it('下一個照下層清單的順序；面板裡點「開啟上層項目」換成上層，面板留著、焦點到面板的標題', async () => {
    // Arrange
    const { user, router, list } = await openRequirement();
    await user.click(within(list).getByRole('link', { name: /^AGY-5 首頁切版 / }));
    const panel = await screen.findByRole('complementary', { name: '項目 AGY-5' });
    await user.click(within(panel).getByRole('button', { name: '下一個項目' }));
    const fields = await within(panel).findByRole('region', { name: '欄位' });
    await within(panel).findByText('表單串接的說明');

    // Act
    await user.click(within(fields).getByRole('link', { name: '開啟上層項目 AGY-1' }));

    // Assert
    expect(await within(panel).findByText('官網需求的說明')).toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: '項目 AGY-1' })).toBe(panel);
    expect(within(panel).getByRole('heading', { level: 2, name: '需求 AGY-1' })).toHaveFocus();
    expect(currentPath(router)).toBe('/projects/AGY/items/AGY-1?item=AGY-1');
    // 面板裡換到的項目不在清單上：關上之後焦點回到清單上最後開的那一筆。
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(within(list).getByRole('link', { name: /^AGY-6 表單串接 / })).toHaveFocus();
    });
  });

  it('在下層的面板裡刪除：關上面板、留在上層的項目頁，上方說明刪了什麼', async () => {
    // Arrange
    const { user, router, list } = await openRequirement();
    await user.click(within(list).getByRole('link', { name: /^AGY-6 表單串接 / }));
    const panel = await screen.findByRole('complementary', { name: '項目 AGY-6' });
    await user.click(await within(panel).findByRole('button', { name: 'AGY-6 的更多動作' }));
    serveItems('AGY', [childA]);
    serveDeleteItem(childB);

    // Act
    await user.click(screen.getByRole('menuitem', { name: '刪除' }));

    // Assert
    expect(await screen.findByText('已刪除 AGY-6「表單串接」。')).toHaveAttribute('role', 'status');
    expect(currentPath(router)).toBe('/projects/AGY/items/AGY-1');
    await waitFor(() => {
      expect(within(list).queryByRole('link', { name: /^AGY-6 / })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('heading', { level: 1 })).toHaveFocus();
  });
});
