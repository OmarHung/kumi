import { waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/http';
import type { ClientErrorReport } from '../api/types';
import { server } from '../test/server';
import {
  CLIENT_ERRORS_PATH,
  createErrorReporter,
  installErrorReporting,
  MAX_REPORTS_PER_LOAD,
  pageErrorReporter,
  sendClientError,
  setSignedOutCheck,
  type ErrorReporter,
} from './errorReporting';

/** 記下要送出的回報；路徑固定是 /my-work。 */
function recordingReporter(path = '/my-work') {
  const sent: ClientErrorReport[] = [];
  const reporter = createErrorReporter({
    send: (report) => {
      sent.push(report);
      return Promise.resolve();
    },
    currentPath: () => path,
  });
  return { reporter, sent };
}

function errorWithStack(message: string, stack: string): Error {
  const error = new TypeError(message);
  error.stack = stack;
  return error;
}

describe('網頁的錯誤回報：要送什麼', () => {
  it('送出種類、「名稱: 訊息」、堆疊與目前的路徑', () => {
    // Arrange
    const { reporter, sent } = recordingReporter('/projects/WEB');

    // Act
    reporter.report('error', errorWithStack('x is undefined', 'TypeError: x is undefined\n    at render'));

    // Assert
    expect(sent).toEqual([
      {
        kind: 'error',
        message: 'TypeError: x is undefined',
        stack: 'TypeError: x is undefined\n    at render',
        path: '/projects/WEB',
      },
    ]);
  });

  it('API 的失敗（ApiError）是預期中的結果，不回報', () => {
    // Arrange
    const { reporter, sent } = recordingReporter();

    // Act
    reporter.report(
      'unhandled_rejection',
      new ApiError({
        status: 409,
        code: 'person.is_self',
        message: '不能停用自己的帳號。',
      }),
    );

    // Assert
    expect(sent).toEqual([]);
  });

  it('同一個訊息一次載入只送一次', () => {
    // Arrange
    const { reporter, sent } = recordingReporter();

    // Act
    reporter.report('error', new Error('boom'));
    reporter.report('render', new Error('boom'));
    reporter.report('error', new Error('another'));

    // Assert
    expect(sent.map((report) => report.message)).toEqual(['Error: boom', 'Error: another']);
  });

  it(`一次載入最多送 ${String(MAX_REPORTS_PER_LOAD)} 筆`, () => {
    // Arrange
    const { reporter, sent } = recordingReporter();

    // Act
    Array.from({ length: MAX_REPORTS_PER_LOAD + 5 }, (_, index) => {
      reporter.report('error', new Error(`boom ${String(index)}`));
    });

    // Assert
    expect(sent).toHaveLength(MAX_REPORTS_PER_LOAD);
  });

  it('訊息截到 1000 字、堆疊截到 4000 字、路徑截到 500 字', () => {
    // Arrange
    const { reporter, sent } = recordingReporter(`/${'p'.repeat(600)}`);
    const error = new Error('m'.repeat(1200));
    error.stack = 's'.repeat(9000);

    // Act
    reporter.report('error', error);

    // Assert
    const [report] = sent;
    expect(report?.message).toHaveLength(1000);
    expect(report?.stack).toHaveLength(4000);
    expect(report?.path).toHaveLength(500);
  });

  it('截斷的地方剛好切在 emoji 這類代理字元對的中間時，連前半個一起去掉（伺服器不收半個字元）', () => {
    // Arrange
    const { reporter, sent } = recordingReporter(`/${'p'.repeat(498)}😀`);
    const error = new Error(`${'m'.repeat(992)}😀`);
    error.stack = `${'s'.repeat(3999)}😀`;

    // Act
    reporter.report('error', error);

    // Assert
    const [report] = sent;
    const loneHighSurrogate = /[\uD800-\uDBFF]$/;
    // 「Error: 」佔 7 個字：7 ＋ 992 ＋ 😀 的前半個 ＝ 1000，前半個不送。
    expect(report?.message).toBe(`Error: ${'m'.repeat(992)}`);
    expect(report?.stack).toBe('s'.repeat(3999));
    expect(report?.path).toBe(`/${'p'.repeat(498)}`);
    expect([report?.message, report?.stack, report?.path].some((text) => loneHighSurrogate.test(text ?? ''))).toBe(false);
  });

  it.each([
    ['字串', 'contains user@example.com', 'string'],
    ['物件：只送型別與鍵名，不送值', { email: 'user@example.com', status: 401 }, 'Object { email, status }'],
    ['沒有鍵的物件', {}, 'Object'],
    ['陣列', ['a', 'b'], 'Array(2)'],
    // 方法在原型上、不是自己的鍵，所以只看到類別名稱；空的類別 ESLint 不收。
    [
      '自訂類別的實例',
      new (class Response {
        describe(): string {
          return 'example';
        }
      })(),
      'Response',
    ],
    ['undefined', undefined, 'undefined'],
    ['null', null, 'null'],
  ])('丟出來的不是 Error（%s）時，訊息只有型別名稱與鍵名，沒有堆疊', (_name, thrown, message) => {
    // Arrange
    const { reporter, sent } = recordingReporter();

    // Act
    reporter.report('unhandled_rejection', thrown);

    // Assert
    expect(sent).toEqual([{ kind: 'unhandled_rejection', message, stack: null, path: '/my-work' }]);
  });

  it('循環參照的物件也只列鍵名', () => {
    // Arrange
    const { reporter, sent } = recordingReporter();
    const cyclic: Record<string, unknown> = {};
    cyclic.self = cyclic;

    // Act
    reporter.report('unhandled_rejection', cyclic);

    // Assert
    expect(sent[0]?.message).toBe('Object { self }');
  });

  it('只有訊息（瀏覽器給的文字）時照原樣送，沒有堆疊；空的訊息寫「（沒有訊息）」', () => {
    // Arrange
    const { reporter, sent } = recordingReporter();

    // Act
    reporter.reportMessage('error', 'Uncaught SyntaxError: Unexpected token');
    reporter.reportMessage('error', '');

    // Assert
    expect(sent).toEqual([
      {
        kind: 'error',
        message: 'Uncaught SyntaxError: Unexpected token',
        stack: null,
        path: '/my-work',
      },
      { kind: 'error', message: '（沒有訊息）', stack: null, path: '/my-work' },
    ]);
  });

  it('知道沒登入時（例如在登入頁）不送，也不佔一次載入的額度', () => {
    // Arrange
    let isSignedOut = true;
    const sent: ClientErrorReport[] = [];
    const reporter = createErrorReporter({
      send: (report) => {
        sent.push(report);
        return Promise.resolve();
      },
      currentPath: () => '/login',
      isSignedOut: () => isSignedOut,
    });

    // Act
    reporter.report('error', new Error('while signed out'));
    isSignedOut = false;
    reporter.report('error', new Error('while signed in'));
    reporter.report('error', new Error('while signed out'));

    // Assert：沒登入時那一則沒有被記成「送過了」，登入之後同一則照樣送得出去。
    expect(sent.map((report) => report.message)).toEqual(['Error: while signed in', 'Error: while signed out']);
  });
});

describe('網頁的錯誤回報：這一次載入共用的 reporter', () => {
  afterEach(() => {
    setSignedOutCheck(() => false);
  });

  it('setSignedOutCheck 說沒登入時，pageErrorReporter 不送', async () => {
    // Arrange
    const bodies: unknown[] = [];
    server.use(
      http.post(CLIENT_ERRORS_PATH, async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json({ success: true, data: null, error: null });
      }),
    );
    let isSignedOut = true;
    setSignedOutCheck(() => isSignedOut);

    // Act
    pageErrorReporter().report('error', new Error('while signed out'));
    isSignedOut = false;
    pageErrorReporter().report('error', new Error('while signed in'));

    // Assert
    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
    expect(bodies[0]).toMatchObject({ message: 'Error: while signed in' });
  });
});

describe('網頁的錯誤回報：回報本身絕不丟例外', () => {
  it('送出失敗（Promise 失敗）時靜靜放掉', async () => {
    // Arrange
    const reporter = createErrorReporter({
      send: () => Promise.reject(new Error('offline')),
      currentPath: () => '/',
    });

    // Act
    const report = () => {
      reporter.report('error', new Error('boom'));
    };

    // Assert
    expect(report).not.toThrow();
    await Promise.resolve();
  });

  it('送出的函式自己就丟出例外時也不往外丟', () => {
    // Arrange
    const reporter = createErrorReporter({
      send: () => {
        throw new Error('fetch is not defined');
      },
      currentPath: () => '/',
    });

    // Act
    const report = () => {
      reporter.report('error', new Error('boom'));
    };

    // Assert
    expect(report).not.toThrow();
  });
});

describe('網頁的錯誤回報：掛在 window 上', () => {
  let uninstall: () => void = () => undefined;

  afterEach(() => {
    uninstall();
  });

  function install(): ErrorReporter & { readonly calls: [string, unknown][] } {
    const calls: [string, unknown][] = [];
    const reporter = {
      calls,
      report: (kind: string, error: unknown) => {
        calls.push([kind, error]);
      },
      reportMessage: (kind: string, message: string) => {
        calls.push([kind, `message:${message}`]);
      },
    };
    uninstall = installErrorReporting(window, reporter);
    return reporter;
  }

  it('window 的 error 回報例外本身；沒有例外物件時回報瀏覽器給的訊息', () => {
    // Arrange
    const reporter = install();
    const thrown = new Error('boom');

    // Act
    window.dispatchEvent(
      new ErrorEvent('error', {
        error: thrown,
        message: 'Uncaught Error: boom',
      }),
    );
    window.dispatchEvent(
      new ErrorEvent('error', {
        message: 'Uncaught SyntaxError: Unexpected token',
      }),
    );

    // Assert
    expect(reporter.calls).toEqual([
      ['error', thrown],
      ['error', 'message:Uncaught SyntaxError: Unexpected token'],
    ]);
  });

  it.each([
    ['別的網域的腳本', 'Script error.'],
    ['ResizeObserver（Chrome）', 'ResizeObserver loop completed with undelivered notifications.'],
    ['ResizeObserver（舊的說法）', 'ResizeObserver loop limit exceeded'],
  ])('沒有例外物件的雜訊（%s）不回報', (_name, message) => {
    // Arrange
    const reporter = install();

    // Act
    window.dispatchEvent(new ErrorEvent('error', { message }));

    // Assert
    expect(reporter.calls).toEqual([]);
  });

  it('沒有人接的 Promise 失敗回報它的原因', () => {
    // Arrange
    const reporter = install();
    const event = Object.assign(new Event('unhandledrejection'), {
      reason: new Error('rejected'),
    });

    // Act
    window.dispatchEvent(event);

    // Assert
    expect(reporter.calls).toEqual([['unhandled_rejection', event.reason]]);
  });

  it('拿下來之後不再回報', () => {
    // Arrange
    const reporter = install();
    uninstall();

    // Act
    // 不帶例外物件：沒有任何人聽 error 時，Vitest 會把帶著例外物件的 error 事件當成測試沒接住的例外。
    window.dispatchEvent(
      new ErrorEvent('error', {
        message: 'Uncaught SyntaxError: Unexpected token',
      }),
    );

    // Assert
    expect(reporter.calls).toEqual([]);
  });
});

describe('網頁的錯誤回報：送到伺服器', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const report: ClientErrorReport = {
    kind: 'render',
    message: 'Error: boom',
    stack: null,
    path: '/people',
  };

  it('用 fetch 加 keepalive 送 JSON，帶防偽標頭', async () => {
    // Arrange
    const fetchSpy = vi.fn(() => Promise.resolve(new Response(null, { status: 200 })));
    vi.stubGlobal('fetch', fetchSpy);

    // Act
    await sendClientError(report);

    // Assert
    expect(fetchSpy).toHaveBeenCalledWith(CLIENT_ERRORS_PATH, {
      method: 'POST',
      keepalive: true,
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        'X-PmTool-Csrf': '1',
      },
      body: JSON.stringify(report),
    });
  });

  it.each([
    ['401（沒登入）', () => HttpResponse.json({ success: false }, { status: 401 })],
    ['429（太多次）', () => HttpResponse.json({ success: false }, { status: 429 })],
    ['網路失敗', () => HttpResponse.error()],
  ])('%s 時靜靜放掉', async (_name, respond) => {
    // Arrange
    const bodies: unknown[] = [];
    server.use(
      http.post(CLIENT_ERRORS_PATH, async ({ request }) => {
        bodies.push(await request.json());
        return respond();
      }),
    );

    // Act
    const sending = sendClientError(report);

    // Assert
    await expect(sending).resolves.toBeUndefined();
    expect(bodies).toEqual([report]);
  });
});
