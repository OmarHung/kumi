import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Member, Person, ProjectDetail, SessionUser } from '../../api/types';
import { failure, signedInAs } from '../../test/api';
import { agyDetail, agyProject, exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems, servePatch } from '../../test/items';
import { serveMembers } from '../../test/members';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const designer: Member = { personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例設計師', role: 'project_manager' };

const people: Person[] = [
  { ...ownerAsAdmin, email: exampleOwner.email, role: 'owner', disabled: false, jobTitle: null, responsibilities: null },
  { ...memberAsEditor, email: exampleMember.email, role: 'member', disabled: false, jobTitle: null, responsibilities: null },
  { ...designer, email: 'designer@example.com', role: 'member', disabled: false, jobTitle: null, responsibilities: null },
];

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
  return { ...view, dialog, backend };
}

function table(dialog: HTMLElement): HTMLElement {
  return within(dialog).getByRole('table', { name: '成員' });
}

const threeMembers = () => agyDetail({ members: [ownerAsAdmin, memberAsEditor, designer] });

/** 選項給人看的文字：不含裝飾用的頭像（aria-hidden）的字。 */
function optionLabel(option: HTMLElement): string {
  const copy = option.cloneNode(true) as HTMLElement;
  copy.querySelectorAll('[aria-hidden="true"]').forEach((node) => {
    node.remove();
  });
  return copy.textContent;
}

describe('成員對話框：移除成員', () => {
  it('確認後移除：說明他負責的項目留著；那一列不見，焦點到下一列的「移除」', async () => {
    // Arrange
    const { user, dialog, backend } = await openDialog(threeMembers());
    await user.click(within(table(dialog)).getByRole('button', { name: '移除範例成員' }));
    const confirm = screen.getByRole('dialog', { name: '把「範例成員」移出專案？' });

    // Act
    await user.click(within(confirm).getByRole('button', { name: '移除' }));

    // Assert
    expect(confirm).toHaveTextContent('「範例成員」負責的項目與成員欄位的值會留著，之後不能再把他指派進去。');
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例成員」移出專案。他負責的項目留著原本的負責人。');
    expect(backend.deletes).toEqual([exampleMember.personId]);
    await waitFor(() => {
      expect(within(table(dialog)).queryByRole('row', { name: /範例成員/ })).not.toBeInTheDocument();
    });
    expect(screen.queryByRole('dialog', { name: '把「範例成員」移出專案？' })).not.toBeInTheDocument();
    expect(within(table(dialog)).getByRole('button', { name: '移除範例設計師' })).toHaveFocus();
  });

  it('移除最後一列時焦點到上一列的「移除」；取消不會送出', async () => {
    // Arrange
    const { user, dialog, backend } = await openDialog(threeMembers());
    await user.click(within(table(dialog)).getByRole('button', { name: '移除範例設計師' }));
    await user.click(within(screen.getByRole('dialog', { name: '把「範例設計師」移出專案？' })).getByRole('button', { name: '取消' }));
    expect(within(table(dialog)).getByRole('button', { name: '移除範例設計師' })).toHaveFocus();

    // Act
    await user.click(within(table(dialog)).getByRole('button', { name: '移除範例設計師' }));
    await user.click(within(screen.getByRole('dialog', { name: '把「範例設計師」移出專案？' })).getByRole('button', { name: '移除' }));

    // Assert
    await waitFor(() => {
      expect(within(table(dialog)).getByRole('button', { name: '移除範例成員' })).toHaveFocus();
    });
    expect(backend.deletes).toEqual([designer.personId]);
  });

  it('不是組織管理員：最後一位管理員的「移除」停用並說明原因，角色只能是管理員', async () => {
    // Arrange：範例成員是組織的一般成員、這個專案唯一的管理員。
    const detail = agyDetail({
      members: [{ ...memberAsEditor, role: 'admin' }, { ...ownerAsAdmin, role: 'editor' }, designer],
      project: { ...agyProject, myRole: 'admin' },
    });
    const { user, dialog } = await openDialog(detail, exampleMember);
    const me = within(table(dialog)).getByRole('row', { name: /範例成員/ });
    const remove = within(me).getByRole('button', { name: '移除範例成員' });

    // Act
    await user.click(remove);

    // Assert
    const reason = '最後一位管理員：要先把另一位成員設成管理員，才能換掉角色或移出專案。';
    expect(remove).toHaveAttribute('aria-disabled', 'true');
    expect(remove).toHaveAccessibleDescription(reason);
    expect(screen.queryByRole('dialog', { name: /移出專案/ })).not.toBeInTheDocument();
    const role = within(me).getByRole('combobox', { name: '範例成員的角色' });
    expect(role).toHaveAccessibleDescription(reason);
    const disabled = (await openOptions(user, role)).filter((option) => option.getAttribute('aria-disabled') === 'true');
    // 不能選的選項的 textContent 是名稱加上第二行的原因（短語；完整的句子只在清單下面那一段）。
    expect(disabled.map((option) => option.textContent)).toEqual(['編輯者', '執行者', '留言者', '檢視者', '專案經理', '客戶'].map((name) => `${name}最後一位管理員不能換角色`));
  });

  it('組織管理員：最後一位管理員的「移除」與角色都不停用；把自己移出之後我的角色是「未加入」', async () => {
    // Arrange：範例使用者是組織的擁有者、這個專案唯一的管理員。
    const { user, dialog, backend } = await openDialog(threeMembers());
    const owner = within(table(dialog)).getByRole('row', { name: /範例使用者/ });
    const remove = within(owner).getByRole('button', { name: '移除範例使用者' });
    const role = within(owner).getByRole('combobox', { name: '範例使用者的角色' });
    const disabled = (await openOptions(user, role)).filter((option) => option.getAttribute('aria-disabled') === 'true');
    await user.keyboard('{Escape}');

    // Act
    await user.click(remove);
    await user.click(within(screen.getByRole('dialog', { name: '把自己移出專案？' })).getByRole('button', { name: '移除' }));

    // Assert
    expect(remove).not.toHaveAttribute('aria-disabled');
    expect(remove).not.toHaveAccessibleDescription();
    expect(disabled).toEqual([]);
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例使用者」移出專案。');
    expect(backend.deletes).toEqual([exampleOwner.personId]);
    expect(await screen.findByText('我的角色：未加入')).toBeInTheDocument();
  });

  it('組織管理員把最後一位管理員換成別的角色：照常送出，不擋', async () => {
    // Arrange
    const { user, dialog, backend } = await openDialog(threeMembers());

    // Act
    await chooseOption(user, within(table(dialog)).getByRole('combobox', { name: '範例使用者的角色' }), '專案經理');

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例使用者」的角色改成「專案經理」。');
    expect(backend.puts).toEqual([{ personId: exampleOwner.personId, body: { role: 'project_manager' } }]);
    expect(await screen.findByText('我的角色：專案經理')).toBeInTheDocument();
  });

  it('伺服器說是最後一位管理員（409）時在確認對話框裡說明', async () => {
    // Arrange
    const twoAdmins = agyDetail({ members: [ownerAsAdmin, { ...memberAsEditor, role: 'admin' }] });
    const { user, dialog } = await openDialog(twoAdmins);
    server.use(
      http.delete('/v1/projects/AGY/members/:personId', () =>
        failure(409, 'project.last_admin', '專案至少要有一位管理員：先把另一位成員設成管理員。'),
      ),
    );
    await user.click(within(table(dialog)).getByRole('button', { name: '移除範例成員' }));
    const confirm = screen.getByRole('dialog', { name: '把「範例成員」移出專案？' });

    // Act
    await user.click(within(confirm).getByRole('button', { name: '移除' }));

    // Assert
    expect(await within(confirm).findByRole('alert')).toHaveTextContent('專案至少要有一位管理員：先把另一位成員設成管理員。');
  });

  it('把最後一位管理員換成別的角色被伺服器擋下（409）時顯示原因，角色退回管理員', async () => {
    // Arrange：畫面上還以為有兩位管理員，伺服器那邊另一位已經不是了。
    const shown = agyDetail({ members: [ownerAsAdmin, { ...memberAsEditor, role: 'admin' }] });
    const { user, dialog } = await openDialog(shown);
    server.use(
      http.put('/v1/projects/AGY/members/:personId', () =>
        failure(409, 'project.last_admin', '專案至少要有一位管理員：先把另一位成員設成管理員。'),
      ),
    );

    // Act
    await chooseOption(user, within(table(dialog)).getByRole('combobox', { name: '範例使用者的角色' }), '編輯者');

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('專案至少要有一位管理員');
    expect(within(table(dialog)).getByRole('combobox', { name: '範例使用者的角色' })).toHaveTextContent('管理員');
  });

  it('把自己移出專案（不是組織管理員）：說明會看不到這個專案，成功後回到專案清單並說明', async () => {
    // Arrange
    const me: Member = { ...memberAsEditor, role: 'admin' };
    const detail = agyDetail({ members: [ownerAsAdmin, me], project: { ...agyProject, myRole: 'admin' } });
    const { user, dialog, router, backend, queryClient } = await openDialog(detail, exampleMember);
    await user.click(within(table(dialog)).getByRole('button', { name: '移除範例成員' }));
    const confirm = screen.getByRole('dialog', { name: '把自己移出專案？' });

    // Act
    await user.click(within(confirm).getByRole('button', { name: '移除' }));

    // Assert
    expect(confirm).toHaveTextContent('你會看不到這個專案，畫面會回到專案清單。');
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects');
    });
    expect(await screen.findByText('你已經不是「接案測試」的成員，看不到這個專案了。')).toHaveAttribute('role', 'status');
    expect(screen.getByRole('heading', { level: 1, name: '專案' })).toHaveFocus();
    // 側欄與專案清單都不再有這個專案。
    await waitFor(() => {
      expect(screen.queryByRole('link', { name: '接案測試' })).not.toBeInTheDocument();
    });
    // 已經看不到的專案不再去讀（讀了只會是 404）。
    await waitFor(() => {
      expect(queryClient.isFetching()).toBe(0);
    });
    expect(backend.hiddenReads()).toBe(0);
  });

  it('組織管理員把自己移出專案：還看得到，留在專案頁', async () => {
    // Arrange
    const twoAdmins = agyDetail({ members: [ownerAsAdmin, { ...memberAsEditor, role: 'admin' }] });
    const { user, dialog, router } = await openDialog(twoAdmins);
    await user.click(within(table(dialog)).getByRole('button', { name: '移除範例使用者' }));
    const confirm = screen.getByRole('dialog', { name: '把自己移出專案？' });

    // Act
    await user.click(within(confirm).getByRole('button', { name: '移除' }));

    // Assert
    expect(confirm).not.toHaveTextContent('你會看不到這個專案');
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例使用者」移出專案。');
    expect(currentPath(router)).toBe('/projects/AGY');
  });

  it('沒有管理專案權限的人沒有「移除」', async () => {
    // Arrange
    const detail = agyDetail({ myPermissions: ['item.read'], project: { ...agyProject, myRole: 'viewer' } });
    signedInAs(exampleMember, { details: [detail] });
    serveItems('AGY', []);
    const { user } = renderApp('/projects/AGY');

    // Act
    await user.click(await screen.findByRole('button', { name: '成員' }));
    const dialog = await screen.findByRole('dialog', { name: '成員' });

    // Assert
    expect(within(dialog).queryByRole('button', { name: /^移除/ })).not.toBeInTheDocument();
  });

  it('負責人已經不是成員（被移出專案）：顯示他的名字，編輯時選單也選著他，不動就不送', async () => {
    // Arrange
    const former = { personId: '0190a000-0000-7000-8000-000000000009', displayName: '離開的成員' };
    const task = smkItem({ number: 7, assignee: former });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task]);
    const patches = servePatch(task);
    const { user } = renderApp('/projects/SMK');
    const row = within(await screen.findByRole('table', { name: '項目' })).getByRole('row', { name: /SMK-7/ });

    // Act
    await user.click(within(row).getByRole('button', { name: '負責人：離開的成員' }));
    const select = within(row).getByRole('combobox', { name: '負責人' });
    const options = (await openOptions(user, select)).map(optionLabel);
    await user.keyboard('{Escape}');

    // Assert
    expect(select).toHaveTextContent('離開的成員');
    expect(options).toEqual(['沒有負責人', '範例使用者', '範例成員', '離開的成員']);
    expect(patches).toHaveLength(0);
  });

  it('成員欄位的值是已被移出的人：顯示「不是成員的人」，編輯時選單保留目前的值，不動就不送', async () => {
    // Arrange：成員欄位只有人員編號，查不到名字。
    const task = smkItem({ number: 7, fields: { reviewer: '0190a000-0000-7000-8000-000000000009' } });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task]);
    const patches = servePatch(task);
    const { user } = renderApp('/projects/SMK');
    const row = within(await screen.findByRole('table', { name: '項目' })).getByRole('row', { name: /SMK-7/ });

    // Act
    await user.click(within(row).getByRole('button', { name: '審查者：不是成員的人' }));
    const select = within(row).getByRole('combobox', { name: '審查者' });
    const options = (await openOptions(user, select)).map(optionLabel);
    await user.keyboard('{Escape}');

    // Assert
    expect(select).toHaveTextContent('不是成員的人');
    expect(options).toEqual(['沒有人', '範例使用者', '範例成員', '不是成員的人']);
    expect(patches).toHaveLength(0);
  });
});
