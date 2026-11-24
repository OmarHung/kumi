import { screen, waitFor } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { authRequired, failure, ok, signedInAs, signedOut } from '../test/api';
import { exampleOwner, webProject } from '../test/fixtures';
import { flushPending } from '../test/flush';
import { currentPath, renderApp } from '../test/renderApp';
import { server } from '../test/server';

function acceptSignIn() {
  const received = vi.fn<(body: unknown) => void>();
  server.use(
    http.post('/v1/session', async ({ request }) => {
      received(await request.json());
      signedInAs(exampleOwner, { projects: [webProject] });
      return ok({ user: exampleOwner });
    }),
  );
  return received;
}

describe('登入頁的「登入已經失效」', () => {
  it('第一次打開（本來就沒登入）時不說明', async () => {
    // Arrange
    signedOut();

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('heading', { name: '登入' })).toBeInTheDocument();
    await flushPending();
    expect(screen.queryByText('登入已經失效，請重新登入。')).not.toBeInTheDocument();
  });

  it('自己按登出時不說明', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.delete('/v1/session', () => ok(null)));
    const { user } = renderApp('/my-work');
    await user.click(await screen.findByRole('button', { name: '範例使用者（範例組織 · 擁有者）' }));

    // Act
    await user.click(screen.getByRole('menuitem', { name: '登出' }));

    // Assert
    expect(await screen.findByRole('heading', { name: '登入' })).toBeInTheDocument();
    await flushPending();
    expect(screen.queryByText('登入已經失效，請重新登入。')).not.toBeInTheDocument();
  });
});

describe('沒登入', () => {
  it.each(['/', '/projects', '/projects/WEB', '/my-work', '/people', '/no-such-page'])(
    '打開 %s 會到登入頁',
    async (path) => {
      // Arrange
      signedOut();

      // Act
      const { router } = renderApp(path);

      // Assert
      expect(await screen.findByRole('heading', { name: '登入' })).toBeInTheDocument();
      expect(router.state.location.pathname).toBe('/login');
    },
  );
});

describe('登入頁', () => {
  it('登入成功後回到原本要去的頁面', async () => {
    // Arrange
    signedOut();
    const received = acceptSignIn();
    const { user, router } = renderApp('/projects');

    // Act
    await user.type(await screen.findByLabelText('Email'), 'user@example.com');
    await user.type(screen.getByLabelText('密碼'), 'example-password');
    await user.click(screen.getByRole('button', { name: '登入' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '專案' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects');
    expect(received).toHaveBeenCalledWith({
      email: 'user@example.com',
      password: 'example-password',
    });
  });

  it('沒有指定要去哪裡時進到我的工作', async () => {
    // Arrange
    signedOut();
    acceptSignIn();
    const { user, router } = renderApp('/login');

    // Act
    await user.type(await screen.findByLabelText('Email'), 'user@example.com');
    await user.type(screen.getByLabelText('密碼'), 'example-password');
    await user.click(screen.getByRole('button', { name: '登入' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/my-work');
  });

  it('要去的網址不是站內路徑時忽略它', async () => {
    // Arrange
    signedOut();
    acceptSignIn();
    const { user, router } = renderApp('/login?next=//evil.example.com');

    // Act
    await user.type(await screen.findByLabelText('Email'), 'user@example.com');
    await user.type(screen.getByLabelText('密碼'), 'example-password');
    await user.click(screen.getByRole('button', { name: '登入' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/my-work');
  });

  it('登入失敗時顯示信封裡的訊息，並和欄位連在一起', async () => {
    // Arrange
    signedOut();
    server.use(
      http.post('/v1/session', () =>
        failure(401, 'auth.invalid_credentials', 'Email 或密碼不正確。'),
      ),
    );
    const { user } = renderApp('/login');

    // Act
    await user.type(await screen.findByLabelText('Email'), 'user@example.com');
    await user.type(screen.getByLabelText('密碼'), 'wrong-password');
    await user.click(screen.getByRole('button', { name: '登入' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Email 或密碼不正確。');
    expect(screen.getByLabelText('密碼')).toHaveAccessibleDescription('Email 或密碼不正確。');
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true');
  });

  it('沒填 Email 或密碼時不送出，直接提示', async () => {
    // Arrange
    signedOut();
    const { user } = renderApp('/login');

    // Act
    await user.click(await screen.findByRole('button', { name: '登入' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('請輸入 Email 與密碼。');
  });

  it('送出中按鈕停用並顯示正在登入', async () => {
    // Arrange
    signedOut();
    let respond: () => void = () => undefined;
    server.use(
      http.post(
        '/v1/session',
        () =>
          new Promise((resolve) => {
            respond = () => {
              resolve(failure(401, 'auth.invalid_credentials', 'Email 或密碼不正確。'));
            };
          }),
      ),
    );
    const { user } = renderApp('/login');
    await user.type(await screen.findByLabelText('Email'), 'user@example.com');
    await user.type(screen.getByLabelText('密碼'), 'example-password');

    // Act
    await user.click(screen.getByRole('button', { name: '登入' }));

    // Assert
    expect(await screen.findByRole('button', { name: '登入中…' })).toBeDisabled();
    respond();
    expect(await screen.findByRole('button', { name: '登入' })).toBeEnabled();
  });

  it('已經登入時打開登入頁直接進主畫面', async () => {
    // Arrange
    signedInAs(exampleOwner);

    // Act
    const { router } = renderApp('/login');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/my-work');
  });

  it('確認登入狀態失敗（不是 401）時顯示錯誤並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(
      http.get('/v1/session', () => failure(503, 'server.not_ready', 'not ready'), { once: true }),
    );
    const { user } = renderApp('/my-work');

    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器暫時無法處理');
    await user.click(screen.getByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
  });
});

describe('登入狀態失效', () => {
  it('任何請求收到 401 時回到登入頁，登入後回到原本的頁面', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    server.use(http.get('/v1/my-work', () => authRequired()));
    const { user, router } = renderApp('/my-work');

    // Act
    const email = await screen.findByLabelText('Email');
    // 已經登入、之後才收到 401：登入頁上方說明為什麼回到這裡。
    expect(screen.getByRole('status')).toHaveTextContent('登入已經失效，請重新登入。');
    signedOut();
    acceptSignIn();
    await user.type(email, 'user@example.com');
    await user.type(screen.getByLabelText('密碼'), 'example-password');
    await user.click(screen.getByRole('button', { name: '登入' }));

    // Assert
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/my-work');
    });
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
  });
});
