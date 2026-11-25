import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { SessionUser } from '../../api/types';
import { rateLimitPasswordChange, serveAccount } from '../../test/account';
import { failure, ok, signedInAs } from '../../test/api';
import { queryKeys } from '../../api/queryKeys';
import { exampleOwner } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const CURRENT = 'example-pass-1';

async function openAccount(user: SessionUser = exampleOwner, otherSessions = 0, accessTokens = 0) {
  signedInAs(user);
  const backend = serveAccount({ currentPassword: CURRENT, otherSessions, accessTokens });
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
    expect(within(section).getByText(/存取權杖預設一併撤銷/)).toHaveTextContent('到「命令列與整合」自己管理');
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
    expect(backend.passwordChanges).toEqual([{ currentPassword: CURRENT, newPassword: 'example-pass-2', revokeAccessTokens: true }]);
    expect(within(form).getByLabelText('目前的密碼')).toHaveValue('');
    expect(within(form).getByLabelText('新密碼')).toHaveValue('');
  });

  it('成功之後權杖、AI 用戶端授權、本機代理的清單都標成過期，之後打開會重抓（不顯示已撤銷的）', async () => {
    // Arrange
    const { user, form, backend, queryClient } = await openAccount(exampleOwner, 1, 2);
    const keys = [queryKeys.accessTokens, queryKeys.oauthGrants, queryKeys.localAgents];
    keys.forEach((key) => {
      queryClient.setQueryData(key, []);
    });
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    await within(form).findByText(/存取權杖撤銷了 2 把/);
    expect(backend.passwordChanges).toHaveLength(1);
    keys.forEach((key) => {
      expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
    });
  });

  it('「同時撤銷所有存取權杖」預設勾著，送出時帶 revokeAccessTokens: true，成功訊息寫出撤銷幾把並提醒重新執行 kumi setup', async () => {
    // Arrange
    const { user, form, backend } = await openAccount(exampleOwner, 1, 3);
    const checkbox = within(form).getByRole('checkbox', { name: '同時撤銷所有存取權杖（命令列、CI、本機代理）' });
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    expect(checkbox).toBeChecked();
    expect(checkbox).toHaveAccessibleDescription('那些工具之後要重新登入（kumi setup）或重建權杖。');
    const status = within(form).getByRole('status');
    await waitFor(() => {
      expect(status).toHaveTextContent(
        '密碼已更新。其他裝置上的登入都登出了（1 個）。存取權杖撤銷了 3 把，用到它們的工具要重新登入：請再執行一次 kumi setup。',
      );
    });
    expect(backend.passwordChanges).toEqual([{ currentPassword: CURRENT, newPassword: 'example-pass-2', revokeAccessTokens: true }]);
  });

  it('取消勾選時送出 revokeAccessTokens: false，成功訊息不提權杖；成功之後勾選的狀態留著', async () => {
    // Arrange
    const { user, form, backend } = await openAccount(exampleOwner, 0, 3);
    const checkbox = within(form).getByRole('checkbox', { name: /同時撤銷所有存取權杖/ });
    await user.click(checkbox);
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    await waitFor(() => {
      expect(within(form).getByRole('status')).toHaveTextContent(/^密碼已更新。$/);
    });
    expect(backend.passwordChanges).toEqual([{ currentPassword: CURRENT, newPassword: 'example-pass-2', revokeAccessTokens: false }]);
    expect(checkbox).not.toBeChecked();
  });

  it('沒有權杖可撤銷時（0 把）不提重新登入', async () => {
    // Arrange
    const { user, form } = await openAccount(exampleOwner, 2, 0);
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    const status = within(form).getByRole('status');
    await waitFor(() => {
      expect(status).toHaveTextContent(/^密碼已更新。其他裝置上的登入都登出了（2 個）。$/);
    });
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

  it('嘗試太多次（429）帶 Retry-After 時寫出要等幾秒', async () => {
    // Arrange
    const { user, form } = await openAccount();
    server.use(
      http.put('/v1/account/password', () =>
        HttpResponse.json(
          { success: false, data: null, error: { code: 'request.rate_limited', message: 'Too many requests.', details: [] } },
          { status: 429, headers: { 'Retry-After': '30' } },
        ),
      ),
    );
    await fillPasswords(user, form, CURRENT, 'example-pass-2');

    // Act
    await user.click(within(form).getByRole('button', { name: '更新密碼' }));

    // Assert
    expect(await within(form).findByRole('alert')).toHaveTextContent('嘗試太多次，請在 30 秒後再試。');
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
        return ok({ signedOutSessions: 0, revokedAccessTokens: 0 });
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
  const DESCRIPTION =
    '在別的電腦或瀏覽器登入過、忘了登出時用。登出其他裝置會結束你在別處的登入，所以要確認是你本人。只登出網頁的登入，不會撤銷存取權杖；權杖到「命令列與整合」撤銷。';

  async function signOutWith(user: ReturnType<typeof renderApp>['user'], section: HTMLElement, password: string) {
    await user.type(within(section).getByLabelText('目前的密碼'), password);
    await user.click(within(section).getByRole('button', { name: '登出其他裝置' }));
  }

  it('輸入目前的密碼之後登出，結果寫「已登出其他 N 個裝置。」並清空欄位，不用確認', async () => {
    // Arrange
    const { user, backend } = await openAccount(exampleOwner, 3);
    const section = screen.getByRole('region', { name: '其他裝置' });
    const field = within(section).getByLabelText('目前的密碼');

    // Act
    await signOutWith(user, section, CURRENT);

    // Assert
    expect(await within(section).findByText('已登出其他 3 個裝置。')).toBeInTheDocument();
    expect(backend.signOutOthers()).toBe(1);
    expect(backend.signOutBodies).toEqual([{ currentPassword: CURRENT }]);
    expect(field).toHaveValue('');
    expect(field).toHaveAttribute('autocomplete', 'current-password');
    expect(within(section).getByRole('button', { name: '登出其他裝置' })).toHaveAccessibleDescription(DESCRIPTION);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('沒填密碼時按鈕用 aria-disabled，按了不送出', async () => {
    // Arrange
    const { user, backend } = await openAccount(exampleOwner, 2);
    const section = screen.getByRole('region', { name: '其他裝置' });
    const button = within(section).getByRole('button', { name: '登出其他裝置' });

    // Act
    await user.click(button);

    // Assert
    expect(button).toHaveAttribute('aria-disabled', 'true');
    await flushPending();
    expect(backend.signOutBodies).toEqual([]);
  });

  it('密碼不對時標在欄位上，不清空，焦點回到欄位', async () => {
    // Arrange
    const { user, backend } = await openAccount(exampleOwner, 2);
    const section = screen.getByRole('region', { name: '其他裝置' });

    // Act
    await signOutWith(user, section, 'wrong-pass');

    // Assert
    const field = within(section).getByLabelText('目前的密碼');
    expect(await within(section).findByText('目前的密碼不正確。')).toBeInTheDocument();
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(field).toHaveValue('wrong-pass');
    expect(field).toHaveFocus();
    expect(backend.signOutOthers()).toBe(0);
  });

  it('沒有別的登入時寫「沒有其他登入中的裝置。」', async () => {
    // Arrange
    const { user } = await openAccount(exampleOwner, 0);
    const section = screen.getByRole('region', { name: '其他裝置' });

    // Act
    await signOutWith(user, section, CURRENT);

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
    let posts = 0;
    server.use(
      http.post('/v1/account/other-sessions/sign-out', async () => {
        posts += 1;
        await released;
        return ok({ signedOutSessions: 1 });
      }),
    );
    const section = screen.getByRole('region', { name: '其他裝置' });
    await signOutWith(user, section, CURRENT);

    // Act
    const pending = await within(section).findByRole('button', { name: '登出中…' });
    await user.click(pending);

    // Assert
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    await flushPending();
    expect(posts).toBe(1);
    expect(backend.signOutOthers()).toBe(0);
    release();
    expect(await within(section).findByText('已登出其他 1 個裝置。')).toBeInTheDocument();
  });

  it('嘗試太多次（429）時說明要等幾秒', async () => {
    // Arrange
    const { user } = await openAccount();
    server.use(
      http.post('/v1/account/other-sessions/sign-out', () =>
        HttpResponse.json(
          { code: 'request.rate_limited', message: 'Too many requests.', issues: [] },
          { status: 429, headers: { 'Retry-After': '30' } },
        ),
      ),
    );
    const section = screen.getByRole('region', { name: '其他裝置' });

    // Act
    await signOutWith(user, section, CURRENT);

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('嘗試太多次，請在 30 秒後再試。');
  });

  it('失敗時在按鈕下面說明', async () => {
    // Arrange
    const { user } = await openAccount();
    server.use(http.post('/v1/account/other-sessions/sign-out', () => failure(503, 'server.not_ready', '服務正在啟動。')));
    const section = screen.getByRole('region', { name: '其他裝置' });

    // Act
    await signOutWith(user, section, CURRENT);

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');
  });
});
