import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Person } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { memberPerson, ownerPerson, servePeopleAdmin } from '../../test/people';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const TITLE = '重設「範例成員」的密碼';

async function openResetDialog(options: { readonly withoutAccount?: readonly string[]; readonly people?: readonly Person[] } = {}) {
  signedInAs(exampleOwner);
  const backend = servePeopleAdmin({ actor: exampleOwner, people: options.people ?? [ownerPerson, memberPerson], withoutAccount: options.withoutAccount });
  const view = renderApp('/people');
  const button = await screen.findByRole('button', { name: '範例成員 的動作' });
  await view.user.click(button);
  const menu = await screen.findByRole('menu', { name: '範例成員 的動作' });
  await view.user.click(within(menu).getByRole('menuitem', { name: '重設密碼…' }));
  const dialog = await screen.findByRole('dialog', { name: TITLE });
  return { ...view, backend, button, dialog };
}

describe('人員頁：重設密碼', () => {
  it('打開時焦點在「新密碼」；提示寫出長度與後果，可以切換顯示密碼', async () => {
    // Arrange
    const { user, dialog } = await openResetDialog();
    const input = within(dialog).getByLabelText('新密碼');
    // 打開時焦點就在欄位上（打字之前先確認，user.type 會自己點一下欄位）。
    expect(input).toHaveFocus();

    // Act
    await user.type(input, 'example-pass-2');
    await user.click(within(dialog).getByRole('button', { name: '顯示新密碼' }));

    // Assert
    expect(input).toHaveAccessibleDescription(
      '至少 10 個字元。重設之後，他在每個裝置上的登入都會登出，請親自把新密碼交給他。',
    );
    expect(input).toHaveAttribute('type', 'text');
    expect(input).toHaveValue('example-pass-2');
  });

  it('送出之後對話框關上，頁面上方說明結果，焦點回到動作按鈕', async () => {
    // Arrange
    const { user, dialog, backend, button } = await openResetDialog();

    // Act
    await user.type(within(dialog).getByLabelText('新密碼'), 'example-pass-2');
    await user.click(within(dialog).getByRole('button', { name: '重設密碼' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.requests).toEqual([{ action: 'password', personId: memberPerson.personId, body: { newPassword: 'example-pass-2' } }]);
    expect(screen.getByRole('status')).toHaveTextContent('已重設「範例成員」的密碼，他的所有登入都登出了。');
    expect(button).toHaveFocus();
  });

  it('在欄位裡按 Enter 也會送出', async () => {
    // Arrange
    const { user, dialog, backend } = await openResetDialog();

    // Act
    await user.type(within(dialog).getByLabelText('新密碼'), 'example-pass-2{Enter}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(backend.requests).toHaveLength(1);
  });

  it('太短時在欄位旁說明、焦點留在欄位，不送出', async () => {
    // Arrange
    const { user, dialog, backend } = await openResetDialog();
    const input = within(dialog).getByLabelText('新密碼');

    // Act
    await user.type(input, 'short');
    await user.click(within(dialog).getByRole('button', { name: '重設密碼' }));

    // Assert
    expect(input).toHaveAccessibleDescription(/新密碼至少要 10 個字元。/);
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveFocus();
    await flushPending();
    expect(backend.requests).toEqual([]);
  });

  it('伺服器回的長度錯誤（細項路徑 newPassword）標在欄位上，對話框留著', async () => {
    // Arrange
    const { user, dialog } = await openResetDialog();
    server.use(
      http.put('/v1/people/:personId/password', () =>
        failure(422, 'person.password_too_short', '密碼至少要 12 個字元。', [
          { code: 'person.password_too_short', path: 'newPassword', message: '密碼至少要 12 個字元。' },
        ]),
      ),
    );
    const input = within(dialog).getByLabelText('新密碼');

    // Act
    await user.type(input, 'example-pass');
    await user.click(within(dialog).getByRole('button', { name: '重設密碼' }));

    // Assert
    await waitFor(() => {
      expect(input).toHaveAccessibleDescription(/密碼至少要 12 個字元。/);
    });
    expect(input).toHaveFocus();
    expect(screen.getByRole('dialog', { name: TITLE })).toBeInTheDocument();
  });

  it('這個人沒有登入帳號（409）時，對話框留著、錯誤寫在對話框裡', async () => {
    // Arrange
    const { user, dialog } = await openResetDialog({ withoutAccount: [memberPerson.personId] });

    // Act
    await user.type(within(dialog).getByLabelText('新密碼'), 'example-pass-2');
    await user.click(within(dialog).getByRole('button', { name: '重設密碼' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('這個人沒有登入帳號。');
    expect(within(dialog).getByLabelText('新密碼')).toHaveValue('example-pass-2');
  });

  it('送出中「重設密碼」標 aria-disabled（焦點不掉）、「取消」停用，再按不重送', async () => {
    // Arrange
    const { user, dialog } = await openResetDialog();
    let release = (): void => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    let puts = 0;
    server.use(
      http.put('/v1/people/:personId/password', async () => {
        puts += 1;
        await released;
        return ok(memberPerson);
      }),
    );
    await user.type(within(dialog).getByLabelText('新密碼'), 'example-pass-2');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '重設密碼' }));
    const pending = await within(dialog).findByRole('button', { name: '重設中…' });
    await user.click(pending);

    // Assert
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    expect(within(dialog).getByRole('button', { name: '取消' })).toBeDisabled();
    await flushPending();
    expect(puts).toBe(1);
    release();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('Esc 關上對話框，焦點回到動作按鈕，什麼都沒送', async () => {
    // Arrange
    const { user, dialog, backend, button } = await openResetDialog();
    await user.type(within(dialog).getByLabelText('新密碼'), 'example-pass-2');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
    await flushPending();
    expect(backend.requests).toEqual([]);
  });
});
