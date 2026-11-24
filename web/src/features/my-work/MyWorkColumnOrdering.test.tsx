import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';

const overdue = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });
const undated = smkItem({ number: 2, title: '整理素材' });

const ORDER_KEY = 'my-work.column-order';
const COLUMN_WIDTH = 100;

async function openMyWork(path = '/my-work') {
  signedInAs(exampleOwner, { details: [smkDetail()], myWork: [overdue, undated] });
  const view = renderApp(path);
  const region = await screen.findByRole('region', { name: '我的工作' });
  await within(region).findAllByRole('button', { name: '狀態：待辦' });
  return { ...view, region };
}

/** 表頭的欄名；沒給名稱是第一組的表格。 */
function headerNames(tableName?: string): string[] {
  const table = tableName === undefined ? screen.getAllByRole('table')[0] : screen.getByRole('table', { name: tableName });
  if (table === undefined) throw new Error('找不到表格');
  return within(table)
    .getAllByRole('columnheader')
    .map((header) => header.textContent);
}

function sortButton(tableName: string, label: string): HTMLElement {
  const header = within(screen.getByRole('table', { name: tableName })).getByRole('columnheader', { name: label });
  return within(header).getByRole('button', { name: label });
}

/** jsdom 沒有版面：表頭每一格 100px 寬，照它在那一列的位置排（第 n 格從 n×100 開始）。 */
function stubHeaderLayout() {
  vi.spyOn(HTMLTableCellElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLTableCellElement) {
    return DOMRect.fromRect({ x: this.cellIndex * COLUMN_WIDTH, y: 0, width: COLUMN_WIDTH, height: 36 });
  });
}

function pointer(button: HTMLElement, type: 'pointerDown' | 'pointerMove' | 'pointerUp', clientX: number) {
  fireEvent[type](button, { pointerId: 1, button: 0, pointerType: 'mouse', clientX });
}

/** 按住欄名，拖到 x，放開；放開之後瀏覽器跟著送一下點擊。 */
function dragHeader(button: HTMLElement, from: number, to: number) {
  pointer(button, 'pointerDown', from);
  pointer(button, 'pointerMove', from + 10);
  pointer(button, 'pointerMove', to);
  pointer(button, 'pointerUp', to);
  fireEvent.click(button);
}

async function openPicker(user: ReturnType<typeof renderApp>['user']) {
  await user.click(screen.getByRole('button', { name: '欄的順序' }));
  return screen.findByRole('dialog', { name: '欄的順序' });
}

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe('我的工作：欄的順序（docs/DESIGN.md 8.3「我的工作」）', () => {
  it('預設是編號、專案、標題、狀態、優先度、到期日', async () => {
    // Arrange / Act
    await openMyWork();

    // Assert
    expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '狀態', '優先度', '到期日']);
  });

  it('依專案分組時沒有專案欄，其他欄照順序', async () => {
    // Arrange / Act
    await openMyWork('/my-work?group=project');

    // Assert
    expect(headerNames()).toEqual(['編號', '標題', '狀態', '優先度', '到期日']);
  });

  it('記住的順序打開就照它排；被改壞的照規則整理（狀態不能在標題左邊）', async () => {
    // Arrange
    localStorage.setItem(ORDER_KEY, JSON.stringify(['state', 'title', 'key', 'due', 'priority', 'project']));

    // Act
    await openMyWork();

    // Assert
    expect(headerNames('已過期')).toEqual(['標題', '狀態', '編號', '到期日', '優先度', '專案']);
  });

  describe('「欄的順序」', () => {
    it('往後移：所有組的表頭一起換、記在這個瀏覽器、焦點留在同一顆按鈕、唸出移到第幾欄', async () => {
      // Arrange
      const { user } = await openMyWork();
      const dialog = await openPicker(user);

      // Act
      await user.click(within(dialog).getByRole('button', { name: '把「專案」往後移' }));

      // Assert
      for (const name of ['已過期', '沒有到期日']) {
        expect(headerNames(name)).toEqual(['編號', '標題', '專案', '狀態', '優先度', '到期日']);
      }
      expect(JSON.parse(localStorage.getItem(ORDER_KEY) ?? 'null')).toEqual(['key', 'title', 'project', 'state', 'priority', 'due']);
      expect(within(dialog).getByRole('button', { name: '把「專案」往後移' })).toHaveFocus();
      expect(within(dialog).getByRole('status')).toHaveTextContent('「專案」移到第 3 欄。');
      expect(within(dialog).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['編號', '標題', '專案', '狀態', '優先度', '到期日']);
    });

    it('移不了的按鈕是 aria-disabled，按了沒有變化、唸出為什麼：第一欄不能往前、狀態不能越過標題', async () => {
      // Arrange
      const { user } = await openMyWork();
      const dialog = await openPicker(user);
      const firstUp = within(dialog).getByRole('button', { name: '把「編號」往前移' });
      const stateUp = within(dialog).getByRole('button', { name: '把「狀態」往前移' });

      // Act
      await user.click(stateUp);

      // Assert
      expect(firstUp).toHaveAttribute('aria-disabled', 'true');
      expect(stateUp).toHaveAttribute('aria-disabled', 'true');
      expect(within(dialog).getByRole('button', { name: '把「專案」往後移' })).not.toHaveAttribute('aria-disabled');
      expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '狀態', '優先度', '到期日']);
      expect(localStorage.getItem(ORDER_KEY)).toBeNull();
      expect(within(dialog).getByRole('status')).toHaveTextContent('「狀態」不能再往前移：標題和它左邊的欄固定在左邊，只放得下編號與專案。');

      // Act
      await user.click(firstUp);

      // Assert
      expect(within(dialog).getByRole('status')).toHaveTextContent('「編號」已經在最前面。');
    });

    it('「回到預設的順序」：表頭回到預設、清掉記住的，按鈕留著', async () => {
      // Arrange
      localStorage.setItem(ORDER_KEY, JSON.stringify(['key', 'title', 'state', 'priority', 'due', 'project']));
      const { user } = await openMyWork();
      const dialog = await openPicker(user);

      // Act
      await user.click(within(dialog).getByRole('button', { name: '回到預設的順序' }));

      // Assert
      expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '狀態', '優先度', '到期日']);
      expect(localStorage.getItem(ORDER_KEY)).toBeNull();
      expect(within(dialog).getByRole('button', { name: '回到預設的順序' })).toHaveFocus();
      expect(within(dialog).getByRole('status')).toHaveTextContent('已回到預設的順序。');
    });

    it('依專案分組時不列專案', async () => {
      // Arrange
      const { user } = await openMyWork('/my-work?group=project');

      // Act
      const dialog = await openPicker(user);

      // Assert
      expect(within(dialog).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['編號', '標題', '狀態', '優先度', '到期日']);
    });
  });

  describe('拖表頭', () => {
    it('把專案拖到最後：所有組一起換、記在這個瀏覽器；放開的那一下點擊不排序', async () => {
      // Arrange
      stubHeaderLayout();
      const { router } = await openMyWork();

      // Act：專案是第 2 格（100～200），拖到最右邊
      dragHeader(sortButton('已過期', '專案'), 150, 650);

      // Assert
      await waitFor(() => {
        expect(headerNames('沒有到期日')).toEqual(['編號', '標題', '狀態', '優先度', '到期日', '專案']);
      });
      expect(JSON.parse(localStorage.getItem(ORDER_KEY) ?? 'null')).toEqual(['key', 'title', 'state', 'priority', 'due', 'project']);
      expect(currentPath(router)).toBe('/my-work');
    });

    it('到期日拖到最前面：放不下的地方放在最近放得下的（標題右邊）', async () => {
      // Arrange
      stubHeaderLayout();
      await openMyWork();

      // Act：到期日是第 6 格（500～600）
      dragHeader(sortButton('已過期', '到期日'), 550, 5);

      // Assert
      await waitFor(() => {
        expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '到期日', '狀態', '優先度']);
      });
    });

    it('拖曳中看得到拖著哪一欄與放下去的位置', async () => {
      // Arrange
      stubHeaderLayout();
      await openMyWork();
      const button = sortButton('已過期', '專案');

      // Act
      pointer(button, 'pointerDown', 150);
      pointer(button, 'pointerMove', 650);

      // Assert：每一組的專案表頭都標著拖曳中
      await waitFor(() => {
        expect(sortButton('沒有到期日', '專案')).toHaveAttribute('data-dragging', 'true');
      });
      pointer(button, 'pointerUp', 650);
      expect(sortButton('沒有到期日', '專案')).not.toHaveAttribute('data-dragging');
    });

    it('拖曳中按 Esc：不換，放開的那一下點擊也不排序', async () => {
      // Arrange
      stubHeaderLayout();
      const { user, router } = await openMyWork();
      const button = sortButton('已過期', '專案');

      // Act
      pointer(button, 'pointerDown', 150);
      pointer(button, 'pointerMove', 650);
      await user.keyboard('{Escape}');
      pointer(button, 'pointerUp', 650);
      fireEvent.click(button);

      // Assert
      expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '狀態', '優先度', '到期日']);
      expect(localStorage.getItem(ORDER_KEY)).toBeNull();
      expect(currentPath(router)).toBe('/my-work');
    });

    it.each([
      ['瀏覽器取消（pointercancel）', 'pointerCancel'],
      ['沒放開就被收回指標（lostpointercapture）', 'lostPointerCapture'],
    ] as const)('%s：不換、不畫拖曳中的樣子', async (_, event) => {
      // Arrange
      stubHeaderLayout();
      await openMyWork();
      const button = sortButton('已過期', '專案');
      pointer(button, 'pointerDown', 150);
      pointer(button, 'pointerMove', 650);
      await waitFor(() => {
        expect(button).toHaveAttribute('data-dragging', 'true');
      });

      // Act
      fireEvent[event](button, { pointerId: 1 });

      // Assert
      await waitFor(() => {
        expect(button).not.toHaveAttribute('data-dragging');
      });
      pointer(button, 'pointerUp', 650);
      expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '狀態', '優先度', '到期日']);
      expect(localStorage.getItem(ORDER_KEY)).toBeNull();
    });

    it('上一次按下之後放開在別的地方（按鈕沒收到）：下一次拖的是新按下的那一欄', async () => {
      // Arrange：在狀態上按下，放開的事件沒有送到它（瀏覽器在按鈕外面放開）
      stubHeaderLayout();
      await openMyWork();
      pointer(sortButton('已過期', '狀態'), 'pointerDown', 350);

      // Act：在到期日上按下，拖到標題的右邊
      dragHeader(sortButton('已過期', '到期日'), 550, 300);

      // Assert
      await waitFor(() => {
        expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '到期日', '狀態', '優先度']);
      });
    });

    it('按下之後滑出欄名才放開（沒移動到算拖曳）：和一般的按鈕一樣不算點擊，不排序', async () => {
      // Arrange：欄名的按鈕在 100～140、上下 0～36
      stubHeaderLayout();
      vi.spyOn(HTMLButtonElement.prototype, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 100, y: 0, width: 40, height: 36 }));
      const { router } = await openMyWork();
      const button = sortButton('已過期', '專案');

      // Act
      pointer(button, 'pointerDown', 138);
      pointer(button, 'pointerUp', 142);
      fireEvent.click(button);

      // Assert
      expect(currentPath(router)).toBe('/my-work');
    });

    it('只移動一點點就放開是點擊：排序', async () => {
      // Arrange
      stubHeaderLayout();
      const { router } = await openMyWork();
      const button = sortButton('已過期', '專案');

      // Act
      pointer(button, 'pointerDown', 150);
      pointer(button, 'pointerMove', 152);
      pointer(button, 'pointerUp', 152);
      fireEvent.click(button);

      // Assert
      await waitFor(() => {
        expect(currentPath(router)).toBe('/my-work?sort=project%3Aasc');
      });
      expect(headerNames('已過期')).toEqual(['編號', '專案', '標題', '狀態', '優先度', '到期日']);
    });
  });
});
