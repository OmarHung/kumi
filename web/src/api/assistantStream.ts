import type { AssistantMessage, AssistantStreamEvent, AssistantToolPart } from './aiTypes';

/** SSE 的一則事件：event: 與 data: 兩行（沒寫 event 的是 message）。 */
export interface SseMessage {
  readonly event: string;
  readonly data: string;
}

export interface SseParser {
  /** 餵進一段文字（可以在任何位置切開），回傳這一段湊齊了的事件。 */
  readonly push: (chunk: string) => readonly SseMessage[];
}

const DEFAULT_EVENT = 'message';

// 行尾可以是 \n、\r\n、\r；一律先換成 \n，事件就是以空行（連續兩個 \n）分開。
// 結尾剛好是 \r 時先留著：下一段開頭可能是配對的 \n，現在換掉會多出一個空行。
function normalize(text: string): {
  readonly ready: string;
  readonly rest: string;
} {
  const endsWithCr = text.endsWith('\r');
  const body = endsWithCr ? text.slice(0, -1) : text;
  return { ready: body.replace(/\r\n?/g, '\n'), rest: endsWithCr ? '\r' : '' };
}

function fieldOf(line: string): {
  readonly name: string;
  readonly value: string;
} {
  const colon = line.indexOf(':');
  if (colon < 0) return { name: line, value: '' };
  const raw = line.slice(colon + 1);
  return {
    name: line.slice(0, colon),
    value: raw.startsWith(' ') ? raw.slice(1) : raw,
  };
}

function parseBlock(block: string): SseMessage | null {
  let event = DEFAULT_EVENT;
  const data: string[] = [];
  for (const line of block.split('\n')) {
    // 冒號開頭的行是註解（心跳 `: ping`），不是事件。
    if (line === '' || line.startsWith(':')) continue;
    const { name, value } = fieldOf(line);
    if (name === 'event') event = value;
    if (name === 'data') data.push(value);
  }
  return data.length === 0 ? null : { event, data: data.join('\n') };
}

/** 伺服器送來的 text/event-stream 的解析：回應是一段一段到的，事件可能被切在任何位置。 */
export function createSseParser(): SseParser {
  let pending = '';
  return {
    push: (chunk) => {
      const { ready, rest } = normalize(pending + chunk);
      const blocks = ready.split('\n\n');
      // 最後一塊還沒收到空行：留到下一段。
      pending = (blocks.pop() ?? '') + rest;
      return blocks.flatMap((block) => {
        const message = parseBlock(block);
        return message === null ? [] : [message];
      });
    },
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

/**
 * SSE 事件換成型別化的事件。不認得的事件、格式不對的資料回 null（略過）：之後伺服器多送一種事件，舊的網頁不會壞。
 * 資料的形狀由合約保證，這裡只確認會用到的欄位在。
 */
export function toStreamEvent(message: SseMessage): AssistantStreamEvent | null {
  const data = parseJson(message.data);
  if (!isRecord(data)) return null;
  switch (message.event) {
    case 'start':
      return isRecord(data.userMessage) && typeof data.assistantMessageId === 'string'
        ? {
            type: 'start',
            userMessage: data.userMessage as unknown as AssistantMessage,
            assistantMessageId: data.assistantMessageId,
          }
        : null;
    case 'text':
      return typeof data.delta === 'string' ? { type: 'text', delta: data.delta } : null;
    case 'tool':
      return typeof data.callId === 'string' && typeof data.name === 'string'
        ? { type: 'tool', tool: data as unknown as AssistantToolPart }
        : null;
    case 'queued':
      return typeof data.position === 'number' ? { type: 'queued', position: data.position } : null;
    case 'started':
      return { type: 'started' };
    case 'done':
      return isRecord(data.message) ? { type: 'done', message: data.message as unknown as AssistantMessage } : null;
    default:
      return null;
  }
}
