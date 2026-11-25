import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ok, signedInAs, signedOut } from '../../test/api';
import { cliLoginCheckOf, failCliLoginApprove, serveCliLogin } from '../../test/cliLogin';
import { exampleOwner } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const navigateTo = vi.hoisted(() => vi.fn<(url: string) => void>());
vi.mock('../../lib/navigateTo', () => ({ navigateTo }));

const STATE = 'st4te_-1';
const QUERY = `port=45678&state=${STATE}&challenge=ch4llenge&machine=MacBook-Pro&tokens=cli%2Cagent&scope=items%3Aread+items%3Awrite+blueprint%3Aread`;
const PATH = `/cli/authorize?${QUERY}`;
const CALLBACK = 'http://127.0.0.1:45678/kumi-login';

async function openConsent(path = PATH) {
  signedInAs(exampleOwner);
  const view = renderApp(path);
  await screen.findByRole('heading', { level: 1 });
  return view;
}

beforeEach(() => {
  navigateTo.mockReset();
});

describe('瀏覽器登入同意頁：顯示', () => {
  it('標題、電腦、帳號、警告，沒有側欄', async () => {
    // Arrange
    serveCliLogin();

    // Act
    await openConsent();

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: /讓這台電腦上的 kumi 使用你的帳號/ })).toBeInTheDocument();
    expect(screen.getByText('MacBook-Pro')).toBeInTheDocument();
    expect(screen.getByText('帳號？').className).toMatch(/nowrap/);
    expect(screen.getByText('owner@example.com', { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/只有在你剛剛自己在終端機執行 kumi 時才允許/)).toBeInTheDocument();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });

  it('把網址列的參數轉成請求交給伺服器檢查', async () => {
    // Arrange
    const backend = serveCliLogin();

    // Act
    await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Assert
    expect(backend.checks).toEqual([
      {
        port: 45678,
        state: STATE,
        challenge: 'ch4llenge',
        machine: 'MacBook-Pro',
        tokens: ['cli', 'agent'],
        scopes: ['items:read', 'items:write', 'blueprint:read'],
      },
    ]);
  });

  it('命令列的範圍列四個（不含本機代理），請求的讀取範圍預先勾好、寫入的列出來但不勾', async () => {
    // Arrange
    serveCliLogin();

    // Act
    await openConsent();

    // Assert
    const group = await screen.findByRole('group', { name: '命令列可以做這些事' });
    expect(within(group).getAllByRole('checkbox')).toHaveLength(4);
    expect(within(group).getByRole('checkbox', { name: /讀取項目/ })).toBeChecked();
    expect(within(group).getByRole('checkbox', { name: /修改項目/ })).not.toBeChecked();
    expect(within(group).getByRole('checkbox', { name: /讀取設定/ })).toBeChecked();
    expect(within(group).getByRole('checkbox', { name: /修改設定/ })).not.toBeChecked();
  });

  it('本機代理的範圍固定列出來，不能改', async () => {
    // Arrange
    serveCliLogin();

    // Act
    await openConsent();

    // Assert
    const heading = await screen.findByRole('heading', { level: 2, name: '本機代理' });
    expect(heading.parentElement).toHaveTextContent('本機代理');
    expect(screen.getAllByRole('checkbox')).toHaveLength(4);
    expect(screen.getByText('會另外建立一把本機代理用的權杖', { exact: false })).toBeInTheDocument();
  });

  it('本機代理底下的範圍用這一頁自己的說法、照固定順序排（不照伺服器給的順序，也不和標題同名）', async () => {
    // Arrange
    serveCliLogin();

    // Act
    await openConsent();

    // Assert
    const heading = await screen.findByRole('heading', { level: 2, name: '本機代理' });
    const items = within(heading.parentElement as HTMLElement).getAllByRole('listitem');
    expect(items.map((item) => item.textContent)).toEqual([
      '讀取項目',
      '讀取設定',
      '修改設定',
      '接網頁的 AI 助理（領工作、回報進度）',
    ]);
  });

  it('只要命令列時沒有本機代理那一塊', async () => {
    // Arrange
    serveCliLogin({ check: cliLoginCheckOf({ tokens: ['cli'] }) });

    // Act
    await openConsent(PATH.replace('cli%2Cagent', 'cli'));

    // Assert
    await screen.findByRole('group', { name: '命令列可以做這些事' });
    expect(screen.queryByRole('heading', { level: 2, name: '本機代理' })).not.toBeInTheDocument();
  });
});

describe('瀏覽器登入同意頁：不合格的請求', () => {
  it.each([
    ['連接埠不是數字', PATH.replace('port=45678', 'port=abc')],
    ['連接埠太小', PATH.replace('port=45678', 'port=80')],
    ['連接埠在 32768 以下', PATH.replace('port=45678', 'port=32767')],
    ['連接埠太大', PATH.replace('port=45678', 'port=70000')],
    ['沒有連接埠', PATH.replace('port=45678&', '')],
    ['state 帶了不是 base64url 的字元', PATH.replace(STATE, 'a%2Fb')],
  ])('%s：直接說有問題、不呼叫伺服器', async (_name, path) => {
    // Arrange
    const backend = serveCliLogin();

    // Act
    await openConsent(path);

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: '無法登入' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('這個登入請求有問題');
    expect(screen.getByRole('alert')).toHaveTextContent('kumi auth login');
    expect(screen.queryByRole('button', { name: '允許' })).not.toBeInTheDocument();
    expect(backend.checks).toHaveLength(0);
  });

  it('伺服器說請求不對（422）：顯示伺服器的訊息', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(
      http.post('/v1/cli-login/check', () =>
        Response.json({ success: false, data: null, error: { code: 'cli_login.request_invalid', message: '登入請求的資料不對。', details: [] } }, { status: 422 }),
      ),
    );

    // Act
    renderApp(PATH);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('登入請求的資料不對。');
  });
});

describe('瀏覽器登入同意頁：允許', () => {
  it('送出請求、勾的範圍與期限，導到伺服器給的迴路網址，並說可以回終端機', async () => {
    // Arrange
    const backend = serveCliLogin();
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Act
    await user.click(screen.getByRole('checkbox', { name: /修改設定/ }));
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    await waitFor(() => {
      expect(navigateTo).toHaveBeenCalledWith(`${CALLBACK}?code=abc&state=${STATE}`);
    });
    expect(backend.approvals).toEqual([
      {
        port: 45678,
        state: STATE,
        challenge: 'ch4llenge',
        machine: 'MacBook-Pro',
        tokens: ['cli', 'agent'],
        scopes: ['items:read', 'items:write', 'blueprint:read'],
        grantedScopes: ['items:read', 'blueprint:read', 'blueprint:write'],
        expiresInDays: 90,
      },
    ]);
    expect(screen.getByText('已允許，請回到終端機。')).toBeInTheDocument();
  });

  it('期限一定要選一個天數：沒有「不過期」，可以改成一年', async () => {
    // Arrange
    const backend = serveCliLogin();
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Act
    const options = await openOptions(user, screen.getByRole('combobox', { name: '期限' }));
    const names = options.map((option) => option.textContent);
    await chooseOption(user, screen.getByRole('combobox', { name: '期限' }), '1 年');
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    expect(names).toEqual(['30 天', '90 天', '1 年']);
    await waitFor(() => {
      expect(backend.approvals[0]?.expiresInDays).toBe(365);
    });
  });

  it('寫入的範圍要自己勾才會送出', async () => {
    // Arrange
    const backend = serveCliLogin();
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Act
    await user.click(screen.getByRole('checkbox', { name: /修改項目/ }));
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    await waitFor(() => {
      expect(backend.approvals[0]?.grantedScopes).toEqual(['items:read', 'items:write', 'blueprint:read']);
    });
  });

  it('請求只要寫入的範圍：都不勾，允許停用並說明，勾了才能允許', async () => {
    // Arrange
    const backend = serveCliLogin({ check: cliLoginCheckOf({ scopes: ['items:write'] }) });
    const { user } = await openConsent();
    const allow = await screen.findByRole('button', { name: '允許' });

    // Assert（先確認是停用的狀態，再確認按下去沒有東西送出）
    expect(screen.getByRole('checkbox', { name: /修改項目/ })).not.toBeChecked();
    expect(allow).toHaveAttribute('aria-disabled', 'true');
    expect(allow).toHaveAccessibleDescription('至少要勾一項');

    // Act
    await user.click(allow);
    await new Promise((resolve) => setTimeout(resolve, 0));

    // Assert
    expect(backend.approvals).toHaveLength(0);
  });

  it('說明按下允許之後會把一次性的登入碼送回哪一個連接埠', async () => {
    // Arrange
    serveCliLogin();

    // Act
    await openConsent();

    // Assert
    expect(await screen.findByText('按下允許之後，會把一次性的登入碼送回這台電腦的 127.0.0.1:45678。')).toBeInTheDocument();
  });

  it('有本機代理的權杖時說明它固定 90 天；只有命令列時不寫', async () => {
    // Arrange
    serveCliLogin();

    // Act
    await openConsent();

    // Assert
    expect(await screen.findByText(/本機代理的權杖固定 90 天/)).toBeInTheDocument();
  });

  it('只要本機代理時沒有期限的選單，說明固定 90 天，送出的期限是 90', async () => {
    // Arrange
    const backend = serveCliLogin({ check: cliLoginCheckOf({ tokens: ['agent'], scopes: [] }) });
    const { user } = await openConsent(PATH.replace('cli%2Cagent', 'agent'));

    // Act
    await user.click(await screen.findByRole('button', { name: '允許' }));

    // Assert
    expect(screen.queryByRole('combobox', { name: '期限' })).not.toBeInTheDocument();
    expect(screen.getByText('本機代理的權杖固定 90 天。')).toBeInTheDocument();
    expect(screen.queryByText(/不受上面的期限影響/)).not.toBeInTheDocument();
    await waitFor(() => {
      expect(backend.approvals[0]?.expiresInDays).toBe(90);
    });
  });

  it('命令列與本機代理一起要時維持期限選單與「不受上面的期限影響」', async () => {
    // Arrange
    serveCliLogin();

    // Act
    await openConsent();

    // Assert
    expect(await screen.findByRole('combobox', { name: '期限' })).toBeInTheDocument();
    expect(screen.getByText('本機代理的權杖固定 90 天，不受上面的期限影響。')).toBeInTheDocument();
  });

  it('只要命令列時沒有本機代理固定 90 天的說明', async () => {
    // Arrange
    serveCliLogin({ check: cliLoginCheckOf({ tokens: ['cli'] }) });

    // Act
    await openConsent(PATH.replace('cli%2Cagent', 'cli'));

    // Assert
    await screen.findByRole('group', { name: '命令列可以做這些事' });
    expect(screen.queryByText(/固定 90 天/)).not.toBeInTheDocument();
  });

  it('伺服器給的導回網址不是這個連接埠的迴路：不導過去，說明並可以再試', async () => {
    // Arrange
    serveCliLogin({ approveRedirect: 'http://evil.example/kumi-login?code=abc&state=x' });
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    expect(await screen.findByText(/伺服器給的導回網址不對/)).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: '允許' })).not.toHaveAttribute('aria-disabled');
    expect(screen.queryByText(/沒辦法建立本機代理的權杖/)).not.toBeInTheDocument();
  });

  it('一個範圍都沒勾：允許停用並說明，不送出', async () => {
    // Arrange
    const backend = serveCliLogin({ check: cliLoginCheckOf({ scopes: ['items:read'] }) });
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Act
    await user.click(screen.getByRole('checkbox', { name: /讀取項目/ }));
    const allow = screen.getByRole('button', { name: '允許' });
    await user.click(allow);

    // Assert
    expect(allow).toHaveAttribute('aria-disabled', 'true');
    expect(allow).toHaveAccessibleDescription('至少要勾一項');
    expect(backend.approvals).toHaveLength(0);
  });

  it('只要本機代理、組織又不允許：允許停用並說明為什麼', async () => {
    // Arrange
    const backend = serveCliLogin({ check: cliLoginCheckOf({ tokens: ['agent'], agentAllowed: false, scopes: [] }) });
    const { user } = await openConsent(PATH.replace('cli%2Cagent', 'agent'));

    // Act
    const allow = await screen.findByRole('button', { name: '允許' });
    await user.click(allow);

    // Assert
    expect(allow).toHaveAttribute('aria-disabled', 'true');
    expect(allow).toHaveAccessibleDescription(/組織沒有開放成員使用自己的 AI 帳號/);
    expect(backend.approvals).toHaveLength(0);
    expect(screen.getByText('組織沒有開放成員使用自己的 AI 帳號，沒辦法建立本機代理的權杖。')).toBeInTheDocument();
    expect(screen.queryByText(/這次不會建立本機代理的權杖/)).not.toBeInTheDocument();
  });

  it('組織不允許本機代理但有命令列：說明這次不會建立本機代理的權杖，照樣可以允許', async () => {
    // Arrange
    serveCliLogin({ check: cliLoginCheckOf({ agentAllowed: false }) });

    // Act
    await openConsent();

    // Assert
    expect(await screen.findByText('組織沒有開放成員使用自己的 AI 帳號，這次不會建立本機代理的權杖。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '允許' })).not.toHaveAttribute('aria-disabled');
  });

  it('額度滿了（409）：顯示訊息與「到存取權杖撤銷不用的」，之後可以再按允許', async () => {
    // Arrange
    serveCliLogin();
    failCliLoginApprove(409, 'access_token.limit_reached', '最多只能同時有 20 把還能用的存取權杖，請先撤銷不用的。');
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    expect(await screen.findByText(/最多只能同時有 20 把/)).toBeInTheDocument();
    const link = screen.getByRole('link', { name: '到存取權杖撤銷不用的' });
    expect(link).toHaveAttribute('href', '/account/integrations#tokens');
    expect(link).toHaveAttribute('target', '_blank');
    expect(screen.getByRole('button', { name: '允許' })).not.toHaveAttribute('aria-disabled');
  });

  it('沒有權限（403）：顯示伺服器的訊息', async () => {
    // Arrange
    serveCliLogin();
    failCliLoginApprove(403, 'cli_login.agent_not_allowed', '組織沒有開放成員使用自己的 AI 帳號。');
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '允許' });

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('組織沒有開放成員使用自己的 AI 帳號。');
    expect(screen.queryByRole('link', { name: '到存取權杖撤銷不用的' })).not.toBeInTheDocument();
  });
});

describe('瀏覽器登入同意頁：拒絕', () => {
  it('不呼叫伺服器，自己導回迴路並帶 access_denied 與 state', async () => {
    // Arrange
    const backend = serveCliLogin();
    const { user } = await openConsent();
    await screen.findByRole('button', { name: '拒絕' });

    // Act
    await user.click(screen.getByRole('button', { name: '拒絕' }));

    // Assert
    expect(navigateTo).toHaveBeenCalledWith(`${CALLBACK}?error=access_denied&state=${STATE}`);
    expect(backend.approvals).toHaveLength(0);
    expect(screen.getByText('已拒絕，請回到終端機。')).toBeInTheDocument();
  });
});

describe('瀏覽器登入同意頁：沒登入', () => {
  it('導到 /login?next=（帶著完整的查詢字串），登入後回到同意頁', async () => {
    // Arrange
    signedOut();
    serveCliLogin();
    const { user, router } = renderApp(PATH);

    // Act
    await screen.findByRole('heading', { level: 1, name: '登入' });
    const next = new URLSearchParams(router.state.location.search).get('next');
    signedInAs(exampleOwner);
    server.use(http.post('/v1/session', () => ok({ user: exampleOwner })));
    await user.type(screen.getByLabelText('Email'), 'owner@example.com');
    await user.type(screen.getByLabelText('密碼'), 'example-pass-1');
    await user.click(screen.getByRole('button', { name: '登入' }));

    // Assert
    expect(next).toBe(PATH);
    await waitFor(() => {
      expect(currentPath(router)).toBe(PATH);
    });
    expect(await screen.findByRole('heading', { level: 1, name: /讓這台電腦上的 kumi/ })).toBeInTheDocument();
  });
});
