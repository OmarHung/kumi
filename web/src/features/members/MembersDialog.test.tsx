import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Member, Person, ProjectDetail } from '../../api/types';
import { serveMembers } from '../../test/members';
import { failure, ok, signedInAs } from '../../test/api';
import {
  agyDetail,
  agyItem,
  agyProject,
  exampleMember,
  exampleOwner,
  memberAsEditor,
  ownerAsAdmin,
} from '../../test/fixtures';
import { failTransition, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const designer: Member = { personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例設計師', role: 'project_manager' };
const newcomer: Person = { personId: '0190a000-0000-7000-8000-000000000004', displayName: '範例新人', email: 'new@example.com', role: 'member', disabled: false };

const people: Person[] = [
  { ...ownerAsAdmin, email: exampleOwner.email, role: 'owner', disabled: false },
  { ...memberAsEditor, email: exampleMember.email, role: 'member', disabled: false },
  { ...designer, email: 'designer@example.com', role: 'member', disabled: false },
  newcomer,
];

async function openDialog(detail: ProjectDetail = agyDetail({ members: [ownerAsAdmin, memberAsEditor, designer] })) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItems('AGY', []);
  const backend = serveMembers(detail, { people });
  const view = renderApp('/projects/AGY');
  await view.user.click(await screen.findByRole('button', { name: '成員' }));
  const dialog = await screen.findByRole('dialog', { name: '成員' });
  return { ...view, dialog, backend };
}

function roleOf(dialog: HTMLElement, name: string): HTMLElement {
  return within(within(dialog).getByRole('table', { name: '成員' })).getByRole('row', { name: new RegExp(name) });
}

describe('成員對話框', () => {
  it('能管理成員的人看得到最高權限的說明，只能看的人沒有', async () => {
    // Arrange & Act
    const { dialog } = await openDialog();

    // Assert
    expect(within(dialog).getByText('有「管理專案」（project.manage）的角色能設定成員與角色；能「套用變更集」（blueprint.apply）的角色能改每個角色的權限。兩者都等同這個專案的最高權限，只給信得過的人。')).toBeInTheDocument();
  });

  it('列出成員與角色：內建角色用中文，自訂角色用藍圖的名稱；Esc 關閉後焦點回到按鈕', async () => {
    // Arrange
    const { user, dialog } = await openDialog();

    // Act：名稱由標籤的 aria-labelledby 來，對話框關上（離開 document）之後就算不出來，所以先找好。
    const ownerRole = within(roleOf(dialog, '範例使用者')).getByRole('combobox', { name: '範例使用者的角色' });
    const customRole = within(roleOf(dialog, '範例設計師')).getByRole('combobox', { name: '範例設計師的角色' });
    // 選項只有清單打開時才在 DOM 裡：對話框關上之前先看；第一個 Esc 只關清單，第二個才關對話框。
    const options = (await openOptions(user, ownerRole)).map((option) => option.textContent);
    await user.keyboard('{Escape}');
    await user.keyboard('{Escape}');

    // Assert
    expect(ownerRole).toHaveTextContent('管理員');
    expect(customRole).toHaveTextContent('專案經理');
    expect(options).toEqual(['管理員', '編輯者', '執行者', '留言者', '檢視者', '專案經理', '客戶']);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '成員' })).toHaveFocus();
  });

  it('沒有管理專案權限的人只能看，不讀組織的人員清單', async () => {
    // Arrange
    const detail = agyDetail({ myPermissions: ['item.read'], project: { ...agyProject, myRole: 'viewer' } });
    signedInAs(exampleMember, { details: [detail] });
    serveItems('AGY', []);
    const { user } = renderApp('/projects/AGY');

    // Act
    await user.click(await screen.findByRole('button', { name: '成員' }));
    const dialog = await screen.findByRole('dialog', { name: '成員' });

    // Assert
    expect(within(roleOf(dialog, '範例成員')).getByText('編輯者')).toBeInTheDocument();
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('form', { name: '加入成員' })).not.toBeInTheDocument();
    expect(within(dialog).queryByText(/等同這個專案的最高權限/)).not.toBeInTheDocument();
  });

  it('改角色後重新讀專案內容，表格與我的角色跟著更新', async () => {
    // Arrange：還有另一位管理員，才能把自己換成別的角色。
    const { user, dialog, backend } = await openDialog(agyDetail({ members: [ownerAsAdmin, memberAsEditor, { ...designer, role: 'admin' }] }));
    const before = backend.detailRequests();

    // Act
    await chooseOption(user, within(roleOf(dialog, '範例使用者')).getByRole('combobox'), '專案經理');

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例使用者」的角色改成「專案經理」。');
    expect(backend.puts).toEqual([{ personId: exampleOwner.personId, body: { role: 'project_manager' } }]);
    await waitFor(() => {
      expect(backend.detailRequests()).toBeGreaterThan(before);
    });
    expect(within(roleOf(dialog, '範例使用者')).getByRole('combobox')).toHaveTextContent('專案經理');
  });

  it('從組織的人員裡加成員，已經是成員的人不在選項裡', async () => {
    // Arrange
    const { user, dialog, backend } = await openDialog();
    const form = within(dialog).getByRole('form', { name: '加入成員' });
    const personSelect = within(form).getByRole('combobox', { name: '人員' });
    // 人員讀到之前按鈕上是「載入中…」。
    await waitFor(() => {
      expect(personSelect).toHaveTextContent('選擇人員');
    });

    // Act
    const values = (await openOptions(user, personSelect)).map((option) => option.dataset.value);
    await chooseOption(user, personSelect, '範例新人（new@example.com）');
    await chooseOption(user, within(form).getByRole('combobox', { name: '角色' }), '客戶');
    await user.click(within(form).getByRole('button', { name: '加入' }));

    // Assert：「選擇人員」是還沒選時按鈕上的灰字（placeholder），不是選項；選項只有還不是成員的人。
    expect(values).toEqual([newcomer.personId]);
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已加入範例新人（客戶）。');
    expect(backend.puts).toEqual([{ personId: newcomer.personId, body: { role: 'client' } }]);
    expect(await within(within(dialog).getByRole('table', { name: '成員' })).findByText('範例新人')).toBeInTheDocument();
  });

  it('人員還在讀：人員選單停用、按鈕上是灰字的「載入中…」（和模板的選單一樣），讀到之後才選得了', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const detail = agyDetail({ members: [ownerAsAdmin, memberAsEditor, designer] });
    signedInAs(exampleOwner, { details: [detail] });
    serveItems('AGY', []);
    serveMembers(detail, { people });
    server.use(
      http.get('/v1/people', async () => {
        await gate;
        return ok(people);
      }),
    );
    const { user } = renderApp('/projects/AGY');
    await user.click(await screen.findByRole('button', { name: '成員' }));
    const dialog = await screen.findByRole('dialog', { name: '成員' });
    const personSelect = within(within(dialog).getByRole('form', { name: '加入成員' })).getByRole('combobox', { name: '人員' });

    // Act
    const isDisabledWhileLoading = personSelect.hasAttribute('disabled');
    const textWhileLoading = personSelect.textContent;
    release();

    // Assert
    expect(isDisabledWhileLoading).toBe(true);
    expect(textWhileLoading).toBe('載入中…');
    await waitFor(() => {
      expect(personSelect).not.toBeDisabled();
    });
    expect(personSelect).toHaveTextContent('選擇人員');
  });

  it('沒選人就按加入時說明', async () => {
    // Arrange
    const { user, dialog, backend } = await openDialog();
    const form = within(dialog).getByRole('form', { name: '加入成員' });

    // Act
    await user.click(within(form).getByRole('button', { name: '加入' }));

    // Assert
    expect(within(form).getByRole('combobox', { name: '人員' })).toHaveAccessibleDescription('請選擇要加入的人。');
    expect(backend.puts).toHaveLength(0);
  });

  it('改角色失敗時顯示後端的訊息', async () => {
    // Arrange
    const { user, dialog } = await openDialog();
    server.use(
      http.put('/v1/projects/AGY/members/:personId', () =>
        failure(403, 'work.forbidden', '你沒有權限管理這個專案的成員。'),
      ),
    );

    // Act
    await chooseOption(user, within(roleOf(dialog, '範例成員')).getByRole('combobox'), '檢視者');

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('你沒有權限管理這個專案的成員。');
  });

  it('組織的人員清單讀不到時說明，成員清單照樣可以用', async () => {
    // Arrange
    const detail = agyDetail({ members: [ownerAsAdmin, memberAsEditor] });
    signedInAs(exampleOwner, { details: [detail] });
    serveItems('AGY', []);
    server.use(http.get('/v1/people', () => failure(403, 'person.forbidden', '你沒有權限看組織的人員。')));
    const { user } = renderApp('/projects/AGY');

    // Act
    await user.click(await screen.findByRole('button', { name: '成員' }));
    const dialog = await screen.findByRole('dialog', { name: '成員' });

    // Assert
    expect(await within(dialog).findByText('你沒有權限看組織的人員。')).toBeInTheDocument();
    expect(within(roleOf(dialog, '範例成員')).getByRole('combobox')).toBeInTheDocument();
  });

  it('改狀態被角色擋下時，可以從錯誤說明直接打開成員對話框', async () => {
    // Arrange
    const item = agyItem({ number: 3, state: 'internal_qa', stateCategory: 'in_progress' });
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [item]);
    server.use(http.get('/v1/people', () => ok(people)));
    failTransition(item, 403, 'item.transition_role_required', '只有「專案經理」可以做。', []);
    const { user } = renderApp('/projects/AGY');
    const row = within(await screen.findByRole('table', { name: '項目' })).getByRole('row', { name: /AGY-3/ });

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：內部驗收' }));
    await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '客戶驗收');
    await user.click(await within(row).findByRole('button', { name: '到「成員」調整角色' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: '成員' })).toBeInTheDocument();
  });
});
