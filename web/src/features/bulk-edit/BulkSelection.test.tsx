import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item } from '../../api/types';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const ITEMS: readonly Item[] = [1, 2, 3, 4, 5].map((number) => smkItem({ number, title: `任務 ${String(number)}` }));

async function openTable(items: readonly Item[] = ITEMS, permissions?: Parameters<typeof smkDetail>[0]) {
  signedInAs(exampleOwner, { details: [smkDetail(permissions)] });
  serveItems('SMK', items);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  return { ...view, table };
}

function rowCheckbox(table: HTMLElement, key: string): HTMLElement {
  return within(table).getByRole('checkbox', { name: `選取 ${key}` });
}

function headerCheckbox(table: HTMLElement): HTMLElement {
  return within(table).getByRole('checkbox', { name: '選取全部已載入的項目' });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('表格的多選：勾選框欄', () => {
  it('沒有改、改狀態、刪除其中一種權限的人沒有勾選框欄', async () => {
    // Arrange / Act
    const { table } = await openTable(ITEMS, { myPermissions: ['item.read', 'item.create'] });

    // Assert
    expect(within(table).queryByRole('checkbox', { name: /^選取/ })).not.toBeInTheDocument();
  });

  it.each(['item.update', 'item.transition', 'item.delete'] as const)('只有 %s 的人也有勾選框欄', async (permission) => {
    // Arrange / Act
    const { table } = await openTable(ITEMS, { myPermissions: ['item.read', permission] });

    // Assert
    expect(headerCheckbox(table)).toBeInTheDocument();
    expect(rowCheckbox(table, 'SMK-1')).toBeInTheDocument();
  });

  it('勾一列：那一列標 data-selected（不是 aria-selected：表格不是 grid）、工具列換成批次動作列並說已選幾個', async () => {
    // Arrange
    const { user, table } = await openTable();
    expect(screen.queryByRole('toolbar', { name: '批次操作' })).not.toBeInTheDocument();

    // Act
    await user.click(rowCheckbox(table, 'SMK-2'));

    // Assert
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });
    expect(bar).toHaveTextContent('已選 1 個');
    expect(within(table).getByRole('row', { name: /SMK-2/ })).toHaveAttribute('data-selected', 'true');
    expect(within(table).getByRole('row', { name: /SMK-1/ })).not.toHaveAttribute('data-selected');
    expect(within(table).getByRole('row', { name: /SMK-2/ })).not.toHaveAttribute('aria-selected');
    expect(screen.queryByRole('group', { name: '篩選項目' })).not.toBeInTheDocument();
  });

  it('表頭的勾選框：部分選取是 indeterminate，全選之後全部選起來，再按一次全部取消', async () => {
    // Arrange
    const { user, table } = await openTable();

    // Act
    await user.click(rowCheckbox(table, 'SMK-1'));
    const partial = headerCheckbox(table);
    const wasIndeterminate = (partial as HTMLInputElement).indeterminate;
    await user.click(partial);

    // Assert
    expect(wasIndeterminate).toBe(true);
    expect(await screen.findByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 5 個');
    expect(headerCheckbox(table)).toBeChecked();
    expect((headerCheckbox(table) as HTMLInputElement).indeterminate).toBe(false);

    // Act
    await user.click(headerCheckbox(table));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('toolbar', { name: '批次操作' })).not.toBeInTheDocument();
    });
    expect(headerCheckbox(table)).not.toBeChecked();
  });

  it('Shift＋點：從上一次點的那一列到這一列整段選起來，再 Shift＋點取消整段', async () => {
    // Arrange
    const { user, table } = await openTable();
    await user.click(rowCheckbox(table, 'SMK-1'));

    // Act
    await user.keyboard('{Shift>}');
    await user.click(rowCheckbox(table, 'SMK-4'));
    await user.keyboard('{/Shift}');

    // Assert
    expect(await screen.findByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 4 個');
    expect(rowCheckbox(table, 'SMK-3')).toBeChecked();
    expect(rowCheckbox(table, 'SMK-5')).not.toBeChecked();

    // Act
    await user.keyboard('{Shift>}');
    await user.click(rowCheckbox(table, 'SMK-2'));
    await user.keyboard('{/Shift}');

    // Assert
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 1 個');
    expect(rowCheckbox(table, 'SMK-1')).toBeChecked();
    expect(rowCheckbox(table, 'SMK-2')).not.toBeChecked();
  });

  it('勾選框是表格鍵盤操作的一格：停駐點、方向鍵走到、空白鍵切換', async () => {
    // Arrange
    const { user, table } = await openTable();
    const first = rowCheckbox(table, 'SMK-1');
    act(() => {
      first.focus();
    });

    // Act
    await user.keyboard(' ');
    await user.keyboard('{ArrowDown}');
    await user.keyboard(' ');

    // Assert
    expect(rowCheckbox(table, 'SMK-2')).toHaveFocus();
    expect(rowCheckbox(table, 'SMK-1')).toBeChecked();
    expect(rowCheckbox(table, 'SMK-2')).toBeChecked();
    expect(await screen.findByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 2 個');
  });

  it('Esc 取消選取：工具列換回去，焦點回到表頭的勾選框', async () => {
    // Arrange
    const { user, table } = await openTable();
    await user.click(rowCheckbox(table, 'SMK-3'));
    await screen.findByRole('toolbar', { name: '批次操作' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('toolbar', { name: '批次操作' })).not.toBeInTheDocument();
    });
    expect(await screen.findByRole('group', { name: '篩選項目' })).toBeInTheDocument();
    expect(headerCheckbox(table)).toHaveFocus();
    expect(rowCheckbox(table, 'SMK-3')).not.toBeChecked();
  });

  it('動作列上的「取消選取」：同樣換回工具列、焦點回到表頭的勾選框', async () => {
    // Arrange
    const { user, table } = await openTable();
    await user.click(rowCheckbox(table, 'SMK-3'));
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });

    // Act
    await user.click(within(bar).getByRole('button', { name: '取消選取' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('toolbar', { name: '批次操作' })).not.toBeInTheDocument();
    });
    expect(headerCheckbox(table)).toHaveFocus();
  });

  it('動作列上開著下拉時，Esc 只關下拉、不取消選取', async () => {
    // Arrange
    const { user, table } = await openTable();
    await user.click(rowCheckbox(table, 'SMK-3'));
    const bar = await screen.findByRole('toolbar', { name: '批次操作' });
    await user.click(within(bar).getByRole('combobox', { name: '狀態' }));
    await screen.findByRole('listbox', { name: '狀態' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('listbox', { name: '狀態' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 1 個');
  });

  it('換條件就清空選取', async () => {
    // Arrange
    const { user, table, router } = await openTable();
    await user.click(rowCheckbox(table, 'SMK-3'));
    await screen.findByRole('toolbar', { name: '批次操作' });

    // Act
    await act(async () => {
      await router.navigate('/projects/SMK?q=任務');
    });

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('toolbar', { name: '批次操作' })).not.toBeInTheDocument();
    });
    expect(within(await screen.findByRole('table', { name: '項目' })).queryAllByRole('checkbox', { name: /^選取/, checked: true })).toHaveLength(0);
  });

  it('重抓之後不在清單裡的編號從選取拿掉', async () => {
    // Arrange
    const { user, table, queryClient } = await openTable();
    await user.click(rowCheckbox(table, 'SMK-1'));
    await user.click(rowCheckbox(table, 'SMK-2'));
    await screen.findByRole('toolbar', { name: '批次操作' });
    serveItems('SMK', ITEMS.filter((item) => item.key !== 'SMK-2'));

    // Act
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: ['project', 'SMK', 'items'] });
    });

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('toolbar', { name: '批次操作' })).toHaveTextContent('已選 1 個');
    });
    expect(within(table).queryByRole('checkbox', { name: '選取 SMK-2' })).not.toBeInTheDocument();
  });

  it('窄螢幕（清單）不提供多選', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', ITEMS);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByText('任務 1')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /^選取/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('toolbar', { name: '批次操作' })).not.toBeInTheDocument();
  });
});
