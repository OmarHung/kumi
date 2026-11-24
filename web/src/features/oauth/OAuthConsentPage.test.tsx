import { act, screen, waitFor, within } from '@testing-library/react';
import { delay, http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PRODUCT_NAME } from '../../config/product';
import { ok, signedInAs, signedOut } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { authorizationCheckOf, authorizationFailure, serveAuthorization } from '../../test/oauth';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const navigateTo = vi.hoisted(() => vi.fn<(url: string) => void>());
vi.mock('../../lib/navigateTo', () => ({ navigateTo }));

const QUERY =
  'response_type=code&client_id=https%3A%2F%2Fclient.example%2Fmetadata.json&redirect_uri=http%3A%2F%2F127.0.0.1%3A4567%2Fcallback' +
  '&scope=items%3Aread+items%3Awrite&state=xyz&code_challenge=abc&code_challenge_method=S256&resource=https%3A%2F%2Fpm.example%2Fmcp';
const PATH = `/oauth/authorize?${QUERY}`;

async function openConsent(path = PATH) {
  signedInAs(exampleOwner);
  const view = renderApp(path);
  await screen.findByRole('heading', { level: 1 });
  return view;
}

beforeEach(() => {
  navigateTo.mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('同意頁：顯示', () => {
  it('標題、提供者、帳號、導回的主機與本機的提醒', async () => {
    // Arrange
    serveAuthorization();

    // Act
    await openConsent();

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: `授權 Claude Code 使用你的 ${PRODUCT_NAME} 帳號` })).toBeInTheDocument();
    expect(screen.getByText('由 client.example 提供')).toBeInTheDocument();
    expect(screen.getByText('範例使用者', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('owner@example.com', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('127.0.0.1')).toBeInTheDocument();
    expect(screen.getByText(/這是你電腦上的程式/)).toBeInTheDocument();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });

  it('把網址列的參數照名稱交給伺服器檢查', async () => {
    // Arrange
    const backend = serveAuthorization();

    // Act
    await openConsent();

    // Assert
    expect(backend.checks).toEqual([
      {
        responseType: 'code',
        clientId: 'https://client.example/metadata.json',
        redirectUri: 'http://127.0.0.1:4567/callback',
        scope: 'items:read items:write',
        state: 'xyz',
        codeChallenge: 'abc',
        codeChallengeMethod: 'S256',
        resource: 'https://pm.example/mcp',
      },
    ]);
  });

  it('請求的範圍預設全勾，沒請求的不列', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ requestedScopes: ['items:read', 'items:write'] }) });

    // Act
    await openConsent();

    // Assert
    expect(screen.getByRole('checkbox', { name: '看專案與項目' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: '建立與修改項目、留言' })).toBeChecked();
    expect(screen.queryByRole('checkbox', { name: /變更集/ })).not.toBeInTheDocument();
  });

  it('沒有經過驗證的應用（clientHost 是 null）：標題不同、名稱旁有「未驗證」、有警告，不寫「由…提供」', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ clientHost: null, clientName: 'Evil Agent' }) });

    // Act
    await openConsent();

    // Assert
    expect(
      screen.getByRole('heading', { level: 1, name: `一個未經驗證的應用「Evil Agent」要求使用你的 ${PRODUCT_NAME} 帳號` }),
    ).toBeInTheDocument();
    expect(screen.getByText('未驗證')).toBeInTheDocument();
    expect(screen.getByText('這個應用沒有經過驗證。只有在你自己剛剛從這個應用發起連線時才允許。')).toBeInTheDocument();
    expect(screen.queryByText(/提供$/)).not.toBeInTheDocument();
  });

  it('沒有經過驗證的應用預設只勾讀取的範圍，寫入的要自己勾', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ clientHost: null }) });

    // Act
    const { user } = await openConsent();

    // Assert
    expect(screen.getByRole('checkbox', { name: '看專案與項目' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: '看專案的設定與變更集' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: '建立與修改項目、留言' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: '提出變更集（還是要有人核准）' })).not.toBeChecked();
    await user.click(screen.getByRole('checkbox', { name: '建立與修改項目、留言' }));
    expect(screen.getByRole('checkbox', { name: '建立與修改項目、留言' })).toBeChecked();
  });

  it('沒有經過驗證、只請求寫入的範圍時，預設一個都不勾，「允許」停用', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ clientHost: null, requestedScopes: ['items:write'] }) });

    // Act
    await openConsent();

    // Assert
    expect(screen.getByRole('checkbox', { name: '建立與修改項目、留言' })).not.toBeChecked();
    expect(screen.getByRole('button', { name: '允許' })).toBeDisabled();
  });

  it('有驗證的應用：「由 {主機} 提供」在標題旁，沒有「未驗證」，預設全勾', async () => {
    // Arrange
    serveAuthorization();

    // Act
    await openConsent();

    // Assert
    expect(screen.queryByText('未驗證')).not.toBeInTheDocument();
    for (const box of screen.getAllByRole('checkbox')) expect(box).toBeChecked();
    expect(screen.getByText('由 client.example 提供')).toHaveClass(/provider/);
  });

  it('主機含 xn-- 時提醒網域有非英文字元', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ clientHost: 'xn--exmple-cua.example' }) });

    // Act
    await openConsent();

    // Assert
    expect(screen.getByText('網域含有非英文字元，請確認是你認得的網站')).toBeInTheDocument();
  });

  it('一般的主機沒有非英文字元的提醒', async () => {
    // Arrange
    serveAuthorization();

    // Act
    await openConsent();

    // Assert
    expect(screen.queryByText(/網域含有非英文字元/)).not.toBeInTheDocument();
  });

  it('導回的不是本機時沒有本機的提醒', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ redirectHost: 'app.example', redirectIsLoopback: false }) });

    // Act
    await openConsent();

    // Assert
    expect(screen.getByText('app.example')).toBeInTheDocument();
    expect(screen.queryByText(/這是你電腦上的程式/)).not.toBeInTheDocument();
  });
});

describe('同意頁：允許與拒絕', () => {
  it('全部取消時「允許」停用，勾回一個就能按', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ requestedScopes: ['items:read', 'items:write'] }) });
    const { user } = await openConsent();

    // Act
    await user.click(screen.getByRole('checkbox', { name: '看專案與項目' }));
    await user.click(screen.getByRole('checkbox', { name: '建立與修改項目、留言' }));

    // Assert
    expect(screen.getByRole('button', { name: '允許' })).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: '看專案與項目' }));
    expect(screen.getByRole('button', { name: '允許' })).toBeEnabled();
  });

  it('允許：送出勾選的範圍、導到伺服器給的網址、換成「可以回到了」', async () => {
    // Arrange
    const backend = serveAuthorization();
    const { user } = await openConsent();
    await user.click(screen.getByRole('checkbox', { name: '提出變更集（還是要有人核准）' }));

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    expect(await screen.findByText('已經授權，可以回到 Claude Code 了。')).toBeInTheDocument();
    expect(backend.approvals[0]?.scopes).toEqual(['items:read', 'items:write', 'blueprint:read']);
    expect(navigateTo).toHaveBeenCalledExactlyOnceWith('http://127.0.0.1:4567/callback?code=abc&state=xyz');
    expect(screen.getByRole('button', { name: '允許' })).toBeDisabled();
  });

  it('拒絕：導到伺服器給的網址', async () => {
    // Arrange
    const backend = serveAuthorization();
    const { user } = await openConsent();

    // Act
    await user.click(screen.getByRole('button', { name: '拒絕' }));

    // Assert
    expect(await screen.findByText('已經拒絕，可以回到 Claude Code 了。')).toBeInTheDocument();
    expect(backend.denials).toHaveLength(1);
    expect(navigateTo).toHaveBeenCalledExactlyOnceWith('http://127.0.0.1:4567/callback?error=access_denied&state=xyz');
  });

  it('允許失敗（不能導回）：寫出原因、沒有「回到應用」、不導頁，可以再試', async () => {
    // Arrange
    serveAuthorization();
    server.use(http.post('/v1/oauth/authorization/approve', () => authorizationFailure('至少要選一個範圍。', null, 'invalid_scope')));
    const { user } = await openConsent();

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('至少要選一個範圍。');
    expect(screen.queryByRole('button', { name: '回到應用' })).not.toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: '允許' })).toBeEnabled();
  });

  it('允許失敗（能導回）：有「回到應用」，按了導到帶錯誤的網址', async () => {
    // Arrange
    serveAuthorization();
    const back = 'http://127.0.0.1:4567/callback?error=access_denied&state=xyz';
    server.use(http.post('/v1/oauth/authorization/approve', () => authorizationFailure('授權的應用太多了。', back, 'access_denied')));
    const { user } = await openConsent();

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));
    await user.click(await screen.findByRole('button', { name: '回到應用' }));

    // Assert
    expect(navigateTo).toHaveBeenCalledExactlyOnceWith(back);
  });
});

describe('同意頁：提示與防重送', () => {
  it('全部取消時有一行永遠掛著的說明，接到「允許」的 aria-describedby', async () => {
    // Arrange
    serveAuthorization({ check: authorizationCheckOf({ requestedScopes: ['items:read'] }) });
    const { user } = await openConsent();
    const allow = screen.getByRole('button', { name: '允許' });
    const hintId = allow.getAttribute('aria-describedby');
    const hint = document.getElementById(hintId ?? '');
    expect(hint).not.toBeNull();
    expect(hint).toBeEmptyDOMElement();

    // Act
    await user.click(screen.getByRole('checkbox', { name: '看專案與項目' }));

    // Assert
    expect(hint).toHaveTextContent('至少要勾一項');
    expect(allow).toHaveAccessibleDescription('至少要勾一項');
    await user.click(screen.getByRole('checkbox', { name: '看專案與項目' }));
    expect(hint).toBeEmptyDOMElement();
  });

  it('錯誤與結果各有固定存在的提示：失敗只動 alert，status 一直是空的', async () => {
    // Arrange
    serveAuthorization();
    server.use(http.post('/v1/oauth/authorization/approve', () => authorizationFailure('至少要選一個範圍。', null, 'invalid_scope')));
    const { user } = await openConsent();
    const alert = screen.getByRole('alert');
    const status = screen.getByRole('status');
    expect(alert).toBeEmptyDOMElement();
    expect(status).toBeEmptyDOMElement();

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    await waitFor(() => {
      expect(alert).toHaveTextContent('至少要選一個範圍。');
    });
    expect(screen.getByRole('alert')).toBe(alert);
    expect(status).toBeEmptyDOMElement();
  });

  it('成功只動 status，同一個 alert 還在而且是空的', async () => {
    // Arrange
    serveAuthorization();
    const { user } = await openConsent();
    const alert = screen.getByRole('alert');
    const status = screen.getByRole('status');

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    await waitFor(() => {
      expect(status).toHaveTextContent('已經授權，可以回到 Claude Code 了。');
    });
    expect(alert).toBeEmptyDOMElement();
  });

  it('結果的提示排在按鈕後面，出現時不會把按鈕往下推', async () => {
    // Arrange
    serveAuthorization();
    await openConsent();
    const allow = screen.getByRole('button', { name: '允許' });

    // Assert
    for (const live of [screen.getByRole('alert'), screen.getByRole('status')]) {
      expect(allow.compareDocumentPosition(live) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
  });

  it('拒絕失敗：寫出原因、按鈕還能按，可以再試', async () => {
    // Arrange
    serveAuthorization();
    server.use(http.post('/v1/oauth/authorization/deny', () => authorizationFailure('這個授權請求已經過期。', null, 'invalid_request')));
    const { user } = await openConsent();

    // Act
    await user.click(screen.getByRole('button', { name: '拒絕' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('這個授權請求已經過期。');
    expect(navigateTo).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: '拒絕' })).toBeEnabled();
    expect(screen.getByRole('button', { name: '允許' })).toBeEnabled();
  });

  it('同一個 task 連按三次「允許」只送一次', async () => {
    // Arrange
    const backend = serveAuthorization();
    await openConsent();
    const allow = screen.getByRole('button', { name: '允許' });

    // Act
    act(() => {
      allow.click();
      allow.click();
      allow.click();
    });

    // Assert
    await screen.findByText('已經授權，可以回到 Claude Code 了。');
    expect(backend.approvals).toHaveLength(1);
    expect(navigateTo).toHaveBeenCalledTimes(1);
  });

  it('同一個 task 連按「允許」與「拒絕」只送最先的那一個', async () => {
    // Arrange
    const backend = serveAuthorization();
    await openConsent();

    // Act
    act(() => {
      screen.getByRole('button', { name: '拒絕' }).click();
      screen.getByRole('button', { name: '允許' }).click();
    });

    // Assert
    await screen.findByText('已經拒絕，可以回到 Claude Code 了。');
    expect(backend.denials).toHaveLength(1);
    expect(backend.approvals).toHaveLength(0);
  });

  it('伺服器給的導回網址不是 http(s)：不導頁、失敗時沒有「回到應用」', async () => {
    // Arrange
    serveAuthorization({ approveRedirect: 'javascript:alert(1)' });
    const { user } = await openConsent();

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    await screen.findByText('已經授權，可以回到 Claude Code 了。');
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('失敗帶的 redirectTo 不是 http(s)：當作沒有導回網址', async () => {
    // Arrange
    serveAuthorization();
    server.use(http.post('/v1/oauth/authorization/approve', () => authorizationFailure('不行。', 'javascript:alert(1)', 'access_denied')));
    const { user } = await openConsent();

    // Act
    await user.click(screen.getByRole('button', { name: '允許' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('不行。');
    expect(screen.queryByRole('button', { name: '回到應用' })).not.toBeInTheDocument();
  });
});

describe('同意頁：檢查失敗', () => {
  it('不能導回時只顯示錯誤，沒有範圍與按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.post('/v1/oauth/authorization/check', () => authorizationFailure('不認得這個應用。', null, 'invalid_client')));

    // Act
    renderApp(PATH);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('這個授權請求有問題：不認得這個應用。');
    expect(screen.queryByRole('button', { name: '允許' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '回到應用' })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('檢查失敗帶的 redirectTo 不是 http(s) 時沒有「回到應用」', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.post('/v1/oauth/authorization/check', () => authorizationFailure('範圍都不認得。', 'data:text/html,x', 'invalid_scope')));

    // Act
    renderApp(PATH);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('範圍都不認得。');
    expect(screen.queryByRole('button', { name: '回到應用' })).not.toBeInTheDocument();
  });

  it('檢查中的畫面：區塊的名稱指到存在的標題', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.post('/v1/oauth/authorization/check', async () => {
      await delay('infinite');
      return ok(authorizationCheckOf());
    }));

    // Act
    renderApp(PATH);

    // Assert
    const panel = await screen.findByRole('region', { name: '授權' });
    expect(panel).not.toHaveAttribute('aria-labelledby');
    expect(screen.getByText('正在確認授權請求…')).toBeInTheDocument();
  });

  it('能導回時有「回到應用」', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const back = 'https://app.example/cb?error=invalid_scope&state=xyz';
    server.use(http.post('/v1/oauth/authorization/check', () => authorizationFailure('範圍都不認得。', back, 'invalid_scope')));
    const { user } = renderApp(PATH);

    // Act
    await user.click(await screen.findByRole('button', { name: '回到應用' }));

    // Assert
    expect(navigateTo).toHaveBeenCalledExactlyOnceWith(back);
    expect(screen.queryByRole('button', { name: '允許' })).not.toBeInTheDocument();
  });
});

describe('同意頁：沒登入', () => {
  it('導到 /login?next=（帶著完整的查詢字串），登入後回到同意頁', async () => {
    // Arrange
    signedOut();
    serveAuthorization();
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
    expect(await screen.findByRole('heading', { level: 1, name: /授權 Claude Code/ })).toBeInTheDocument();
    expect(within(document.body).queryByRole('heading', { name: '登入' })).not.toBeInTheDocument();
  });
});
