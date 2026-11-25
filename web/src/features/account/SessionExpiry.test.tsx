import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { serveAccount } from '../../test/account';
import { authRequired, sessionEndsAfterFirstCheck, signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { memberPerson, ownerPerson, servePeopleAdmin } from '../../test/people';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const EXPIRED = '登入已經失效，請重新登入。';

/** 回到登入頁，上方說明登入已經失效；登入之後回到原本的頁面（next）。 */
async function expectBackAtLogin(router: ReturnType<typeof renderApp>['router'], from: string) {
  expect(await screen.findByRole('heading', { level: 1, name: '登入' })).toBeInTheDocument();
  expect(screen.getByRole('status')).toHaveTextContent(EXPIRED);
  expect(currentPath(router)).toBe(`/login?next=${encodeURIComponent(from)}`);
}

async function openPeopleActions(name: string) {
  signedInAs(exampleOwner);
  sessionEndsAfterFirstCheck(exampleOwner);
  servePeopleAdmin({ actor: exampleOwner, people: [ownerPerson, memberPerson] });
  const view = renderApp('/people');
  await view.user.click(await screen.findByRole('button', { name: `${name} 的動作` }));
  const menu = await screen.findByRole('menu', { name: `${name} 的動作` });
  return { ...view, menu };
}

describe('帳號的操作時登入已經失效（401）', () => {
  it('我的帳號改密碼時收到 401：回到登入頁並說明登入已經失效', async () => {
    // Arrange
    signedInAs(exampleOwner);
    sessionEndsAfterFirstCheck(exampleOwner);
    serveAccount({ currentPassword: 'example-pass-1' });
    server.use(http.put('/v1/account/password', () => authRequired()));
    const { user, router } = renderApp('/account');
    const form = await screen.findByRole('region', { name: '密碼' });
    await user.type(within(form).getByLabelText('目前的密碼'), 'example-pass-1');
    await user.type(within(form).getByLabelText('新密碼'), 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    await expectBackAtLogin(router, '/account');
  });

  it('重設別人的密碼時收到 401：對話框跟著不見，回到登入頁並說明', async () => {
    // Arrange
    const { user, router, menu } = await openPeopleActions('範例成員');
    server.use(http.put('/v1/people/:personId/password', () => authRequired()));
    await user.click(within(menu).getByRole('menuitem', { name: '重設密碼…' }));
    const dialog = await screen.findByRole('dialog', { name: '重設「範例成員」的密碼' });
    await user.type(within(dialog).getByLabelText('新密碼'), 'example-pass-2');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '重設密碼' }));

    // Assert
    await expectBackAtLogin(router, '/people');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('停用帳號時收到 401：回到登入頁並說明', async () => {
    // Arrange
    const { user, router, menu } = await openPeopleActions('範例成員');
    server.use(http.post('/v1/people/:personId/disable', () => authRequired()));
    await user.click(within(menu).getByRole('menuitem', { name: '停用帳號…' }));
    const dialog = await screen.findByRole('dialog', { name: '停用「範例成員」的帳號？' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '停用帳號' }));

    // Assert
    await expectBackAtLogin(router, '/people');
  });
});
