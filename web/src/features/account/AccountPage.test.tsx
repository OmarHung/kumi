import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { SessionUser } from '../../api/types';
import { rateLimitPasswordChange, serveAccount } from '../../test/account';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const CURRENT = 'example-pass-1';

async function openAccount(user: SessionUser = exampleOwner, otherSessions = 0) {
  signedInAs(user);
  const backend = serveAccount({ currentPassword: CURRENT, otherSessions });
  const view = renderApp('/account');
  const form = await screen.findByRole('region', { name: '密碼' });
  return { ...view, backend, form };
}

async function fillPasswords(user: ReturnType<typeof renderApp>['user'], form: HTMLElement, current: string, next: string) {
  if (current !== '') await user.type(within(form).getByLabelText('目前的密碼'), current);
  if (next !== '') await user.type(within(form).getByLabelText('新密碼'), next);
}

describe('帳號選單：我的帳號', () => {
  it('在主題那一組前面；選了之後到「我的帳號」頁', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const { user, router } = renderApp('/my-work');
    await user.click(await screen.findByRole('button', { name: '範例使用者（範例組織 · 擁有者）' }));
    const menu = screen.getByRole('menu', { name: '帳號' });

    // Act
    const labels = Array.from(menu.querySelectorAll('[role^="menuitem"]')).map((item) => item.textContent);
    await user.click(within(menu).getByRole('menuitem', { name: '我的帳號' }));

    // Assert
    expect(labels.slice(0, 2)).toEqual(['我的帳號', '主題跟著系統']);
    expect(await screen.findByRole('heading', { level: 1, name: '我的帳號' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/account');
  });
});

describe('我的帳號：基本資料', () => {
  it('名稱、Email、組織、組織角色只能看，說明目前還不能改；這一塊是第一筆內容', async () => {
    // Arrange
    const user: SessionUser = { ...exampleOwner, role: 'admin' };

    // Act
    await openAccount(user);

    // Assert
    const profile = screen.getByRole('region', { name: '基本資料' });
    const facts = within(profile).getAllByRole('term').map((term) => [term.textContent, term.nextElementSibling?.textContent]);
    expect(facts).toEqual([
      ['名稱', '範例使用者'],
      ['Email', 'user@example.com'],
      ['組織', '範例組織'],
      ['組織角色', '管理員'],
    ]);
    expect(within(profile).getByText('名稱與 Email 目前還不能修改。')).toBeInTheDocument();
    expect(within(profile).queryByRole('textbox')).not.toBeInTheDocument();
    expect(profile).toHaveAttribute('data-content-start');
    expect(document.title).toContain('我的帳號');
  });
});

describe('我的帳號：改密碼', () => {
  it('說明裡的連結指到「命令列與整合」（存取權杖在那裡撤銷）', async () => {
    // Arrange
    // Act
    await openAccount();

    // Assert
    const section = screen.getByRole('region', { name: '密碼' });
    expect(within(section).getByText(/改密碼不會撤銷存取權杖/)).toHaveTextContent('到「命令列與整合」撤銷');
    expect(within(section).getByRole('link', { name: '「命令列與整合」' })).toHaveAttribute('href', '/account/integrations');
  });

  it('成功時清空欄位，說明其他裝置上的登入都登出了（幾個）', async () => {
    // Arrange
    const { user, form, backend } = await openAccount(exampleOwner, 2);
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    expect(await within(form).findByText('密碼已更新。其他裝置上的登入都登出了（2 個）。')).toBeInTheDocument();
    expect(backend.passwordChanges).toEqual([{ currentPassword: CURRENT, newPassword: 'example-pass-2' }]);
    expect(within(form).getByLabelText('目前的密碼')).toHaveValue('');
    expect(within(form).getByLabelText('新密碼')).toHaveValue('');
  });

  it('沒有其他登入時只說「密碼已更新。」', async () => {
    // Arrange
    const { user, form } = await openAccount(exampleOwner, 0);
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    const status = within(form).getByRole('status');
    await waitFor(() => {
      expect(status).toHaveTextContent(/^密碼已更新。$/);
    });
  });

  it('新密碼的提示是「至少 10 個字元」；兩個欄位都可以切換顯示，按鈕的名稱帶上欄位', async () => {
    // Arrange
    const { user, form } = await openAccount();
    const next = within(form).getByLabelText('新密碼');

    // Act
    await user.click(within(form).getByRole('button', { name: '顯示目前的密碼' }));

    // Assert
    expect(next).toHaveAccessibleDescription('至少 10 個字元');
    expect(within(form).getByLabelText('目前的密碼')).toHaveAttribute('type', 'text');
    expect(next).toHaveAttribute('type', 'password');
    expect(within(form).getByRole('button', { name: '顯示新密碼' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('沒填目前的密碼、新密碼太短時在欄位旁說明，焦點到第一個有問題的欄位，不送出', async () => {
    // Arrange
    const { user, form, backend } = await openAccount();
    await fillPasswords(user, form, '', 'short');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    const current = within(form).getByLabelText('目前的密碼');
    expect(current).toHaveAccessibleDescription('請輸入目前的密碼。');
    expect(current).toHaveFocus();
    expect(within(form).getByLabelText('新密碼')).toHaveAccessibleDescription(/新密碼至少要 10 個字元。/);
    await flushPending();
    expect(backend.passwordChanges).toEqual([]);
  });

  it('目前的密碼不對（細項路徑 currentPassword）：錯誤標在「目前的密碼」，焦點過去，打的字留著', async () => {
    // Arrange
    const { user, form } = await openAccount();
    await fillPasswords(user, form, 'wrong-password', 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    const current = within(form).getByLabelText('目前的密碼');
    await waitFor(() => {
      expect(current).toHaveAccessibleDescription('目前的密碼不正確。');
    });
    expect(current).toHaveFocus();
    expect(within(form).getByLabelText('新密碼')).toHaveValue('example-pass-2');
  });

  it('伺服器一次回兩個細項（目前的密碼不正確、新密碼太長）時兩個欄位一起標，焦點到「目前的密碼」', async () => {
    // Arrange
    const { user, form } = await openAccount();
    // 前端只擋 128 個字元以上（輸入框的 maxLength），這裡用伺服器自己的規則（例如之後改得更嚴）回兩個細項。
    server.use(
      http.put('/v1/account/password', () =>
        failure(422, 'account.password_incorrect', '目前的密碼不正確。', [
          { code: 'account.password_incorrect', path: 'currentPassword', message: '目前的密碼不正確。' },
          { code: 'person.password_too_long', path: 'newPassword', message: '密碼不能超過 64 個字元。' },
        ]),
      ),
    );
    await fillPasswords(user, form, 'wrong-password', 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    const current = within(form).getByLabelText('目前的密碼');
    await waitFor(() => {
      expect(current).toHaveAccessibleDescription('目前的密碼不正確。');
    });
    expect(within(form).getByLabelText('新密碼')).toHaveAccessibleDescription(/密碼不能超過 64 個字元。/);
    expect(current).toHaveFocus();
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('伺服器說新密碼太短（細項路徑 newPassword）時標在「新密碼」', async () => {
    // Arrange
    const { user, form } = await openAccount();
    server.use(
      http.put('/v1/account/password', () =>
        failure(422, 'person.password_too_short', '密碼至少要 12 個字元。', [
          { code: 'person.password_too_short', path: 'newPassword', message: '密碼至少要 12 個字元。' },
        ]),
      ),
    );
    await fillPasswords(user, form, CURRENT, 'example-pass');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    const next = within(form).getByLabelText('新密碼');
    await waitFor(() => {
      expect(next).toHaveAccessibleDescription(/密碼至少要 12 個字元。/);
    });
    expect(next).toHaveFocus();
  });

  it('嘗試太多次（429）時在表單下面說明', async () => {
    // Arrange
    const { user, form } = await openAccount();
    rateLimitPasswordChange();
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    expect(await within(form).findByRole('alert')).toHaveTextContent('嘗試太多次，請稍後再試。');
  });

  it('送出中按鈕寫「更新中…」並標 aria-disabled（焦點留在上面），再按不重送', async () => {
    // Arrange
    const { user, form } = await openAccount();
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    let puts = 0;
    server.use(
      http.put('/v1/account/password', async () => {
        puts += 1;
        await released;
        return ok({ signedOutSessions: 0 });
      }),
    );
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));
    const pending = await within(form).findByRole('button', { name: '更新中…' });
    await user.click(pending);

    // Assert
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    await flushPending();
    expect(puts).toBe(1);
    release();
    expect(await within(form).findByText('密碼已更新。')).toBeInTheDocument();
  });
});

describe('我的帳號：登出其他裝置', () => {
  it('不用確認，結果寫「已登出其他 N 個裝置。」', async () => {
    // Arrange
    const { user, backend } = await openAccount(exampleOwner, 3);
    const section = screen.getByRole('region', { name: '其他裝置' });
    const button = within(section).getByRole('button', { name: '登出其他裝置' });

    // Act
    await user.click(button);

    // Assert
    expect(await within(section).findByText('已登出其他 3 個裝置。')).toBeInTheDocument();
    expect(backend.signOutOthers()).toBe(1);
    expect(button).toHaveAccessibleDescription('在別的電腦或瀏覽器登入過、忘了登出時用。');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('沒有別的登入時寫「沒有其他登入中的裝置。」', async () => {
    // Arrange
    const { user } = await openAccount(exampleOwner, 0);
    const section = screen.getByRole('region', { name: '其他裝置' });

    // Act
    await user.click(within(section).getByRole('button', { name: '登出其他裝置' }));

    // Assert
    expect(await within(section).findByText('沒有其他登入中的裝置。')).toBeInTheDocument();
  });

  it('送出中寫「登出中…」並標 aria-disabled，焦點留在按鈕上、再按不重送', async () => {
    // Arrange
    const { user, backend } = await openAccount(exampleOwner, 1);
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    let deletes = 0;
    server.use(
      http.delete('/v1/account/other-sessions', async () => {
        deletes += 1;
        await released;
        return ok({ signedOutSessions: 1 });
      }),
    );
    const section = screen.getByRole('region', { name: '其他裝置' });
    await user.click(within(section).getByRole('button', { name: '登出其他裝置' }));

    // Act
    const pending = await within(section).findByRole('button', { name: '登出中…' });
    await user.click(pending);

    // Assert
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    await flushPending();
    expect(deletes).toBe(1);
    expect(backend.signOutOthers()).toBe(0);
    release();
    expect(await within(section).findByText('已登出其他 1 個裝置。')).toBeInTheDocument();
  });

  it('失敗時在按鈕下面說明', async () => {
    // Arrange
    const { user } = await openAccount();
    server.use(http.delete('/v1/account/other-sessions', () => failure(503, 'server.not_ready', '服務正在啟動。')));
    const section = screen.getByRole('region', { name: '其他裝置' });

    // Act
    await user.click(within(section).getByRole('button', { name: '登出其他裝置' }));

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');
  });
});
