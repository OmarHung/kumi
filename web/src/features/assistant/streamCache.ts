import type { AssistantConversation, AssistantMessage, AssistantPart, AssistantStreamEvent, AssistantToolPart } from '../../api/aiTypes';

/** 串流還沒結束時，快取裡那則助理的訊息：沒有狀態、沒有選擇，內容一段一段長出來。 */
function placeholder(id: string, createdAt: string): AssistantMessage {
  return {
    id,
    role: 'assistant',
    createdAt,
    parts: [],
    status: null,
    error: null,
    selection: null,
    connectionName: null,
    usage: null,
    itemKey: null,
    attachments: [],
  };
}

function appendText(parts: readonly AssistantPart[], delta: string): readonly AssistantPart[] {
  const last = parts[parts.length - 1];
  if (last?.type === 'text') return [...parts.slice(0, -1), { type: 'text', text: last.text + delta }];
  return [...parts, { type: 'text', text: delta }];
}

function upsertTool(parts: readonly AssistantPart[], tool: AssistantToolPart): readonly AssistantPart[] {
  const exists = parts.some((part) => part.type === 'tool' && part.callId === tool.callId);
  return exists ? parts.map((part) => (part.type === 'tool' && part.callId === tool.callId ? tool : part)) : [...parts, tool];
}

function updateMessage(
  conversation: AssistantConversation,
  id: string,
  change: (message: AssistantMessage) => AssistantMessage,
): AssistantConversation {
  return {
    ...conversation,
    messages: conversation.messages.map((message) => (message.id === id ? change(message) : message)),
  };
}

/**
 * 把串流的一個事件套進快取的對話（不改原本的物件）。start 放進使用者的訊息與一則空的助理訊息；
 * text、tool 更新那則（messageId 是 start 給的）；done 換成伺服器存下來的那一則。
 * 重複收到同一個 start 或 done 不會多出一則訊息。
 */
export function applyStreamEvent(
  conversation: AssistantConversation,
  event: AssistantStreamEvent,
  messageId: string | null,
): AssistantConversation {
  switch (event.type) {
    case 'start': {
      const known = new Set(conversation.messages.map((message) => message.id));
      const added = [event.userMessage, placeholder(event.assistantMessageId, event.userMessage.createdAt)].filter(
        (message) => !known.has(message.id),
      );
      return {
        conversation: { ...conversation.conversation, isRunning: true },
        messages: [...conversation.messages, ...added],
      };
    }
    case 'text':
      return messageId === null
        ? conversation
        : updateMessage(conversation, messageId, (message) => ({
            ...message,
            parts: appendText(message.parts, event.delta),
          }));
    case 'tool':
      return messageId === null
        ? conversation
        : updateMessage(conversation, messageId, (message) => ({
            ...message,
            parts: upsertTool(message.parts, event.tool),
          }));
    // 排隊與開始執行只影響畫面上的狀態（useAssistantReply 的 phase），不改快取裡的訊息。
    case 'queued':
    case 'started':
      return conversation;
    case 'done': {
      const has = conversation.messages.some((message) => message.id === event.message.id);
      const messages = has
        ? conversation.messages.map((message) => (message.id === event.message.id ? event.message : message))
        : [...conversation.messages, event.message];
      return {
        conversation: { ...conversation.conversation, isRunning: false },
        messages,
      };
    }
  }
}

/**
 * 停止之後才讀到的伺服器版本，併進已經有下一輪的快取：快取裡有的訊息換成伺服器的（已存好的狀態、完整的內容），
 * 正在長的那一則與伺服器沒有的訊息（下一輪的）照舊。不新增訊息、不改順序。
 */
export function mergeStored(current: AssistantConversation, stored: AssistantConversation, activeMessageId: string | null): AssistantConversation {
  const byId = new Map(stored.messages.map((message) => [message.id, message]));
  return {
    ...current,
    messages: current.messages.map((message) => (message.id === activeMessageId ? message : (byId.get(message.id) ?? message))),
  };
}
