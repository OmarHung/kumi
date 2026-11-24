import { afterEach, describe, expect, it, vi } from 'vitest';
import { STREAM_IDLE_TIMEOUT_MS, streamAssistantReply } from './assistant';

/** 一直沒有資料、也不結束的串流；連線被中斷（abort）時讀取才失敗。 */
function hangingResponse(signal: AbortSignal, firstChunk: string | null): Response {
  const encoder = new TextEncoder();
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      if (firstChunk !== null) controller.enqueue(encoder.encode(firstChunk));
      signal.addEventListener('abort', () => {
        controller.error(new DOMException('aborted', 'AbortError'));
      });
    },
  });
  return new Response(body, { headers: { 'Content-Type': 'text/event-stream' } });
}

function stubFetch(firstChunk: string | null) {
  vi.stubGlobal(
    'fetch',
    vi.fn((_path: string, init: RequestInit) => Promise.resolve(hangingResponse(init.signal as AbortSignal, firstChunk))),
  );
}

const START = 'event: start\ndata: {"userMessage":{"id":"u"},"assistantMessageId":"a"}\n\n';
const INPUT = { text: 'x', selection: { connectionId: 'c', model: 'm', effort: null }, itemKey: null };

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('串流的閒置逾時', () => {
  it('開始之後超過逾時時間都沒有資料：自己中斷，回傳 incomplete（要重抓對話）', async () => {
    // Arrange
    vi.useFakeTimers();
    stubFetch(START);
    const events: string[] = [];
    const pending = streamAssistantReply('SMK', 'c1', INPUT, new AbortController().signal, (event) => events.push(event.type));

    // Act
    await vi.advanceTimersByTimeAsync(STREAM_IDLE_TIMEOUT_MS - 1000);
    const early = await Promise.race([pending, Promise.resolve('pending')]);
    await vi.advanceTimersByTimeAsync(1500);

    // Assert
    expect(early).toBe('pending');
    expect(await pending).toBe('incomplete');
    expect(events).toEqual(['start']);
  });

  it('使用者按停止：aborted，不是 incomplete', async () => {
    // Arrange
    vi.useFakeTimers();
    stubFetch(START);
    const controller = new AbortController();
    const pending = streamAssistantReply('SMK', 'c1', INPUT, controller.signal, () => undefined);

    // Act
    await vi.advanceTimersByTimeAsync(1000);
    controller.abort();

    // Assert
    expect(await pending).toBe('aborted');
  });

  it('還沒收到 start 就逾時：丟錯誤（字留著）', async () => {
    // Arrange
    vi.useFakeTimers();
    stubFetch(': ping\n\n');
    const pending = streamAssistantReply('SMK', 'c1', INPUT, new AbortController().signal, () => undefined);
    const caught = pending.catch((error: unknown) => error);

    // Act
    await vi.advanceTimersByTimeAsync(STREAM_IDLE_TIMEOUT_MS + 1000);

    // Assert
    expect(await caught).toMatchObject({ message: '回覆的連線中斷了，請再試一次。' });
  });
});
