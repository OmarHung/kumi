import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Person, SessionUser } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { adminPerson, guestPerson, memberPerson, ownerPerson, servePeopleAdmin } from '../../test/people';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const orgAdmin: SessionUser = {
  ...exampleOwner,
  personId: adminPerson.personId,
  displayName: adminPerson.displayName,
  email: 'admin@example.com',
  role: 'admin',
};
const otherAdmin: Person = { ...adminPerson, personId: '0190a000-0000-7000-8000-000000000006', displayName: '範例副管理員', email: 'admin2@example.com' };
const disabledMember: Person = { ...memberPerson, disabled: true };

async function openPeople(actor: SessionUser, people: readonly Person[]) {
  signedInAs(actor);
  const backend = servePeopleAdmin({ actor, people });
  const view = renderApp('/people');
  const table = await screen.findByRole('table', { name: '人員' });
  return { ...view, table, backend };
}

async function openActions(user: ReturnType<typeof renderApp>['user'], name: string) {
  const button = await screen.findByRole('button', { name: `${name} 的動作` });
  await user.click(button);
  const menu = await screen.findByRole('menu', { name: `${name} 的動作` });
  return { button, menu };
}

async function openDisableDialog() {
  const view = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
  const { menu, button } = await openActions(view.user, '範例成員');
  await view.user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));
  await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });
  return { ...view, button };
}

describe('人員頁：停用的人', () => {
  it('停用的人照樣在清單裡，名稱後面標「已停用」', async () => {
    // Arrange
    const people = [ownerPerson, disabledMember];

    // Act
    const { table } = await openPeople(exampleOwner, people);

    // Assert
    const row = within(table).getByRole('row', { name: /範例成員/ });
    expect(within(row).getByText('已停用')).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /範例使用者/ })).queryByText('已停用')).not.toBeInTheDocument();
  });
});

describe('人員頁：每一列的動作', () => {
  it('擁有者看得到別人那一列的「動作」，自己那一列沒有', async () => {
    // Arrange
    const people = [ownerPerson, adminPerson, memberPerson];

    // Act
    const { table } = await openPeople(exampleOwner, people);

    // Assert
    expect(within(table).getByRole('columnheader', { name: '動作' })).toBeInTheDocument();
    expect(within(table).getByRole('button', { name: '範例管理員 的動作' })).toBeInTheDocument();
    expect(within(table).getByRole('button', { name: '範例成員 的動作' })).toBeInTheDocument();
    expect(within(table).queryByRole('button', { name: '範例使用者 的動作' })).not.toBeInTheDocument();
  });

  it('管理員看不到擁有者、其他管理員與自己那一列的動作；成員與訪客的有', async () => {
    // Arrange
    const people = [ownerPerson, adminPerson, otherAdmin, memberPerson, guestPerson];

    // Act
    const { table } = await openPeople(orgAdmin, people);

    // Assert
    const buttons = within(table).getAllByRole('button', { name: /的動作$/ });
    expect(buttons.map((button) => button.getAttribute('aria-label'))).toEqual(['範例成員 的動作', '範例訪客 的動作']);
  });

  it('沒有登入帳號的人（沒有 Email）沒有東西可以停用或重設，選單裡只剩「變更角色…」', async () => {
    // Arrange
    const noAccount: Person = { ...guestPerson, email: null };
    const { user } = await openPeople(exampleOwner, [ownerPerson, memberPerson, noAccount]);

    // Act
    const { menu } = await openActions(user, '範例訪客');

    // Assert
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['變更角色…']);
  });

  it('一般成員沒有「動作」那一欄', async () => {
    // Arrange
    const people = [ownerPerson, memberPerson];

    // Act
    const { table } = await openPeople(exampleMember, people);

    // Assert
    expect(within(table).queryByRole('columnheader', { name: '動作' })).not.toBeInTheDocument();
    expect(within(table).queryByRole('button')).not.toBeInTheDocument();
  });

  it('選單裡是「變更角色…」「重設密碼…」與「停用帳號…」；停用的人是「重新啟用帳號」', async () => {
    // Arrange
    const { user } = await openPeople(exampleOwner, [ownerPerson, memberPerson, { ...guestPerson, disabled: true }]);

    // Act
    const { menu: active } = await openActions(user, '範例成員');
    const activeItems = within(active).getAllByRole('menuitem').map((item) => item.textContent);
    await user.keyboard('{Escape}');
    const { menu: disabled } = await openActions(user, '範例訪客');

    // Assert
    expect(activeItems).toEqual(['變更角色…', '重設密碼…', '停用帳號…']);
    expect(within(disabled).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['變更角色…', '重設密碼…', '重新啟用帳號']);
  });

  it('用鍵盤打開選單、Esc 關上，焦點回到那一列的動作按鈕', async () => {
    // Arrange
    const { user } = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
    const button = await screen.findByRole('button', { name: '範例成員 的動作' });
    button.focus();

    // Act
    await user.keyboard('{Enter}');
    const menu = await screen.findByRole('menu', { name: '範例成員 的動作' });
    const firstItem = within(menu).getByRole('menuitem', { name: '變更角色…' });
    const hadFocus = document.activeElement === firstItem;
    await user.keyboard('{Escape}');

    // Assert
    expect(hadFocus).toBe(true);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });
});

describe('人員頁：停用帳號', () => {
  it('確認對話框寫出後果；停用之後對話框關上、標「已停用」，頁面上方說明結果，焦點回到動作按鈕', async () => {
    // Arrange
    const { user, table, backend } = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
    const { menu, button } = await openActions(user, '範例成員');

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));
    const dialog = await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });
    const description = dialog.getAttribute('aria-describedby');
    await user.click(within(dialog).getByRole('button', { name: '停用帳號' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(description).not.toBeNull();
    expect(backend.requests).toEqual([{ action: 'disable', personId: memberPerson.personId, body: undefined }]);
    expect(await within(within(table).getByRole('row', { name: /範例成員/ })).findByText('已停用')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('已停用「範例成員」的帳號。');
    expect(button).toHaveFocus();
  });

  it('對話框的說明：會立刻被登出、不能再登入，項目與活動留著，之後可以重新啟用', async () => {
    // Arrange
    const { user } = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
    const { menu } = await openActions(user, '範例成員');

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });
    expect(dialog).toHaveAccessibleDescription(
      '停用之後，他會立刻被登出，也不能再登入。他負責的項目、留言與活動都會留著，之後可以重新啟用。',
    );
    expect(within(dialog).getByRole('button', { name: '取消' })).toBeInTheDocument();
  });

  it('Esc 關上確認對話框，焦點回到動作按鈕，什麼都沒送', async () => {
    // Arrange
    const { user, backend } = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
    const { menu, button } = await openActions(user, '範例成員');
    await user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));
    await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
    await flushPending();
    expect(backend.requests).toEqual([]);
  });

  it('送出中確認鈕寫「停用中…」、標 aria-disabled（焦點留在上面，再按不重送），「取消」停用', async () => {
    // Arrange
    const { user, backend } = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('/v1/people/:personId/disable', async ({ params }) => {
        backend.requests.push({ action: 'disable', personId: String(params.personId), body: undefined });
        await released;
        return ok({ ...memberPerson, disabled: true });
      }),
    );
    const { menu } = await openActions(user, '範例成員');
    await user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));
    const dialog = await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '停用帳號' }));
    const pending = await within(dialog).findByRole('button', { name: '停用中…' });
    await user.click(pending);

    // Assert
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    expect(within(dialog).getByRole('button', { name: '取消' })).toBeDisabled();
    await flushPending();
    expect(backend.requests).toHaveLength(1);
    release();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('被擋下來（403）時對話框留著，寫出伺服器的說明', async () => {
    // Arrange
    const { user } = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
    server.use(
      http.post('/v1/people/:personId/disable', () =>
        failure(403, 'person.forbidden', '只有擁有者可以停用擁有者或管理員的帳號。'),
      ),
    );
    const { menu } = await openActions(user, '範例成員');
    await user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));
    const dialog = await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '停用帳號' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('只有擁有者可以停用擁有者或管理員的帳號。');
    const confirm = within(dialog).getByRole('button', { name: '停用帳號' });
    expect(confirm).not.toHaveAttribute('aria-disabled');
    expect(confirm).toHaveFocus();
  });

  it('失敗之後焦點還在對話框裡：Esc 關得掉，焦點回到動作按鈕', async () => {
    // Arrange
    const { user, button } = await openDisableDialog();
    server.use(http.post('/v1/people/:personId/disable', () => failure(409, 'person.no_account', '這個人沒有登入帳號。')));
    const dialog = screen.getByRole('dialog', { name: '停用「範例成員」的帳號？' });
    await user.click(within(dialog).getByRole('button', { name: '停用帳號' }));
    await within(dialog).findByRole('alert');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('這個人已經不在組織裡（404）時對話框留著說明，清單重抓', async () => {
    // Arrange
    const { user } = await openPeople(exampleOwner, [ownerPerson, memberPerson]);
    let listed = 0;
    server.use(
      http.post('/v1/people/:personId/disable', () => failure(404, 'person.not_found', '這個組織裡沒有這個人。')),
      http.get('/v1/people', () => {
        listed += 1;
        return ok([ownerPerson]);
      }),
    );
    const { menu } = await openActions(user, '範例成員');
    await user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));
    const dialog = await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '停用帳號' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('這個組織裡沒有這個人。');
    await waitFor(() => {
      expect(listed).toBe(1);
    });
  });
});

describe('人員頁：重新啟用帳號', () => {
  it('直接送出、不用確認：「已停用」拿掉，頁面上方說明結果，焦點留在動作按鈕', async () => {
    // Arrange
    const { user, table, backend } = await openPeople(exampleOwner, [ownerPerson, disabledMember]);
    const { menu, button } = await openActions(user, '範例成員');

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '重新啟用帳號' }));

    // Assert
    expect(await screen.findByText('已重新啟用「範例成員」的帳號。')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(backend.requests).toEqual([{ action: 'enable', personId: memberPerson.personId, body: undefined }]);
    expect(within(within(table).getByRole('row', { name: /範例成員/ })).queryByText('已停用')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('送出中那一列寫「啟用中…」，動作按鈕打不開（aria-disabled），焦點留在上面', async () => {
    // Arrange
    const { user, table } = await openPeople(exampleOwner, [ownerPerson, disabledMember]);
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('/v1/people/:personId/enable', async () => {
        await released;
        return ok(memberPerson);
      }),
    );
    const { menu, button } = await openActions(user, '範例成員');

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '重新啟用帳號' }));

    // Assert
    await waitFor(() => {
      expect(button).toHaveAttribute('aria-disabled', 'true');
    });
    const row = within(table).getByRole('row', { name: /範例成員/ });
    expect(within(row).getByText('啟用中…')).toBeInTheDocument();
    expect(within(row).queryByText('已停用')).not.toBeInTheDocument();
    await user.click(button);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
    release();
    expect(await screen.findByText('已重新啟用「範例成員」的帳號。')).toBeInTheDocument();
  });

  it('連續重新啟用兩個人：兩個都送出，兩個人的結果都寫出來', async () => {
    // Arrange
    const disabledGuest: Person = { ...guestPerson, disabled: true };
    const { user, table, backend } = await openPeople(exampleOwner, [ownerPerson, disabledMember, disabledGuest]);
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('/v1/people/:personId/enable', async ({ params }) => {
        const personId = String(params.personId);
        backend.requests.push({ action: 'enable', personId, body: undefined });
        if (personId === memberPerson.personId) await released;
        return ok({ ...(personId === memberPerson.personId ? memberPerson : guestPerson), disabled: false });
      }),
    );
    const { menu: first } = await openActions(user, '範例成員');
    await user.click(within(first).getByRole('menuitem', { name: '重新啟用帳號' }));
    const { menu: second } = await openActions(user, '範例訪客');

    // Act
    await user.click(within(second).getByRole('menuitem', { name: '重新啟用帳號' }));
    expect(await screen.findByText(/已重新啟用「範例訪客」的帳號。/)).toBeInTheDocument();
    release();

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('已重新啟用「範例訪客」的帳號。 已重新啟用「範例成員」的帳號。');
    });
    expect(backend.requests.map((request) => request.personId)).toEqual([memberPerson.personId, guestPerson.personId]);
    expect(within(table).queryByText('已停用')).not.toBeInTheDocument();
  });

  it('失敗時在頁面上方說明，清單不變', async () => {
    // Arrange
    const { user, table } = await openPeople(exampleOwner, [ownerPerson, disabledMember]);
    server.use(http.post('/v1/people/:personId/enable', () => failure(409, 'person.no_account', '這個人沒有登入帳號。')));
    const { menu } = await openActions(user, '範例成員');

    // Act
    await user.click(within(menu).getByRole('menuitem', { name: '重新啟用帳號' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('沒有重新啟用「範例成員」的帳號：這個人沒有登入帳號。');
    expect(within(within(table).getByRole('row', { name: /範例成員/ })).getByText('已停用')).toBeInTheDocument();
  });
});
