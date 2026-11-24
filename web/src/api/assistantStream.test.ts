import { describe, expect, it } from 'vitest';
import { createSseParser, toStreamEvent } from './assistantStream';

const STREAM =
  'event: start\ndata: {"userMessage":{"id":"u"},"assistantMessageId":"a"}\n\n' +
  ': ping\n\n' +
  'event: text\ndata: {"delta":"你好"}\n\n' +
  'event: done\ndata: {"message":{"id":"a"}}\n\n';

function parseAll(chunks: readonly string[]) {
  const parser = createSseParser();
  return chunks.flatMap((chunk) => parser.push(chunk));
}

describe('SSE 的解析', () => {
  it('不管從哪個位置切開，解出來的事件都一樣', () => {
    // Arrange
    const expected = parseAll([STREAM]);

    // Act / Assert
    for (let at = 1; at < STREAM.length; at += 1) {
      expect(parseAll([STREAM.slice(0, at), STREAM.slice(at)])).toEqual(expected);
    }
    expect(expected.map((message) => message.event)).toEqual(['start', 'text', 'done']);
  });

  it('一次一個字元地送也解得出來', () => {
    expect(parseAll(STREAM.split('')).map((message) => message.event)).toEqual(['start', 'text', 'done']);
  });

  it('忽略冒號開頭的心跳，心跳不算事件', () => {
    expect(parseAll([': ping\n\n', ': ping\n\n'])).toEqual([]);
  });

  it('行尾是 CRLF 或 CR 也一樣；CRLF 被切在兩段中間也不多出事件', () => {
    const crlf = 'event: text\r\ndata: {"delta":"a"}\r\n\r\n';
    expect(parseAll([crlf])).toHaveLength(1);
    expect(parseAll([crlf.slice(0, crlf.length - 1), crlf.slice(-1)])).toHaveLength(1);
    // 結尾的 \r 要等下一個字元才知道是不是 \r\n，所以後面接著下一個事件的開頭。
    expect(parseAll(['event: text\rdata: {"delta":"a"}\r\revent: x'])).toHaveLength(1);
  });

  it('還沒收到空行的事件留著，等下一段', () => {
    const parser = createSseParser();
    expect(parser.push('event: text\ndata: {"delta":"a"}\n')).toEqual([]);
    expect(parser.push('\n')).toHaveLength(1);
  });

  it('多行的 data 用換行接起來', () => {
    expect(parseAll(['event: x\ndata: a\ndata: b\n\n'])).toEqual([{ event: 'x', data: 'a\nb' }]);
  });
});

describe('事件的資料', () => {
  it('認得 start、text、tool、done', () => {
    expect(toStreamEvent({ event: 'text', data: '{"delta":"字"}' })).toEqual({ type: 'text', delta: '字' });
    expect(
      toStreamEvent({
        event: 'tool',
        data: '{"type":"tool","callId":"c1","name":"get_blueprint","status":"running","changeSetNumber":null,"error":null}',
      }),
    ).toMatchObject({
      type: 'tool',
      tool: { callId: 'c1' },
    });
    expect(toStreamEvent({ event: 'start', data: '{"userMessage":{"id":"u"},"assistantMessageId":"a"}' })).toMatchObject({
      type: 'start',
      assistantMessageId: 'a',
    });
    expect(toStreamEvent({ event: 'done', data: '{"message":{"id":"a"}}' })).toMatchObject({ type: 'done' });
  });

  it('認得主機來源的 queued（前面的位置）與 started', () => {
    expect(toStreamEvent({ event: 'queued', data: '{"position":3}' })).toEqual({ type: 'queued', position: 3 });
    expect(toStreamEvent({ event: 'started', data: '{}' })).toEqual({ type: 'started' });
    expect(toStreamEvent({ event: 'queued', data: '{"position":"3"}' })).toBeNull();
  });

  it('不認得的事件與格式不對的資料略過，不丟錯誤', () => {
    expect(toStreamEvent({ event: 'future', data: '{}' })).toBeNull();
    expect(toStreamEvent({ event: 'text', data: 'not json' })).toBeNull();
    expect(toStreamEvent({ event: 'text', data: '{"delta":1}' })).toBeNull();
  });
});
