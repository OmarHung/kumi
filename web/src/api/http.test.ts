import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { ApiError, apiRequest, onAuthRequired, tooManyAttemptsMessage } from './http';

function fail(status: number, code: string, message: string, details?: unknown) {
  return HttpResponse.json(
    { success: false, data: null, error: { code, message, details } },
    { status },
  );
}

async function catchApiError(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (error) {
    if (error instanceof ApiError) {
      return error;
    }
    throw error;
  }
  throw new Error('預期請求失敗，結果成功了');
}

describe('apiRequest', () => {
  it('回傳成功信封裡的 data', async () => {
    // Arrange
    server.use(
      http.get('/v1/templates', () =>
        HttpResponse.json({ success: true, data: [{ key: 'simple_tasks' }], error: null }),
      ),
    );

    // Act
    const data = await apiRequest<{ key: string }[]>('/v1/templates');

    // Assert
    expect(data).toEqual([{ key: 'simple_tasks' }]);
  });

  it('每個請求都帶防偽標頭，Cookie 只送同一個來源', async () => {
    // Arrange
    const seen = vi.fn<(csrf: string | null, credentials: RequestCredentials) => void>();
    server.use(
      http.get('/v1/projects', ({ request }) => {
        seen(request.headers.get('X-PmTool-Csrf'), request.credentials);
        return HttpResponse.json({ success: true, data: [], error: null });
      }),
    );

    // Act
    await apiRequest('/v1/projects');

    // Assert
    expect(seen).toHaveBeenCalledWith('1', 'same-origin');
  });

  it('有內文時用 JSON 送出', async () => {
    // Arrange
    const received = vi.fn<(contentType: string | null, body: unknown) => void>();
    server.use(
      http.post('/v1/session', async ({ request }) => {
        received(request.headers.get('Content-Type'), await request.json());
        return HttpResponse.json({ success: true, data: { user: null }, error: null });
      }),
    );

    // Act
    await apiRequest('/v1/session', {
      method: 'POST',
      body: { email: 'user@example.com', password: 'example-password' },
    });

    // Assert
    expect(received).toHaveBeenCalledWith('application/json', {
      email: 'user@example.com',
      password: 'example-password',
    });
  });

  it('失敗的信封變成帶狀態碼、代碼、訊息與細項的錯誤', async () => {
    // Arrange
    server.use(
      http.post('/v1/projects', () =>
        fail(422, 'project.key_invalid', '專案代號的格式不正確。', [
          { code: 'project.key_invalid', path: 'key', message: '代號要用大寫英文字母開頭。' },
        ]),
      ),
    );

    // Act
    const error = await catchApiError(apiRequest('/v1/projects', { method: 'POST', body: {} }));

    // Assert
    expect(error).toMatchObject({
      status: 422,
      code: 'project.key_invalid',
      message: '專案代號的格式不正確。',
      details: [{ code: 'project.key_invalid', path: 'key', message: '代號要用大寫英文字母開頭。' }],
    });
  });

  it('信封沒有細項時，細項是空陣列', async () => {
    // Arrange
    server.use(http.get('/v1/projects/WEB', () => fail(404, 'project.not_found', '找不到專案。')));

    // Act
    const error = await catchApiError(apiRequest('/v1/projects/WEB'));

    // Assert
    expect(error.details).toEqual([]);
  });

  it('錯誤信封帶 error.data 時留在 ApiError.data，沒帶時是 undefined', async () => {
    // Arrange
    server.use(
      http.post('/v1/oauth/authorization/check', () =>
        HttpResponse.json(
          { success: false, data: null, error: { code: 'oauth.request_invalid', message: '有問題。', details: [], data: { redirectTo: 'https://app.example/cb' } } },
          { status: 422 },
        ),
      ),
      http.get('/v1/projects/WEB', () => fail(404, 'project.not_found', '找不到專案。')),
    );

    // Act
    const withData = await catchApiError(apiRequest('/v1/oauth/authorization/check', { method: 'POST', body: {} }));
    const withoutData = await catchApiError(apiRequest('/v1/projects/WEB'));

    // Assert
    expect(withData.data).toEqual({ redirectTo: 'https://app.example/cb' });
    expect(withoutData.data).toBeUndefined();
  });

  it('401 auth.required 時通知登入狀態已失效', async () => {
    // Arrange
    const listener = vi.fn();
    const unsubscribe = onAuthRequired(listener);
    server.use(http.get('/v1/my-work', () => fail(401, 'auth.required', '請先登入。')));

    // Act
    const error = await catchApiError(apiRequest('/v1/my-work'));
    unsubscribe();

    // Assert
    expect(error.code).toBe('auth.required');
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('帳號或密碼錯誤的 401 不算登入失效', async () => {
    // Arrange
    const listener = vi.fn();
    const unsubscribe = onAuthRequired(listener);
    server.use(
      http.post('/v1/session', () =>
        fail(401, 'auth.invalid_credentials', 'Email 或密碼不正確。'),
      ),
    );

    // Act
    const error = await catchApiError(apiRequest('/v1/session', { method: 'POST', body: {} }));
    unsubscribe();

    // Assert
    expect(error.message).toBe('Email 或密碼不正確。');
    expect(listener).not.toHaveBeenCalled();
  });

  it('取消訂閱之後就不再通知', async () => {
    // Arrange
    const listener = vi.fn();
    onAuthRequired(listener)();
    server.use(http.get('/v1/my-work', () => fail(401, 'auth.required', '請先登入。')));

    // Act
    await catchApiError(apiRequest('/v1/my-work'));

    // Assert
    expect(listener).not.toHaveBeenCalled();
  });

  it('太頻繁（429）時給看得懂的訊息', async () => {
    // Arrange
    server.use(
      http.get('/v1/projects', () => fail(429, 'request.rate_limited', 'Too many requests')),
    );

    // Act
    const error = await catchApiError(apiRequest('/v1/projects'));

    // Assert
    expect(error).toMatchObject({ status: 429, code: 'request.rate_limited' });
    expect(error.message).toBe('請求太頻繁，請等幾秒再試。');
  });

  it.each([
    ['12', 12],
    [' 30 ', 30],
    ['0', 0],
    ['Wed, 21 Oct 2026 07:28:00 GMT', null],
    ['-5', null],
    ['1.5', null],
    ['', null],
  ])('429 的 Retry-After「%s」讀成 %s 秒（不是幾秒的寫法就當作沒有）', async (header, seconds) => {
    // Arrange
    server.use(
      http.get('/v1/projects', () =>
        HttpResponse.json(
          { success: false, data: null, error: { code: 'request.rate_limited', message: 'Too many requests', details: [] } },
          { status: 429, headers: { 'Retry-After': header } },
        ),
      ),
    );

    // Act
    const error = await catchApiError(apiRequest('/v1/projects'));

    // Assert
    expect(error.retryAfterSeconds).toBe(seconds);
    expect(tooManyAttemptsMessage(error)).toBe(seconds === null ? '嘗試太多次，請稍後再試。' : `嘗試太多次，請在 ${String(seconds)} 秒後再試。`);
  });

  it('沒有 Retry-After 標頭時 retryAfterSeconds 是 null', async () => {
    // Arrange
    server.use(http.get('/v1/projects', () => fail(429, 'request.rate_limited', 'Too many requests')));

    // Act
    const error = await catchApiError(apiRequest('/v1/projects'));

    // Assert
    expect(error.retryAfterSeconds).toBeNull();
  });

  it('伺服器錯誤而且內文不是信封時，仍然給看得懂的訊息', async () => {
    // Arrange
    server.use(http.get('/v1/projects', () => new HttpResponse('Bad Gateway', { status: 502 })));

    // Act
    const error = await catchApiError(apiRequest('/v1/projects'));

    // Assert
    expect(error.status).toBe(502);
    expect(error.message).toBe('伺服器暫時無法處理，請稍後再試。');
  });

  it('回應不是信封時當成格式錯誤', async () => {
    // Arrange
    server.use(http.get('/v1/projects', () => HttpResponse.json({ items: [] })));

    // Act
    const error = await catchApiError(apiRequest('/v1/projects'));

    // Assert
    expect(error).toMatchObject({ status: 200, code: 'client.invalid_response' });
  });

  it('信封裡的錯誤缺少代碼時當成格式錯誤', async () => {
    // Arrange
    server.use(
      http.get('/v1/projects', () =>
        HttpResponse.json({ success: false, data: null, error: { message: 'x' } }, { status: 400 }),
      ),
    );

    // Act
    const error = await catchApiError(apiRequest('/v1/projects'));

    // Assert
    expect(error.code).toBe('client.invalid_response');
  });

  it('連不上伺服器時丟出網路錯誤', async () => {
    // Arrange
    server.use(http.get('/v1/projects', () => HttpResponse.error()));

    // Act
    const error = await catchApiError(apiRequest('/v1/projects'));

    // Assert
    expect(error).toMatchObject({ status: 0, code: 'client.network' });
    expect(error.message).toBe('連不上伺服器，請確認網路連線後再試。');
  });

  it('204 沒有內文時回傳 null', async () => {
    // Arrange
    server.use(http.delete('/v1/session', () => new HttpResponse(null, { status: 204 })));

    // Act
    const data = await apiRequest('/v1/session', { method: 'DELETE' });

    // Assert
    expect(data).toBeNull();
  });
});
