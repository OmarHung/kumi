import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Member, Person, ProjectDetail, SessionUser } from '../../api/types';
import { AppProviders } from '../../app/AppProviders';
import { createQueryClient } from '../../app/queryClient';
import { CurrentUserProvider } from '../../auth/CurrentUser';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { agyDetail, agyProject, exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin } from '../../test/fixtures';
import { serveMembers } from '../../test/members';
import { chooseOption } from '../../test/select';
import { MembersDialog } from './MembersDialog';

const designer: Member = { personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例設計師', role: 'project_manager' };

const people: Person[] = [
  { ...ownerAsAdmin, email: exampleOwner.email, role: 'owner', disabled: false, jobTitle: null, responsibilities: null },
  { ...memberAsEditor, email: exampleMember.email, role: 'member', disabled: false, jobTitle: null, responsibilities: null },
  { ...designer, email: 'designer@example.com', role: 'member', disabled: false, jobTitle: '設計師', responsibilities: null },
];

/** 窄螢幕：只有窄螢幕的查詢符合。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

/** 只渲染成員對話框：不經過專案頁，窄螢幕的專案頁長什麼樣不影響這裡。 */
async function openDialog(detail: ProjectDetail, viewer: SessionUser = exampleOwner) {
  signedInAs(viewer, { details: [detail] });
  const backend = serveMembers(detail, {
    people,
    viewer: { personId: viewer.personId, isOrganizationAdmin: viewer.role === 'owner' || viewer.role === 'admin' },
  });
  const queryClient = createQueryClient({ queries: { retry: false } });
  const ui = (
    <CurrentUserProvider user={viewer}>
      <MembersDialog projectKey="AGY" onClose={() => undefined} />
    </CurrentUserProvider>
  );
  const router = createMemoryRouter([{ path: '*', element: ui }], { initialEntries: ['/projects/AGY'] });
  const user = userEvent.setup();
  render(
    <AppProviders queryClient={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>,
  );
  const dialog = await screen.findByRole('dialog', { name: '成員' });
  const list = await within(dialog).findByRole('list', { name: '成員' });
  return { user, dialog, list, backend };
}

function itemOf(list: HTMLElement, name: string): HTMLElement {
  const item = within(list).getAllByRole('listitem').find((candidate) => within(candidate).queryByText(name) !== null);
  if (item === undefined) throw new Error(`找不到「${name}」那一筆`);
  return item;
}

const threeMembers = () => agyDetail({ members: [ownerAsAdmin, memberAsEditor, designer] });

describe('成員對話框：寬螢幕', () => {
  it('對話框是寬的（裡面有表格）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [threeMembers()] });
    serveMembers(threeMembers(), { people });
    const queryClient = createQueryClient({ queries: { retry: false } });
    const ui = (
      <CurrentUserProvider user={exampleOwner}>
        <MembersDialog projectKey="AGY" onClose={() => undefined} />
      </CurrentUserProvider>
    );
    const router = createMemoryRouter([{ path: '*', element: ui }]);

    // Act
    render(
      <AppProviders queryClient={queryClient}>
        <RouterProvider router={router} />
      </AppProviders>,
    );
    const dialog = await screen.findByRole('dialog', { name: '成員' });

    // Assert
    expect(dialog).toHaveAttribute('data-size', 'lg');
    expect(await within(dialog).findByRole('table', { name: '成員' })).toBeInTheDocument();
    expect(within(dialog).queryByRole('list', { name: '成員' })).not.toBeInTheDocument();
  });
});

describe('成員對話框：窄螢幕', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('成員的名稱後面寫職稱（灰字），沒有職稱的人沒有', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    const { list } = await openDialog(threeMembers());

    // Assert
    expect(await within(itemOf(list, '範例設計師')).findByText('設計師')).toBeInTheDocument();
    expect(within(itemOf(list, '範例成員')).queryByText('設計師')).not.toBeInTheDocument();
  });

  it('成員是清單而不是表格：每一筆有名稱、角色選單與「移除」，標出自己', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    const { dialog, list } = await openDialog(threeMembers());

    // Assert
    expect(within(dialog).queryByRole('table')).not.toBeInTheDocument();
    expect(within(list).getAllByRole('listitem')).toHaveLength(3);
    const owner = itemOf(list, '範例使用者');
    expect(within(owner).getByText('你')).toBeInTheDocument();
    expect(within(owner).getByRole('combobox', { name: '範例使用者的角色' })).toHaveTextContent('管理員');
    expect(within(itemOf(list, '範例設計師')).getByRole('combobox', { name: '範例設計師的角色' })).toHaveTextContent('專案經理');
    expect(within(itemOf(list, '範例成員')).getByRole('button', { name: '移除範例成員' })).toBeInTheDocument();
  });

  it('沒有管理專案權限的人只看得到角色的名稱', async () => {
    // Arrange
    stubCompactScreen();
    const detail = agyDetail({ myPermissions: ['item.read'], project: { ...agyProject, myRole: 'viewer' } });

    // Act
    const { dialog, list } = await openDialog(detail, exampleMember);

    // Assert
    expect(within(itemOf(list, '範例成員')).getByText('編輯者')).toBeInTheDocument();
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /^移除/ })).not.toBeInTheDocument();
  });

  it('改角色照樣儲存並說明', async () => {
    // Arrange
    stubCompactScreen();
    const { user, dialog, list, backend } = await openDialog(threeMembers());

    // Act
    await chooseOption(user, within(itemOf(list, '範例成員')).getByRole('combobox'), '檢視者');

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已把「範例成員」的角色改成「檢視者」。');
    expect(backend.puts).toEqual([{ personId: exampleMember.personId, body: { role: 'viewer' } }]);
  });

  it('移除之後那一筆不見，焦點到下一筆的「移除」', async () => {
    // Arrange
    stubCompactScreen();
    const { user, list, backend } = await openDialog(threeMembers());
    await user.click(within(itemOf(list, '範例成員')).getByRole('button', { name: '移除範例成員' }));

    // Act
    await user.click(within(screen.getByRole('dialog', { name: '把「範例成員」移出專案？' })).getByRole('button', { name: '移除' }));

    // Assert
    await waitFor(() => {
      expect(within(list).queryByText('範例成員')).not.toBeInTheDocument();
    });
    expect(backend.deletes).toEqual([exampleMember.personId]);
    expect(within(list).getByRole('button', { name: '移除範例設計師' })).toHaveFocus();
  });

  it('不是組織管理員：最後一位管理員的「移除」與角色選單都連到說明', async () => {
    // Arrange
    stubCompactScreen();
    const detail = agyDetail({
      members: [{ ...memberAsEditor, role: 'admin' }, { ...ownerAsAdmin, role: 'editor' }, designer],
      project: { ...agyProject, myRole: 'admin' },
    });

    // Act
    const { list } = await openDialog(detail, exampleMember);

    // Assert
    const reason = '最後一位管理員：要先把另一位成員設成管理員，才能換掉角色或移出專案。';
    const me = itemOf(list, '範例成員');
    expect(within(me).getByRole('button', { name: '移除範例成員' })).toHaveAccessibleDescription(reason);
    expect(within(me).getByRole('button', { name: '移除範例成員' })).toHaveAttribute('aria-disabled', 'true');
    expect(within(me).getByRole('combobox', { name: '範例成員的角色' })).toHaveAccessibleDescription(reason);
  });

  it('最後一位管理員的說明在清單下面一段，不擠進那一筆；關係照舊連得到', async () => {
    // Arrange
    stubCompactScreen();
    const detail = agyDetail({
      members: [{ ...memberAsEditor, role: 'admin' }, { ...ownerAsAdmin, role: 'editor' }],
      project: { ...agyProject, myRole: 'admin' },
    });

    // Act
    const { dialog, list } = await openDialog(detail, exampleMember);

    // Assert
    const reason = '最後一位管理員：要先把另一位成員設成管理員，才能換掉角色或移出專案。';
    expect(within(list).queryByText(reason)).not.toBeInTheDocument();
    expect(within(dialog).getByText(reason)).toBeInTheDocument();
    expect(within(itemOf(list, '範例成員')).getByRole('button', { name: '移除範例成員' })).toHaveAccessibleDescription(reason);
  });
});

describe('成員對話框：寬螢幕的最後一位管理員', () => {
  it('說明在表格下面一段，那一列維持一行；「移除」與角色選單照舊連到它', async () => {
    // Arrange
    const detail = agyDetail({
      members: [{ ...memberAsEditor, role: 'admin' }, { ...ownerAsAdmin, role: 'editor' }],
      project: { ...agyProject, myRole: 'admin' },
    });
    signedInAs(exampleMember, { details: [detail] });
    serveMembers(detail, { people, viewer: { personId: exampleMember.personId, isOrganizationAdmin: false } });
    const queryClient = createQueryClient({ queries: { retry: false } });
    const ui = (
      <CurrentUserProvider user={exampleMember}>
        <MembersDialog projectKey="AGY" onClose={() => undefined} />
      </CurrentUserProvider>
    );
    const router = createMemoryRouter([{ path: '*', element: ui }]);

    // Act
    render(
      <AppProviders queryClient={queryClient}>
        <RouterProvider router={router} />
      </AppProviders>,
    );
    const dialog = await screen.findByRole('dialog', { name: '成員' });
    const table = await within(dialog).findByRole('table', { name: '成員' });

    // Assert
    const reason = '最後一位管理員：要先把另一位成員設成管理員，才能換掉角色或移出專案。';
    expect(within(table).queryByText(reason)).not.toBeInTheDocument();
    expect(within(dialog).getByText(reason)).toBeInTheDocument();
    const me = within(table).getByRole('row', { name: /範例成員/ });
    expect(within(me).getByRole('button', { name: '移除範例成員' })).toHaveAccessibleDescription(reason);
    expect(within(me).getByRole('combobox', { name: '範例成員的角色' })).toHaveAccessibleDescription(reason);
    expect(within(dialog).getByRole('region', { name: '成員' })).toContainElement(table);
  });
});
