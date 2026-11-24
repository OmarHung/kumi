import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

const overdue = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });
const overdueToo = smkItem({ number: 3, title: '上線檢查', dueDate: daysFromToday(-1) });
const undated = smkItem({ number: 2, title: '整理素材' });

async function openMyWork(details = [smkDetail()]) {
  signedInAs(exampleOwner, { details, myWork: [overdue, overdueToo, undated] });
  const view = renderApp('/my-work');
  const region = await screen.findByRole('region', { name: '我的工作' });
  // 狀態格要等專案內容讀到才換成可以改的按鈕。
  await within(region).findAllByRole('button', { name: '狀態：待辦' });
  return { ...view, region };
}

/** 所有組的表格裡 tabIndex 是 0 的元素。 */
function tabStopsIn(region: HTMLElement): HTMLElement[] {
  const grid = within(region);
  return [
    ...grid.getAllByRole('link'),
    ...grid.getAllByRole('button'),
    ...grid.getAllByRole('cell'),
    ...grid.getAllByRole('columnheader'),
  ].filter((element) => element.tabIndex === 0);
}

function keyCell(tableName: string, itemKey: string): HTMLElement {
  return within(screen.getByRole('table', { name: tableName })).getByRole('cell', { name: itemKey });
}

describe('我的工作：所有組的表格只有一個 Tab 停駐點（docs/DESIGN.md 8.3「清單的鍵盤操作」）', () => {
  it('從工具列最後的「欄的順序」按 Tab 一次進到第一組的第一格，再一次就離開；捲動的區塊不是停駐點', async () => {
    // Arrange
    const { user, region } = await openMyWork();
    act(() => {
      screen.getByRole('button', { name: '欄的順序' }).focus();
    });

    // Act
    await user.tab();
    const entered = document.activeElement;
    await user.tab();

    // Assert
    expect(entered).toBe(keyCell('已過期', 'SMK-1'));
    expect(region).not.toContainElement(document.activeElement as HTMLElement | null);
    expect(region).not.toHaveAttribute('tabindex');
  });

  it('同一時間只有一格的 tabIndex 是 0；每一張表格都說怎麼操作', async () => {
    // Arrange / Act
    const { region } = await openMyWork();

    // Assert
    await waitFor(() => {
      expect(tabStopsIn(region)).toEqual([keyCell('已過期', 'SMK-1')]);
    });
    for (const name of ['已過期', '沒有到期日']) {
      expect(screen.getByRole('table', { name })).toHaveAccessibleDescription('用方向鍵在格子之間移動，Enter 或 F2 編輯，Tab 離開表格。在表頭按 Shift 加左右方向鍵調整欄寬，Shift 加 Enter 照內容調整。');
    }
  });

  it('方向鍵跨過組：走過下一組的表頭（排序按鈕）到它的第一列；右鍵經過專案到標題的連結，停駐點跟著焦點走', async () => {
    // Arrange
    const { user, region } = await openMyWork();
    act(() => {
      keyCell('已過期', 'SMK-3').focus();
    });

    // Act
    await user.keyboard('{ArrowDown}');
    const header = document.activeElement;
    await user.keyboard('{ArrowDown}');
    const nextGroup = document.activeElement;
    await user.keyboard('{ArrowRight}{ArrowRight}');

    // Assert
    expect(header).toHaveRole('button');
    expect(header).toHaveAccessibleName('編號');
    expect(header?.closest('th')).toHaveRole('columnheader');
    expect(nextGroup).toBe(keyCell('沒有到期日', 'SMK-2'));
    const title = within(screen.getByRole('table', { name: '沒有到期日' })).getByRole('link', { name: '整理素材' });
    expect(title).toHaveFocus();
    expect(tabStopsIn(region)).toEqual([title]);
  });

  it('狀態格照表格的做法：F2 進入編輯，Esc 回到那一格', async () => {
    // Arrange
    const { user } = await openMyWork();
    const state = within(screen.getByRole('row', { name: /SMK-1/ })).getByRole('button', { name: '狀態：待辦' });
    act(() => {
      state.focus();
    });

    // Act
    await user.keyboard('{F2}');
    const editor = await within(screen.getByRole('row', { name: /SMK-1/ })).findByRole('combobox', { name: '狀態' });
    await user.keyboard('{Escape}');

    // Assert
    expect(editor).not.toBeInTheDocument();
    const back = await within(screen.getByRole('row', { name: /SMK-1/ })).findByRole('button', { name: '狀態：待辦' });
    await waitFor(() => {
      expect(back).toHaveFocus();
    });
    expect(back.tabIndex).toBe(0);
  });

  it('沒有任何專案能改狀態時，說明不提編輯', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })], myWork: [overdue, undated] });

    // Act
    renderApp('/my-work');

    // Assert
    const table = await screen.findByRole('table', { name: '已過期' });
    await waitFor(() => {
      expect(table).toHaveAccessibleDescription('用方向鍵在格子之間移動，Tab 離開表格。在表頭按 Shift 加左右方向鍵調整欄寬，Shift 加 Enter 照內容調整。');
    });
  });
});
