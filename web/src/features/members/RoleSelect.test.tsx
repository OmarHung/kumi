import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Member, Person, ProjectDetail, SessionUser } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agyDetail, agyProject, exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { serveMembers } from '../../test/members';
import { renderApp } from '../../test/renderApp';
import { flushPending } from '../../test/flush';
import { chooseOption, openOptions } from '../../test/select';

/*
 * 成員的角色選單（RoleSelect，docs/DESIGN.md 7.3「馬上生效的下拉」）：選了就存，不分滑鼠或鍵盤；
 * 方向鍵、打字只移動反白，Esc 關上，都不送出。最後一位管理員的其他角色是不能選的選項，原因寫在選項裡。
 */

const designer: Member = { personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例設計師', role: 'project_manager' };

const people: Person[] = [
  { ...ownerAsAdmin, email: exampleOwner.email, role: 'owner', disabled: false },
  { ...memberAsEditor, email: exampleMember.email, role: 'member', disabled: false },
  { ...designer, email: 'designer@example.com', role: 'member', disabled: false },
];

const LAST_ADMIN_REASON = '最後一位管理員：要先把另一位成員設成管理員，才能換掉角色或移出專案。';
/** 選項裡的原因是短語；完整的句子只在表格下面那一段（選單用 aria-describedby 連過去）。 */
const LAST_ADMIN_SHORT = '最後一位管理員不能換角色';

async function openDialog(detail: ProjectDetail, viewer: SessionUser = exampleOwner) {
  signedInAs(viewer, { details: [detail] });
  serveItems('AGY', []);
  const backend = serveMembers(detail, {
    people,
    viewer: { personId: viewer.personId, isOrganizationAdmin: viewer.role === 'owner' || viewer.role === 'admin' },
  });
  const view = renderApp('/projects/AGY');
  await view.user.click(await screen.findByRole('button', { name: '成員' }));
  const dialog = await screen.findByRole('dialog', { name: '成員' });
  const table = await within(dialog).findByRole('table', { name: '成員' });
  return { ...view, dialog, table, backend };
}

function roleSelect(table: HTMLElement, name: string): HTMLElement {
  return within(table).getByRole('combobox', { name: `${name}的角色` });
}

const threeMembers = () => agyDetail({ members: [ownerAsAdmin, memberAsEditor, designer] });

describe('成員的角色選單：選了就存', () => {
  it('內建角色有「執行者」，排在編輯者後面；選了就存 contributor', async () => {
    // Arrange
    const { user, dialog, table, backend } = await openDialog(threeMembers());
    const select = roleSelect(table, '範例成員');

    // Act
    const options = await openOptions(user, select);
    const labels = options.map((option) => option.textContent);
    await chooseOption(user, select, '執行者');

    // Assert
    expect(labels.slice(0, 5)).toEqual(['管理員', '編輯者', '執行者', '留言者', '檢視者']);
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例成員」的角色改成「執行者」。');
    expect(backend.puts).toEqual([{ personId: exampleMember.personId, body: { role: 'contributor' } }]);
  });

  it('加入成員時，角色下面寫出選到的內建角色能做什麼：執行者只看得到自己負責或建立的項目', async () => {
    // Arrange
    const { user, dialog } = await openDialog(threeMembers());
    const form = within(dialog).getByRole('form', { name: '加入成員' });
    const select = within(form).getByRole('combobox', { name: '角色' });

    // Act
    await chooseOption(user, select, '執行者');

    // Assert
    expect(select).toHaveAccessibleDescription('只看得到自己負責或自己建立的項目；能新增、修改、改狀態、留言。');
  });

  it('點選項就送出一次，選單換成新的角色、說明改好了', async () => {
    // Arrange
    const { user, dialog, table, backend } = await openDialog(threeMembers());

    // Act
    await chooseOption(user, roleSelect(table, '範例成員'), '檢視者');

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例成員」的角色改成「檢視者」。');
    expect(backend.puts).toEqual([{ personId: exampleMember.personId, body: { role: 'viewer' } }]);
    await waitFor(() => {
      expect(roleSelect(table, '範例成員')).toHaveTextContent('檢視者');
    });
  });

  it('鍵盤：方向鍵只移動反白、Esc 關上，都不送出；打開後按 Enter 選了就送出，離開時不再送一次', async () => {
    // Arrange
    const { user, dialog, table, backend } = await openDialog(threeMembers());
    const select = roleSelect(table, '範例成員');
    select.focus();

    // Act
    await user.keyboard('{ArrowDown}{ArrowDown}');
    await within(dialog).findByRole('listbox', { name: '範例成員的角色' });
    await user.keyboard('{Escape}');

    // Assert
    await flushPending();
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(select).not.toHaveAttribute('aria-busy');
    expect(select).toHaveTextContent('編輯者');
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
    expect(backend.puts).toHaveLength(0);

    // Act：編輯者的下一個是執行者，再下一個是留言者。
    await user.keyboard('{ArrowDown}');
    await within(dialog).findByRole('listbox', { name: '範例成員的角色' });
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例成員」的角色改成「留言者」。');
    expect(backend.puts).toEqual([{ personId: exampleMember.personId, body: { role: 'commenter' } }]);
    expect(select).toHaveFocus();
    await user.tab();
    await flushPending();
    expect(backend.puts).toHaveLength(1);
  });

  it('選了目前的角色：清單關上，不送出', async () => {
    // Arrange
    const { user, table, backend } = await openDialog(threeMembers());
    const select = roleSelect(table, '範例成員');

    // Act
    await chooseOption(user, select, '編輯者');

    // Assert
    await flushPending();
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(select).not.toHaveAttribute('aria-busy');
    expect(backend.puts).toHaveLength(0);
  });
});

describe('成員的角色選單：最後一位管理員', () => {
  const lockedDetail = () =>
    agyDetail({
      members: [{ ...memberAsEditor, role: 'admin' }, { ...ownerAsAdmin, role: 'editor' }, designer],
      project: { ...agyProject, myRole: 'admin' },
    });

  it('其他角色是不能選的選項，說得出原因；管理員照樣選得到。選單本身也連到下面那段說明', async () => {
    // Arrange：範例成員是組織的一般成員、這個專案唯一的管理員。
    const { user, table } = await openDialog(lockedDetail(), exampleMember);
    const select = roleSelect(table, '範例成員');

    // Act
    const options = await openOptions(user, select);

    // Assert
    expect(select).toHaveAccessibleDescription(LAST_ADMIN_REASON);
    const admin = options.find((option) => option.dataset.value === 'admin');
    expect(admin).not.toHaveAttribute('aria-disabled');
    const blocked = options.filter((option) => option.dataset.value !== 'admin');
    expect(blocked).toHaveLength(6);
    for (const option of blocked) {
      expect(option).toHaveAttribute('aria-disabled', 'true');
      expect(option).toHaveAccessibleDescription(LAST_ADMIN_SHORT);
    }
  });

  it('點了不能選的角色：不送出，清單留著，值不變', async () => {
    // Arrange
    const { user, table, backend } = await openDialog(lockedDetail(), exampleMember);
    const select = roleSelect(table, '範例成員');
    const options = await openOptions(user, select);
    const viewer = options.find((option) => option.dataset.value === 'viewer');
    if (viewer === undefined) throw new Error('沒有「檢視者」這個選項');

    // Act
    await user.click(viewer);

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(select).toHaveTextContent('管理員');
    expect(backend.puts).toHaveLength(0);
  });
});
