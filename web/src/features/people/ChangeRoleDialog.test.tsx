import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Person, SessionUser } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { adminPerson, guestPerson, memberPerson, ownerPerson, servePeopleAdmin } from '../../test/people';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const TITLE = '變更「範例成員」的角色';

const orgAdmin: SessionUser = {
  ...exampleOwner,
  personId: adminPerson.personId,
  displayName: adminPerson.displayName,
  email: 'admin@example.com',
  role: 'admin',
};

async function openChangeRole(actor: SessionUser = exampleOwner, people: readonly Person[] = [ownerPerson, memberPerson]) {
  signedInAs(actor);
  const backend = servePeopleAdmin({ actor, people });
  const view = renderApp('/people');
  const table = await screen.findByRole('table', { name: '人員' });
  const button = await screen.findByRole('button', { name: '範例成員 的動作' });
  await view.user.click(button);
  const menu = await screen.findByRole('menu', { name: '範例成員 的動作' });
  await view.user.click(within(menu).getByRole('menuitem', { name: '變更角色…' }));
  const dialog = await screen.findByRole('dialog', { name: TITLE });
  const select = within(dialog).getByRole('combobox', { name: '組織角色' });
  return { ...view, backend, table, button, dialog, select };
}

describe('人員頁：變更角色', () => {
  it('對話框的下拉是目前的角色，下面一句說明寫出選到的角色能做什麼', async () => {
    // Arrange & Act
    const { select, dialog } = await openChangeRole();

    // Assert
    expect(select).toHaveTextContent('成員');
    expect(select).toHaveAccessibleDescription('一般成員：看得到被加入的專案，在裡面照專案的角色工作。');
    expect(within(dialog).getByRole('button', { name: '儲存' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: '取消' })).toBeInTheDocument();
  });

  it('擁有者把成員改成管理員：送出之後對話框關上、那一列換成新的角色，頁面上方說明結果，焦點回到動作按鈕', async () => {
    // Arrange
    const { user, dialog, select, table, button, backend } = await openChangeRole();

    // Act
    await chooseOption(user, select, '管理員');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.requests).toEqual([{ action: 'role', personId: memberPerson.personId, body: { role: 'admin' } }]);
    expect(await within(within(table).getByRole('row', { name: /範例成員/ })).findByText('管理員')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('已把「範例成員」的組織角色改成「管理員」。');
    expect(button).toHaveFocus();
  });

  it('管理員看到的下拉：擁有者與管理員是不能選的選項並說明原因，成員與訪客照樣選得到', async () => {
    // Arrange
    const { user, select, backend, dialog } = await openChangeRole(orgAdmin, [ownerPerson, adminPerson, memberPerson, guestPerson]);

    // Act
    const options = await openOptions(user, select);
    const byValue = (value: string) => options.find((option) => option.dataset.value === value);
    await user.click(byValue('admin') ?? select);

    // Assert
    expect(options.map((option) => option.dataset.value)).toEqual(['owner', 'admin', 'member', 'guest']);
    expect(byValue('owner')).toHaveAttribute('aria-disabled', 'true');
    expect(byValue('owner')).toHaveTextContent('只有擁有者能設定擁有者');
    expect(byValue('admin')).toHaveAttribute('aria-disabled', 'true');
    expect(byValue('admin')).toHaveTextContent('只有擁有者能設定管理員');
    expect(byValue('member')).not.toHaveAttribute('aria-disabled');
    expect(byValue('guest')).not.toHaveAttribute('aria-disabled');
    // 點不能選的選項：值不變、什麼都沒送。
    expect(select).toHaveTextContent('成員');
    await flushPending();
    expect(backend.requests).toEqual([]);
    expect(dialog).toBeInTheDocument();
  });

  it('管理員打開時，下拉下面多一句說明擁有者與管理員只有擁有者能設定；擁有者沒有這句', async () => {
    // Arrange & Act
    const admin = await openChangeRole(orgAdmin, [ownerPerson, adminPerson, memberPerson]);

    // Assert
    expect(admin.select).toHaveAccessibleDescription('一般成員：看得到被加入的專案，在裡面照專案的角色工作。擁有者與管理員只有擁有者能設定。');
  });

  it('用鍵盤在選單裡對不能選的選項按 Enter：選不到，值不變、什麼都沒送', async () => {
    // Arrange
    const { user, select, backend, dialog } = await openChangeRole(orgAdmin, [ownerPerson, adminPerson, memberPerson, guestPerson]);
    await openOptions(user, select);

    // Act
    await user.keyboard('{ArrowUp}');
    await user.keyboard('{Enter}');

    // Assert
    expect(select).toHaveTextContent('成員');
    expect(dialog).toBeInTheDocument();
    await flushPending();
    expect(backend.requests).toEqual([]);
  });

  it('管理員把成員改成訪客', async () => {
    // Arrange
    const { user, dialog, select, backend } = await openChangeRole(orgAdmin, [ownerPerson, adminPerson, memberPerson]);

    // Act
    await chooseOption(user, select, '訪客');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('已把「範例成員」的組織角色改成「訪客」。')).toBeInTheDocument();
    expect(backend.requests).toEqual([{ action: 'role', personId: memberPerson.personId, body: { role: 'guest' } }]);
  });

  it('已經是那個角色時照常送出（伺服器照樣成功），說明結果', async () => {
    // Arrange
    const { user, dialog, backend } = await openChangeRole();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.requests).toEqual([{ action: 'role', personId: memberPerson.personId, body: { role: 'member' } }]);
    expect(screen.getByRole('status')).toHaveTextContent('「範例成員」的組織角色已經是「成員」。');
  });

  it('Esc 關上對話框，焦點回到動作按鈕，什麼都沒送', async () => {
    // Arrange
    const { user, button, backend } = await openChangeRole();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
    await flushPending();
    expect(backend.requests).toEqual([]);
  });

  it('送出中「儲存」寫「儲存中…」、標 aria-disabled（焦點留在上面，再按不重送），「取消」停用', async () => {
    // Arrange
    const { user, dialog, select, backend } = await openChangeRole();
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.put('/v1/people/:personId/role', async ({ params }) => {
        backend.requests.push({ action: 'role', personId: String(params.personId), body: undefined });
        await released;
        return ok({ ...memberPerson, role: 'guest' });
      }),
    );
    await chooseOption(user, select, '訪客');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));
    const pending = await within(dialog).findByRole('button', { name: '儲存中…' });
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

  it.each([
    [403, 'person.forbidden', '只有擁有者可以設定或變更擁有者、管理員的角色。'],
    [409, 'person.is_self', '不能變更自己的角色。'],
    [404, 'person.not_found', '組織裡找不到這個人。'],
  ])('被擋下來（%i）時對話框留著，寫出伺服器的說明，清單重抓', async (status, code, message) => {
    // Arrange
    const { user, dialog, select } = await openChangeRole();
    let listed = 0;
    let sessions = 0;
    server.use(
      http.get('/v1/session', () => {
        sessions += 1;
        return ok({ user: exampleOwner });
      }),
      http.put('/v1/people/:personId/role', () => failure(status, code, message)),
      http.get('/v1/people', () => {
        listed += 1;
        return ok([ownerPerson, memberPerson]);
      }),
    );
    await chooseOption(user, select, '訪客');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(message);
    const confirm = within(dialog).getByRole('button', { name: '儲存' });
    expect(confirm).not.toHaveAttribute('aria-disabled');
    expect(confirm).toHaveFocus();
    await waitFor(() => {
      expect(listed).toBe(1);
    });
    // 只有 403 把登入的人自己的資料也重抓（他的角色多半變了，選單的選項要跟著變）。
    if (code === 'person.forbidden') {
      await waitFor(() => {
        expect(sessions).toBe(1);
      });
    } else {
      await flushPending();
      expect(sessions).toBe(0);
    }
  });
});
