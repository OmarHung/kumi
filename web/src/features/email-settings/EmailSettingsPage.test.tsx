import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import { forbidEmailSettings, serveEmailSettings } from '../../test/emailSettings';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { chooseOption } from '../../test/select';
import type { EmailSettings } from '../../api/emailSettings';

const SMTP_SAVED: Partial<EmailSettings> = {
  enabled: true,
  provider: 'smtp',
  host: 'smtp.example.com',
  port: 465,
  security: 'tls',
  username: 'mailer',
  hasPassword: true,
  fromAddress: 'noreply@example.com',
  fromName: 'Example 公司',
  siteUrl: 'https://pm.example.com',
  updatedAt: '2026-11-21T06:00:00+00:00',
  updatedBy: { personId: 'p-1', displayName: '範例擁有者' },
};

async function openPage(initial: Partial<EmailSettings> = {}) {
  signedInAs(exampleOwner);
  const backend = serveEmailSettings(initial);
  const view = renderApp('/email-settings');
  const save = await screen.findByRole('button', { name: '儲存' });
  return { ...view, backend, save };
}

describe('寄信設定：入口與權限', () => {
  it('在設定中心的次導覽裡是目前所在的一頁，頁列標題是「寄信設定」', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    const hubNav = screen.getByRole('navigation', { name: '設定' });
    expect(within(hubNav).getByRole('link', { name: '寄信設定' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('heading', { level: 1, name: '寄信設定' })).toBeInTheDocument();
  });

  it('分成寄送、寄信服務、寄件人、儲存與測試四塊，儲存與寄測試信在最後一塊', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    const regions = screen.getAllByRole('region').map((region) => region.getAttribute('aria-labelledby'));
    expect(regions).toHaveLength(4);
    expect(screen.getByRole('heading', { level: 2, name: '寄送' })).toBeInTheDocument();
    const service = screen.getByRole('region', { name: '寄信服務' });
    expect(within(service).getByLabelText('Gmail 地址')).toBeInTheDocument();
    const sender = screen.getByRole('region', { name: '寄件人' });
    expect(within(sender).getByLabelText('寄件人名稱')).toBeInTheDocument();
    expect(within(sender).getByLabelText('網站網址')).toBeInTheDocument();
    const last = screen.getByRole('region', { name: '儲存與測試' });
    expect(within(last).getByRole('button', { name: '儲存' })).toBeInTheDocument();
    expect(within(last).getByRole('button', { name: '寄測試信給我' })).toBeInTheDocument();
  });

  it('一般成員的設定中心沒有「寄信設定」；直接打網址說沒有權限，也不去讀設定', async () => {
    // Arrange
    signedInAs(exampleMember);
    forbidEmailSettings();

    // Act
    renderApp('/email-settings');

    // Assert
    expect(await screen.findByText('只有組織的擁有者與管理員可以設定寄信。')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '寄信設定' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '儲存' })).not.toBeInTheDocument();
  });
});

describe('寄信設定：第一次開', () => {
  it('Gmail 的欄位、預先填的寄件人名稱與網站網址；沒有 SMTP 的欄位', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    expect(screen.getByRole('checkbox', { name: /寄送 Email 通知/ })).not.toBeChecked();
    expect(within(screen.getByRole('group', { name: '寄信服務' })).getByRole('button', { name: 'Gmail' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText('Gmail 地址')).toHaveValue('');
    expect(screen.getByLabelText('應用程式密碼')).toHaveValue('');
    expect(screen.getByLabelText('寄件人名稱')).toHaveValue('範例組織');
    expect(screen.getByLabelText('網站網址')).toHaveValue(window.location.origin);
    expect(screen.queryByLabelText('主機')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /應用程式密碼/ })).toHaveAttribute('href', 'https://myaccount.google.com/apppasswords');
  });

  it('填 Gmail 地址與密碼、開啟、儲存：送出本文，密碼欄清空並說已經存了', async () => {
    // Arrange
    const { user, backend, save } = await openPage();

    // Act
    await user.click(screen.getByRole('checkbox', { name: /寄送 Email 通知/ }));
    await user.type(screen.getByLabelText('Gmail 地址'), 'team@example.com');
    await user.type(screen.getByLabelText('應用程式密碼'), 'abcd efgh ijkl mnop');
    await user.click(save);

    // Assert
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();
    expect(backend.saved).toEqual([
      {
        enabled: true,
        provider: 'gmail',
        host: null,
        port: null,
        security: null,
        username: 'team@example.com',
        password: 'abcd efgh ijkl mnop',
        fromAddress: null,
        fromName: '範例組織',
        siteUrl: window.location.origin,
      },
    ]);
    expect(screen.getByLabelText('應用程式密碼')).toHaveValue('');
    expect(screen.getByText('已經存了，不改就留白')).toBeInTheDocument();
  });
});

describe('寄信設定：其他 SMTP', () => {
  it('切到「其他 SMTP」：出現六個欄位，加密方式換了連接埠跟著換', async () => {
    // Arrange
    const { user } = await openPage();

    // Act
    await user.click(screen.getByRole('button', { name: '其他 SMTP' }));
    await chooseOption(user, screen.getByRole('combobox', { name: '加密方式' }), 'SSL／TLS');

    // Assert
    for (const label of ['主機', '帳號', '密碼', '寄件人地址']) {
      expect(screen.getByLabelText(label)).toBeInTheDocument();
    }
    expect(screen.getByRole('combobox', { name: '連接埠' })).toHaveTextContent('465');
    expect(screen.queryByLabelText('Gmail 地址')).not.toBeInTheDocument();
  });

  it('不加密：帳號與密碼停用並說明（已存密碼時說會清掉）；儲存時不送帳號、清掉密碼', async () => {
    // Arrange
    const { user, backend, save } = await openPage({ ...SMTP_SAVED, security: 'start_tls', port: 587 });

    // Act
    await chooseOption(user, await screen.findByRole('combobox', { name: '加密方式' }), '不加密');

    // Assert
    const note = '不加密時不能登入，帳號密碼會用明文送出。儲存後會清掉已存的帳號與密碼。';
    expect(screen.getByLabelText('帳號')).toBeDisabled();
    expect(screen.getByLabelText('密碼')).toBeDisabled();
    expect(screen.getByLabelText('帳號')).toHaveAccessibleDescription(note);
    expect(screen.getByLabelText('密碼')).toHaveAccessibleDescription(note);

    // Act
    await user.click(save);

    // Assert
    await waitFor(() => {
      expect(backend.saved).toHaveLength(1);
    });
    expect(backend.saved[0]).toMatchObject({ provider: 'smtp', security: 'none', port: 25, username: null, password: '' });
  });

  it('已經存了密碼：密碼欄是空的並提示；登入的對象沒變就存，送 null（沿用）', async () => {
    // Arrange
    const { user, backend, save } = await openPage(SMTP_SAVED);

    // Act
    await user.clear(screen.getByLabelText('寄件人名稱'));
    await user.type(screen.getByLabelText('寄件人名稱'), '新的名稱');
    await user.click(save);

    // Assert
    expect(screen.getByLabelText('密碼')).toHaveValue('');
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();
    expect(backend.saved[0]).toMatchObject({ host: 'smtp.example.com', port: 465, security: 'tls', password: null, fromName: '新的名稱' });
  });

  it('改了主機、連接埠、帳號或寄信服務：提示改成要重新輸入密碼；照樣送出，後端的 422 標在密碼欄', async () => {
    // Arrange
    const { user, backend, save } = await openPage(SMTP_SAVED);
    const changed = '改了寄信服務、主機、連接埠或帳號，要重新輸入密碼。';

    // Act
    await user.clear(screen.getByLabelText('主機'));
    await user.type(screen.getByLabelText('主機'), 'mail.example.com');

    // Assert
    expect(screen.getByLabelText('密碼')).toHaveAccessibleDescription(changed);
    expect(screen.queryByText('已經存了，不改就留白')).not.toBeInTheDocument();

    // Act
    await user.click(save);

    // Assert
    // 說明和錯誤同一句，只顯示錯誤，不疊兩次。
    await waitFor(() => {
      expect(screen.getByLabelText('密碼')).toHaveAttribute('aria-invalid', 'true');
    });
    expect(screen.getByLabelText('密碼')).toHaveAccessibleDescription(changed);
    expect(screen.getAllByText(changed)).toHaveLength(1);
    expect(backend.saved).toHaveLength(1);
    expect(backend.saved[0]?.password).toBeNull();
  });

  it('密碼只打空白：當作沒填，送 null', async () => {
    // Arrange
    const { user, backend, save } = await openPage(SMTP_SAVED);

    // Act
    await user.type(screen.getByLabelText('密碼'), '   ');
    await user.click(save);

    // Assert
    await waitFor(() => {
      expect(backend.saved).toHaveLength(1);
    });
    expect(backend.saved[0]?.password).toBeNull();
  });
});

describe('寄信設定：切換寄信服務', () => {
  it('切換時清掉所有欄位的錯誤、表單上方的錯誤與結果訊息', async () => {
    // Arrange
    const { user, backend, save } = await openPage();
    backend.rejectNextSave({
      status: 422,
      code: 'email.settings_invalid',
      message: '寄信設定有問題。',
      details: [
        { code: 'email.field_required', path: 'username', message: '要填 Gmail 地址。' },
        { code: 'email.field_required', path: 'password', message: '要填 Gmail 的應用程式密碼。' },
        { code: 'email.field_required', path: 'host', message: '請填主機。' },
      ],
    });
    await user.click(save);
    expect(await screen.findByText('要填 Gmail 地址。')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('請填主機。');

    // Act
    await user.click(screen.getByRole('button', { name: '其他 SMTP' }));

    // Assert
    expect(screen.queryByText('要填 Gmail 地址。')).not.toBeInTheDocument();
    expect(screen.queryByText('要填 Gmail 的應用程式密碼。')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByLabelText('帳號')).not.toHaveAttribute('aria-invalid', 'true');
  });

  it('Gmail 切到 SMTP 再切回來：送出的是 Gmail 的本文（SMTP 的欄位都是 null）', async () => {
    // Arrange
    const { user, backend, save } = await openPage();
    await user.type(screen.getByLabelText('Gmail 地址'), 'team@example.com');

    // Act
    await user.click(screen.getByRole('button', { name: '其他 SMTP' }));
    await user.type(screen.getByLabelText('主機'), 'smtp.example.com');
    await user.click(screen.getByRole('button', { name: 'Gmail' }));
    await user.click(save);

    // Assert
    await waitFor(() => {
      expect(backend.saved).toHaveLength(1);
    });
    expect(backend.saved[0]).toMatchObject({ provider: 'gmail', host: null, port: null, security: null, fromAddress: null, username: 'team@example.com' });
  });

  it('改了任何欄位（含換加密方式）就清掉「已儲存。」', async () => {
    // Arrange
    const { user, save } = await openPage(SMTP_SAVED);
    await user.click(save);
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '加密方式' }), 'STARTTLS');

    // Assert
    expect(screen.queryByText('已儲存。')).not.toBeInTheDocument();
  });
});

describe('寄信設定：錯誤', () => {
  it('422 的細項放在對應的欄位下面，焦點到第一個有問題的欄位，內容不丟', async () => {
    // Arrange
    const { user, backend, save } = await openPage(SMTP_SAVED);
    backend.rejectNextSave({
      status: 422,
      code: 'email.settings_invalid',
      message: '寄信設定有問題。',
      details: [
        { code: 'email.field_required', path: 'host', message: '請填主機。' },
        { code: 'email.field_invalid', path: 'siteUrl', message: '網站網址要是 http 或 https 開頭的網址。' },
      ],
    });
    await user.clear(screen.getByLabelText('網站網址'));
    await user.type(screen.getByLabelText('網站網址'), 'ftp://x');

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByText('請填主機。')).toBeInTheDocument();
    expect(screen.getByLabelText('主機')).toHaveAccessibleDescription('請填主機。');
    expect(screen.getByLabelText('網站網址')).toHaveAccessibleDescription(/網站網址要是/);
    expect(screen.getByLabelText('網站網址')).toHaveValue('ftp://x');
    await waitFor(() => {
      expect(screen.getByLabelText('主機')).toHaveFocus();
    });
  });

  it('細項對到畫面上沒有的欄位（Gmail 沒有主機）：訊息放在表單上方，不會不見', async () => {
    // Arrange
    const { user, backend, save } = await openPage();
    backend.rejectNextSave({
      status: 422,
      code: 'email.settings_invalid',
      message: '寄信設定有問題。',
      details: [{ code: 'email.field_required', path: 'host', message: '請填主機。' }],
    });

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('請填主機。');
  });
});

describe('寄信設定：更多錯誤', () => {
  it('email.port_not_allowed 標在連接埠、email.insecure_credentials 標在加密方式', async () => {
    // Arrange
    const { user, backend, save } = await openPage(SMTP_SAVED);
    backend.rejectNextSave({
      status: 422,
      code: 'email.port_not_allowed',
      message: '連接埠不在允許的範圍。',
      details: [
        { code: 'email.port_not_allowed', path: '', message: '連接埠只能是 25、465、587、2525。' },
        { code: 'email.insecure_credentials', path: '', message: '不加密時不能設帳號密碼。' },
      ],
    });

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByRole('combobox', { name: '連接埠' })).toHaveAccessibleDescription('連接埠只能是 25、465、587、2525。');
    expect(screen.getByRole('combobox', { name: '加密方式' })).toHaveAccessibleDescription('不加密時不能設帳號密碼。');
  });

  it('儲存太頻繁（429）：說請求太頻繁', async () => {
    // Arrange
    const { user, backend, save } = await openPage();
    backend.rejectNextSave({ status: 429, code: 'request.rate_limited', message: '請求太頻繁，請稍後再試。' });

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('請求太頻繁，請稍後再試。');
  });

  it('送出中再按「儲存」不送第二次', async () => {
    // Arrange
    const { user, save } = await openPage();
    const requests: unknown[] = [];
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.put('/v1/email-settings', async ({ request }) => {
        requests.push(await request.json());
        await gate;
        return failure(500, 'server.error', '伺服器暫時無法處理。');
      }),
    );

    // Act
    await user.click(save);
    await waitFor(() => {
      expect(save).toHaveAttribute('aria-disabled', 'true');
    });
    await user.click(save);
    release();

    // Assert
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(requests).toHaveLength(1);
  });

  it('管理員被後端回 403：顯示沒有權限，不是載入失敗', async () => {
    // Arrange
    signedInAs(exampleOwner);
    forbidEmailSettings();

    // Act
    renderApp('/email-settings');

    // Assert
    expect(await screen.findByText('只有組織的擁有者與管理員可以設定寄信。')).toBeInTheDocument();
    expect(screen.queryByText('寄信設定載入失敗')).not.toBeInTheDocument();
  });

  it('背景重抓失敗：表單與填到一半的內容留著，上面說明畫面可能是舊的', async () => {
    // Arrange
    const { user, queryClient } = await openPage();
    await user.type(screen.getByLabelText('Gmail 地址'), 'team@example.com');
    server.use(http.get('/v1/email-settings', () => failure(500, 'server.error', '伺服器暫時無法處理。')));

    // Act
    await queryClient.invalidateQueries({ queryKey: ['email-settings'] });

    // Assert
    expect(await screen.findByText('無法更新寄信設定，畫面上可能是舊的。')).toBeInTheDocument();
    expect(screen.getByLabelText('Gmail 地址')).toHaveValue('team@example.com');
    expect(screen.queryByText('寄信設定載入失敗')).not.toBeInTheDocument();
  });

  it('改了欄位就清掉「已經寄到…」', async () => {
    // Arrange
    const { user } = await openPage();
    await user.click(screen.getByRole('button', { name: '寄測試信給我' }));
    expect(await screen.findByText('已經寄到 user@example.com，請到信箱確認。')).toBeInTheDocument();

    // Act
    await user.type(screen.getByLabelText('網站網址'), '/x');

    // Assert
    expect(screen.queryByText(/已經寄到/)).not.toBeInTheDocument();
  });
});

describe('寄信設定：寄送狀況', () => {
  it('上次失敗比上次寄出新：警告提示帶時間與原因', async () => {
    // Arrange / Act
    await openPage({
      ...SMTP_SAVED,
      status: { lastSentAt: '2026-11-20T01:00:00+00:00', lastFailedAt: '2026-11-21T01:00:00+00:00', lastError: '連不上 smtp.example.com:465' },
    });

    // Assert
    expect(screen.getByText(/上次寄送失敗（.+）：連不上 smtp\.example\.com:465/)).toBeInTheDocument();
    expect(screen.queryByText(/上次寄出：/)).not.toBeInTheDocument();
  });

  it('都沒有寄過：不顯示寄送狀況', async () => {
    // Arrange / Act
    await openPage(SMTP_SAVED);

    // Assert
    expect(screen.queryByText(/上次寄出：/)).not.toBeInTheDocument();
    expect(screen.queryByText(/上次寄送失敗/)).not.toBeInTheDocument();
  });

  it('只有寄出過：一行「上次寄出：時間」', async () => {
    // Arrange / Act
    await openPage({ ...SMTP_SAVED, status: { lastSentAt: '2026-11-21T01:00:00+00:00', lastFailedAt: null, lastError: null } });

    // Assert
    expect(screen.getByText(/上次寄出：2026\/11\/2\d \d\d:\d\d/)).toBeInTheDocument();
  });
});

describe('寄信設定：寄測試信', () => {
  it('用畫面上還沒存的設定寄，成功說寄到哪', async () => {
    // Arrange
    const { user, backend } = await openPage();
    await user.type(screen.getByLabelText('Gmail 地址'), 'team@example.com');
    await user.type(screen.getByLabelText('應用程式密碼'), 'secretsecretsecr');

    // Act
    await user.click(screen.getByRole('button', { name: '寄測試信給我' }));

    // Assert
    expect(await screen.findByText('已經寄到 user@example.com，請到信箱確認。')).toBeInTheDocument();
    expect(backend.tests).toHaveLength(1);
    expect(backend.tests[0]).toMatchObject({ provider: 'gmail', username: 'team@example.com', password: 'secretsecretsecr' });
    expect(backend.saved).toHaveLength(0);
  });

  it('寄不出去（422 email.send_failed）：說原因；429 說太頻繁', async () => {
    // Arrange
    const { user, backend } = await openPage();
    backend.rejectNextTest({ status: 422, code: 'email.send_failed', message: '帳號或密碼不對（Gmail 要用應用程式密碼，不是登入密碼）' });

    // Act
    await user.click(screen.getByRole('button', { name: '寄測試信給我' }));

    // Assert
    expect(await screen.findByText('帳號或密碼不對（Gmail 要用應用程式密碼，不是登入密碼）')).toBeInTheDocument();

    // Act
    backend.rejectNextTest({ status: 429, code: 'request.rate_limited', message: '請求太頻繁，請稍後再試。' });
    await user.click(screen.getByRole('button', { name: '寄測試信給我' }));

    // Assert
    expect(await screen.findByText('測試信寄得太頻繁，請稍後再試。')).toBeInTheDocument();
  });
});

describe('寄信設定：沒存的修改', () => {
  it('改了就離開：先問；留下來還在，捨棄才離開', async () => {
    // Arrange
    const { user, router } = await openPage();
    await user.type(screen.getByLabelText('Gmail 地址'), 'team@example.com');

    // Act
    await user.click(screen.getByRole('link', { name: '我的工作' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '還有沒儲存的修改' });
    expect(currentPath(router)).toBe('/email-settings');
    await user.click(within(dialog).getByRole('button', { name: '留下來' }));
    expect(screen.getByLabelText('Gmail 地址')).toHaveValue('team@example.com');
    await user.click(screen.getByRole('link', { name: '我的工作' }));
    await user.click(await screen.findByRole('button', { name: '捨棄修改並離開' }));
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
  });

  it('存完再離開：不問', async () => {
    // Arrange
    const { user, router, save } = await openPage();
    await user.type(screen.getByLabelText('Gmail 地址'), 'team@example.com');
    await user.type(screen.getByLabelText('應用程式密碼'), 'abcdabcdabcdabcd');
    await user.click(save);
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: '我的工作' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
    expect(screen.queryByRole('dialog', { name: '還有沒儲存的修改' })).not.toBeInTheDocument();
  });

  it('沒改就離開：不問', async () => {
    // Arrange
    const { user, router } = await openPage();

    // Act
    await user.click(screen.getByRole('link', { name: '我的工作' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
  });
});
