import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Person, SessionUser } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const owner: Person = { personId: exampleOwner.personId, displayName: exampleOwner.displayName, email: exampleOwner.email, role: 'owner', disabled: false };
const member: Person = { personId: exampleMember.personId, displayName: exampleMember.displayName, email: exampleMember.email, role: 'member', disabled: false };
const created: Person = { personId: '0190a000-0000-7000-8000-000000000009', displayName: '範例新人', email: 'new@example.com', role: 'admin', disabled: false };

const orgAdmin: SessionUser = { ...exampleMember, role: 'admin' };

/** 人員清單；POST 成功後清單多一個人。 */
function servePeople(initial: readonly Person[]) {
  let people = initial;
  const posted: unknown[] = [];
  server.use(
    http.get('/v1/people', () => ok(people)),
    http.post('/v1/people', async ({ request }) => {
      posted.push(await request.json());
      people = [...people, created];
      return ok(created, 201);
    }),
  );
  return posted;
}

async function openNewPerson(user: SessionUser = exampleOwner) {
  signedInAs(user);
  const posted = servePeople([owner, member]);
  const view = renderApp('/people');
  await screen.findByRole('table', { name: '人員' });
  await view.user.click(screen.getByRole('button', { name: '新增人員' }));
  const dialog = await screen.findByRole('dialog', { name: '新增人員' });
  return { ...view, dialog, posted };
}

describe('人員頁', () => {
  it('列出名稱、Email 與組織角色，標出自己', async () => {
    // Arrange
    signedInAs(exampleOwner);
    servePeople([owner, member]);

    // Act
    renderApp('/people');

    // Assert
    const table = await screen.findByRole('table', { name: '人員' });
    const memberRow = within(table).getByRole('row', { name: /範例成員/ });
    expect(within(memberRow).getByText('member@example.com')).toBeInTheDocument();
    expect(within(memberRow).getByText('成員')).toBeInTheDocument();
    const ownerRow = within(table).getByRole('row', { name: /範例使用者/ });
    expect(within(ownerRow).getByText('擁有者')).toBeInTheDocument();
    expect(within(ownerRow).getByText('你')).toBeInTheDocument();
  });

  it('一般成員看得到清單，但不能新增', async () => {
    // Arrange
    signedInAs(exampleMember);
    servePeople([owner, member]);

    // Act
    renderApp('/people');

    // Assert
    expect(await screen.findByRole('table', { name: '人員' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增人員' })).not.toBeInTheDocument();
  });

  it('訪客拿不到清單時說明沒有權限', async () => {
    // Arrange
    signedInAs({ ...exampleMember, role: 'guest' });
    server.use(http.get('/v1/people', () => failure(403, 'person.forbidden', '你沒有權限看組織的人員。')));

    // Act
    renderApp('/people');

    // Assert
    expect(await screen.findByText('沒有權限')).toBeInTheDocument();
    expect(screen.getByText('你沒有權限看組織的人員。')).toBeInTheDocument();
  });

  it('其他載入失敗可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    servePeople([owner]);
    server.use(http.get('/v1/people', () => failure(500, 'server.internal_error', 'boom'), { once: true }));
    const { user } = renderApp('/people');

    // Act
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('table', { name: '人員' })).toBeInTheDocument();
  });
});

describe('新增人員', () => {
  it('填好送出後關閉對話框，清單出現新的人', async () => {
    // Arrange
    const { user, dialog, posted } = await openNewPerson();

    // Act
    await user.type(within(dialog).getByLabelText('名稱'), ' 範例新人 ');
    await user.type(within(dialog).getByLabelText('Email'), 'new@example.com');
    await user.type(within(dialog).getByLabelText('密碼'), 'example-pass-1');
    await chooseOption(user, within(dialog).getByLabelText('組織角色'), '管理員');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(posted).toEqual([
      { displayName: '範例新人', email: 'new@example.com', password: 'example-pass-1', role: 'admin' },
    ]);
    expect(await within(screen.getByRole('table', { name: '人員' })).findByText('new@example.com')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('已新增「範例新人」。');
  });

  it('預設角色是成員；擁有者可以選「擁有者」', async () => {
    // Arrange
    const { user, dialog } = await openNewPerson();

    // Act
    const roles = within(dialog).getByLabelText('組織角色');

    // Assert
    expect(roles).toHaveTextContent('成員');
    expect((await openOptions(user, roles)).map((option) => option.textContent)).toEqual([
      '擁有者',
      '管理員',
      '成員',
      '訪客',
    ]);
  });

  it('不是擁有者的管理員不能選「擁有者」', async () => {
    // Arrange
    const { user, dialog } = await openNewPerson(orgAdmin);

    // Act
    await openOptions(user, within(dialog).getByLabelText('組織角色'));

    // Assert
    expect(within(dialog).queryByRole('option', { name: '擁有者' })).not.toBeInTheDocument();
  });

  it('沒填或密碼太短時在欄位旁說明，焦點移到第一個有問題的欄位，不送出', async () => {
    // Arrange
    const { user, dialog, posted } = await openNewPerson();

    // Act
    await user.type(within(dialog).getByLabelText('Email'), 'new@example.com');
    await user.type(within(dialog).getByLabelText('密碼'), 'short');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    const name = within(dialog).getByLabelText('名稱');
    expect(name).toHaveAccessibleDescription('請輸入名稱。');
    expect(name).toHaveFocus();
    expect(within(dialog).getByLabelText('密碼')).toHaveAccessibleDescription(/至少要 10 個字元/);
    expect(posted).toHaveLength(0);
  });

  it('後端回的欄位錯誤顯示在對應的欄位旁', async () => {
    // Arrange
    const { user, dialog } = await openNewPerson();
    server.use(
      http.post('/v1/people', () =>
        failure(409, 'person.email_taken', '這個 Email 已經有人使用。', [
          { code: 'person.email_taken', path: 'email', message: '這個 Email 已經有人使用。' },
        ]),
      ),
    );

    // Act
    await user.type(within(dialog).getByLabelText('名稱'), '範例新人');
    await user.type(within(dialog).getByLabelText('Email'), 'member@example.com');
    await user.type(within(dialog).getByLabelText('密碼'), 'example-pass-1');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    const email = within(dialog).getByLabelText('Email');
    await waitFor(() => {
      expect(email).toHaveAccessibleDescription('這個 Email 已經有人使用。');
    });
    expect(email).toHaveFocus();
  });

  it('權限之類對不到欄位的錯誤顯示在表單上方', async () => {
    // Arrange
    const { user, dialog } = await openNewPerson(orgAdmin);
    server.use(http.post('/v1/people', () => failure(403, 'person.forbidden', '只有擁有者可以新增擁有者。')));

    // Act
    await user.type(within(dialog).getByLabelText('名稱'), '範例新人');
    await user.type(within(dialog).getByLabelText('Email'), 'new@example.com');
    await user.type(within(dialog).getByLabelText('密碼'), 'example-pass-1');
    await user.click(within(dialog).getByRole('button', { name: '新增' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('只有擁有者可以新增擁有者。');
  });
});

describe('人員：背景重抓失敗', () => {
  it('已經有清單時重抓失敗，留著原本的清單，提示可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    servePeople([owner, member]);
    const { queryClient } = renderApp('/people');
    const table = await screen.findByRole('table', { name: '人員' });
    server.use(http.get('/v1/people', () => failure(503, 'server.not_ready', '服務正在啟動。')));

    // Act
    await queryClient.refetchQueries({ queryKey: ['people'], exact: true });

    // Assert
    expect(await screen.findByText('無法更新人員清單，畫面上可能是舊的。')).toBeInTheDocument();
    expect(within(table).getByText(member.displayName)).toBeInTheDocument();
  });
});
