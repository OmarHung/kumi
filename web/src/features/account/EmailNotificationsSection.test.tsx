import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { failure, ok, signedInAs } from '../../test/api';
import { serveEmailPreference } from '../../test/emailSettings';
import { exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

async function openSection(initial = { enabled: true, organizationEnabled: false }) {
  signedInAs(exampleOwner);
  const backend = serveEmailPreference(initial);
  const view = renderApp('/account/notifications');
  const section = await screen.findByRole('region', { name: 'Email 通知' });
  const checkbox = await within(section).findByRole('checkbox', { name: '有新通知時寄 Email 到 user@example.com' });
  return { ...view, backend, section, checkbox };
}

describe('我的帳號：Email 通知', () => {
  it('勾選框照目前的設定；組織還沒開寄信時說明，勾選框照樣能改', async () => {
    // Arrange / Act
    const { section, checkbox } = await openSection({ enabled: true, organizationEnabled: false });

    // Assert
    expect(checkbox).toBeChecked();
    expect(checkbox).toBeEnabled();
    expect(within(section).getByText('組織還沒開啟寄信，開啟之後才會寄。')).toBeInTheDocument();
  });

  it('組織開了寄信：沒有那一行說明', async () => {
    // Arrange / Act
    const { section } = await openSection({ enabled: false, organizationEnabled: true });

    // Assert
    expect(within(section).queryByText(/組織還沒開啟寄信/)).not.toBeInTheDocument();
  });

  it('按了就存：立刻換成新的狀態，送出 enabled', async () => {
    // Arrange
    const { user, backend, checkbox } = await openSection();

    // Act
    await user.click(checkbox);

    // Assert
    await waitFor(() => {
      expect(checkbox).not.toBeChecked();
    });
    await waitFor(() => {
      expect(backend.puts).toEqual([false]);
    });
    expect(backend.current().enabled).toBe(false);
  });

  it('存失敗：退回原來的狀態並說明原因', async () => {
    // Arrange
    const { user, section, backend, checkbox } = await openSection();
    backend.rejectNext({ status: 422, code: 'request.invalid', message: '本文不對。' });

    // Act
    await user.click(checkbox);

    // Assert
    expect(await within(section).findByText('本文不對。')).toBeInTheDocument();
    await waitFor(() => {
      expect(checkbox).toBeChecked();
    });
  });

  it('讀不到設定：說明失敗並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/account/email-notifications', () => failure(500, 'server.error', '伺服器出了問題。')));

    // Act
    renderApp('/account/notifications');

    // Assert
    const section = await screen.findByRole('region', { name: 'Email 通知' });
    expect(await within(section).findByText('Email 通知的設定載入失敗')).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '重試' })).toBeInTheDocument();
  });

  it('組織還沒開寄信的說明是勾選框的說明（連到勾選框）', async () => {
    // Arrange / Act
    const { checkbox } = await openSection({ enabled: true, organizationEnabled: false });

    // Assert
    expect(checkbox).toHaveAccessibleDescription('組織還沒開啟寄信，開啟之後才會寄。');
  });

  it('送出中再按不送第二次；回來之後才能再改', async () => {
    // Arrange
    const { user, checkbox } = await openSection();
    const requests: unknown[] = [];
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    let stored = true;
    server.use(
      http.get('/v1/account/email-notifications', () => ok({ enabled: stored, organizationEnabled: false })),
      http.put('/v1/account/email-notifications', async ({ request }) => {
        requests.push(await request.json());
        await gate;
        stored = false;
        return ok({ enabled: false, organizationEnabled: false });
      }),
    );

    // Act
    await user.click(checkbox);
    await waitFor(() => {
      expect(checkbox).toHaveAttribute('aria-disabled', 'true');
    });
    await user.click(checkbox);
    release();

    // Assert
    await waitFor(() => {
      expect(checkbox).not.toHaveAttribute('aria-disabled');
    });
    expect(requests).toEqual([{ enabled: false }]);
    expect(checkbox).not.toBeChecked();
  });
});
