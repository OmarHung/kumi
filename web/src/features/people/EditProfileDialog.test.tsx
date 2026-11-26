import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Person, SessionUser } from '../../api/types';
import { failure, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { adminPerson, memberPerson, ownerPerson, servePeopleAdmin } from '../../test/people';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const TITLE = '編輯「範例成員」的職稱';

const designer: Person = { ...memberPerson, jobTitle: '前端工程師', responsibilities: 'WordPress 維運、範例客戶的網站' };

async function openEditor(actor: SessionUser = exampleOwner, people: readonly Person[] = [ownerPerson, memberPerson]) {
  signedInAs(actor);
  const backend = servePeopleAdmin({ actor, people });
  const view = renderApp('/people');
  const table = await screen.findByRole('table', { name: '人員' });
  const button = await screen.findByRole('button', { name: '範例成員 的動作' });
  await view.user.click(button);
  const menu = await screen.findByRole('menu', { name: '範例成員 的動作' });
  await view.user.click(within(menu).getByRole('menuitem', { name: '編輯職稱…' }));
  const dialog = await screen.findByRole('dialog', { name: TITLE });
  return { ...view, backend, table, button, dialog };
}

describe('人員頁：職稱與負責範圍的顯示', () => {
  it('名稱下面用次要文字寫「職稱 · 負責範圍」，只有職稱時只寫職稱，兩個都沒有就不畫', async () => {
    // Arrange
    signedInAs(exampleOwner);
    servePeopleAdmin({ actor: exampleOwner, people: [ownerPerson, designer, { ...adminPerson, jobTitle: '專案經理' }] });

    // Act
    renderApp('/people');

    // Assert
    const table = await screen.findByRole('table', { name: '人員' });
    expect(within(within(table).getByRole('row', { name: /範例成員/ })).getByText('前端工程師 · WordPress 維運、範例客戶的網站')).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /範例管理員/ })).getByText('專案經理')).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /範例使用者/ })).queryByText(/·/)).not.toBeInTheDocument();
  });
});

describe('人員頁：編輯職稱', () => {
  it('對話框帶出目前的職稱與負責範圍', async () => {
    // Arrange & Act
    const { dialog } = await openEditor(exampleOwner, [ownerPerson, designer]);

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '職稱' })).toHaveValue('前端工程師');
    expect(within(dialog).getByRole('textbox', { name: '負責範圍' })).toHaveValue('WordPress 維運、範例客戶的網站');
  });

  it('送出之後對話框關上、那一列換成新的職稱，頁面上方說明結果，焦點回到動作按鈕', async () => {
    // Arrange
    const { user, dialog, table, button, backend } = await openEditor();

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '職稱' }), '  後端工程師 ');
    await user.type(within(dialog).getByRole('textbox', { name: '負責範圍' }), '資料庫');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.requests).toEqual([
      { action: 'profile', personId: memberPerson.personId, body: { jobTitle: '後端工程師', responsibilities: '資料庫' } },
    ]);
    expect(await within(within(table).getByRole('row', { name: /範例成員/ })).findByText('後端工程師 · 資料庫')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('已更新「範例成員」的職稱與負責範圍。');
    expect(button).toHaveFocus();
  });

  it('兩個欄位都清空送出 null（清掉職稱）', async () => {
    // Arrange
    const { user, dialog, backend } = await openEditor(exampleOwner, [ownerPerson, designer]);

    // Act
    await user.clear(within(dialog).getByRole('textbox', { name: '職稱' }));
    await user.clear(within(dialog).getByRole('textbox', { name: '負責範圍' }));
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.requests).toEqual([{ action: 'profile', personId: memberPerson.personId, body: { jobTitle: null, responsibilities: null } }]);
  });

  it('伺服器說某個欄位有問題：訊息標在那個欄位下面，對話框留著，焦點到那個欄位', async () => {
    // Arrange
    const { user, dialog } = await openEditor();
    server.use(
      http.put('/v1/people/:personId/profile', () =>
        failure(422, 'person.profile_invalid', '負責範圍不能有換行、控制字元或看不見的字元。', [
          { code: 'person.profile_invalid', path: 'responsibilities', message: '負責範圍不能有換行、控制字元或看不見的字元。' },
        ]),
      ),
    );

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '負責範圍' }), '壞字');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    const field = within(dialog).getByRole('textbox', { name: '負責範圍' });
    await waitFor(() => {
      expect(field).toHaveAccessibleDescription(expect.stringContaining('負責範圍不能有換行'));
    });
    expect(field).toHaveFocus();
    expect(screen.getByRole('dialog', { name: TITLE })).toBeInTheDocument();
  });

  it('沒有指到欄位的錯誤（403）寫在表單上方', async () => {
    // Arrange
    const { user, dialog } = await openEditor();
    server.use(http.put('/v1/people/:personId/profile', () => failure(403, 'person.forbidden', '只有組織的擁有者或管理員可以設定職稱與負責範圍。')));

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '職稱' }), '工程師');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('只有組織的擁有者或管理員可以設定職稱與負責範圍。');
  });

  it('管理員也能改自己與擁有者的職稱', async () => {
    // Arrange
    const orgAdmin: SessionUser = { ...exampleOwner, personId: adminPerson.personId, displayName: adminPerson.displayName, role: 'admin' };
    signedInAs(orgAdmin);
    servePeopleAdmin({ actor: orgAdmin, people: [ownerPerson, adminPerson, memberPerson] });
    const { user } = renderApp('/people');

    // Act
    await user.click(await screen.findByRole('button', { name: '範例管理員 的動作' }));
    const menu = await screen.findByRole('menu', { name: '範例管理員 的動作' });

    // Assert
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['編輯職稱…']);
  });

  it('一般成員沒有編輯職稱的入口', async () => {
    // Arrange
    signedInAs(exampleMember);
    servePeopleAdmin({ actor: exampleMember, people: [ownerPerson, memberPerson] });

    // Act
    renderApp('/people');

    // Assert
    const table = await screen.findByRole('table', { name: '人員' });
    expect(within(table).queryByRole('button')).not.toBeInTheDocument();
  });
});
