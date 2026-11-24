import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Member, Person, ProjectDetail, SessionUser } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agyDetail, agyProject, exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { serveMembers } from '../../test/members';
import { renderApp } from '../../test/renderApp';

/*
 * 成員對話框的表格只有一個 Tab 停駐點（docs/DESIGN.md 8.3「清單的鍵盤操作」，lib/useRovingGrid.ts）：
 * 方向鍵在格子之間移動。角色那一格是下拉（選了就存）：關著時 ↑／↓ 是打開清單（不換值），開著時方向鍵都是清單的，
 * 表格不跟著動；要上下移動先用 ← 回到名稱那一格。F2 不按「移除」（它不是編輯）。
 */

const designer: Member = { personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例設計師', role: 'project_manager' };

const people: Person[] = [
  { ...ownerAsAdmin, email: exampleOwner.email, role: 'owner', disabled: false },
  { ...memberAsEditor, email: exampleMember.email, role: 'member', disabled: false },
  { ...designer, email: 'designer@example.com', role: 'member', disabled: false },
];

const threeMembers = () => agyDetail({ members: [ownerAsAdmin, memberAsEditor, designer] });

async function openTable(detail: ProjectDetail = threeMembers(), viewer: SessionUser = exampleOwner) {
  signedInAs(viewer, { details: [detail] });
  serveItems('AGY', []);
  const backend = serveMembers(detail, { people });
  const view = renderApp('/projects/AGY');
  await view.user.click(await screen.findByRole('button', { name: '成員' }));
  const dialog = await screen.findByRole('dialog', { name: '成員' });
  const table = await within(dialog).findByRole('table', { name: '成員' });
  return { ...view, dialog, table, backend };
}

function nameCell(table: HTMLElement, name: string): HTMLElement {
  return within(table).getByRole('rowheader', { name: new RegExp(`^${name}`) });
}

function roleOf(table: HTMLElement, name: string): HTMLElement {
  return within(table).getByRole('combobox', { name: `${name}的角色` });
}

/** 表格裡 tabIndex 是 0 的元素。 */
function tabStopsIn(table: HTMLElement): HTMLElement[] {
  const grid = within(table);
  return [
    ...grid.getAllByRole('button'),
    ...grid.queryAllByRole('combobox'),
    ...grid.getAllByRole('rowheader'),
    ...grid.queryAllByRole('cell'),
    ...grid.getAllByRole('columnheader'),
  ].filter((element) => element.tabIndex === 0);
}

describe('成員表格：一個 Tab 停駐點', () => {
  it('同一時間只有一格的 tabIndex 是 0（第一位的名稱）；再按 Tab 就離開表格，Shift+Tab 回到同一格；捲動的區塊不是停駐點', async () => {
    // Arrange
    const { user, dialog, table } = await openTable();
    await waitFor(() => {
      expect(tabStopsIn(table)).toEqual([nameCell(table, '範例使用者')]);
    });
    act(() => {
      nameCell(table, '範例使用者').focus();
    });

    // Act
    await user.tab();
    const left = document.activeElement;
    await user.tab({ shift: true });

    // Assert
    expect(left).toBe(within(dialog).getByRole('combobox', { name: '人員' }));
    expect(nameCell(table, '範例使用者')).toHaveFocus();
    expect(within(dialog).getByRole('region', { name: '成員' })).not.toHaveAttribute('tabindex');
  });

  it('表格說怎麼操作：能管理的人的說明提到編輯；對話框打開時焦點在表格的停駐點（對話框裡第一個停得到的）', async () => {
    // Arrange / Act
    const { table } = await openTable();

    // Assert
    expect(table).toHaveAccessibleDescription('用方向鍵在格子之間移動，Enter 或 F2 編輯，Tab 離開表格。');
    await waitFor(() => {
      expect(nameCell(table, '範例使用者')).toHaveFocus();
    });
  });

  it('只能看的人：說明不提編輯，角色是文字的格子自己停得到', async () => {
    // Arrange
    const detail = agyDetail({ myPermissions: ['item.read'], project: { ...agyProject, myRole: 'viewer' }, members: [ownerAsAdmin, memberAsEditor] });
    const { user, table } = await openTable(detail, exampleMember);
    act(() => {
      nameCell(table, '範例使用者').focus();
    });

    // Act
    await user.keyboard('{ArrowRight}');

    // Assert
    expect(table).toHaveAccessibleDescription('用方向鍵在格子之間移動，Tab 離開表格。');
    expect(document.activeElement).toHaveRole('cell');
    expect(document.activeElement).toHaveTextContent('管理員');
  });

  it('方向鍵在格子之間移動：下一位的名稱、右邊的角色與「移除」，Home 回到名稱；停駐點跟著焦點走', async () => {
    // Arrange
    const { user, table } = await openTable();
    act(() => {
      nameCell(table, '範例使用者').focus();
    });

    // Act
    await user.keyboard('{ArrowDown}');
    const below = document.activeElement;
    await user.keyboard('{ArrowRight}');
    const role = document.activeElement;
    await user.keyboard('{ArrowRight}');
    const remove = document.activeElement;
    await user.keyboard('{Home}');

    // Assert
    expect(below).toBe(nameCell(table, '範例成員'));
    expect(role).toBe(roleOf(table, '範例成員'));
    expect(remove).toBe(within(table).getByRole('button', { name: '移除範例成員' }));
    expect(nameCell(table, '範例成員')).toHaveFocus();
    expect(tabStopsIn(table)).toEqual([nameCell(table, '範例成員')]);
  });
});

describe('成員表格：角色那一格是下拉', () => {
  it('關著時 ↓ 打開清單、不換角色，焦點留在下拉；開著時方向鍵都是清單的，表格不動；Esc 關上之後 ← 回到名稱', async () => {
    // Arrange
    const { user, dialog, table, backend } = await openTable();
    const select = roleOf(table, '範例成員');
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('{ArrowDown}');
    const listbox = await within(dialog).findByRole('listbox', { name: '範例成員的角色' });
    const activeBefore = select.getAttribute('aria-activedescendant');
    await user.keyboard('{ArrowDown}{ArrowLeft}{ArrowRight}');

    // Assert
    expect(listbox).toBeInTheDocument();
    expect(select).toHaveFocus();
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(select.getAttribute('aria-activedescendant')).not.toBe(activeBefore);
    expect(select).toHaveTextContent('編輯者');

    // Act
    await user.keyboard('{Escape}');
    await user.keyboard('{ArrowLeft}');

    // Assert
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
    expect(nameCell(table, '範例成員')).toHaveFocus();
    expect(backend.puts).toHaveLength(0);
  });

  it('F2 在角色上打開清單；在「移除」上不做事（不是編輯），Enter 才是按下它', async () => {
    // Arrange
    const { user, dialog, table } = await openTable();
    const select = roleOf(table, '範例成員');
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('{F2}');
    await within(dialog).findByRole('listbox', { name: '範例成員的角色' });
    await user.keyboard('{Escape}{ArrowRight}{F2}');

    // Assert
    const remove = within(table).getByRole('button', { name: '移除範例成員' });
    expect(remove).toHaveFocus();
    expect(screen.queryByRole('dialog', { name: '把「範例成員」移出專案？' })).not.toBeInTheDocument();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('dialog', { name: '把「範例成員」移出專案？' })).toBeInTheDocument();
  });
});
