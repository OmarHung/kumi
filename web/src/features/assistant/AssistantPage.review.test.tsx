import { screen, waitFor, within } from '@testing-library/react';
import { delay, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { ok, signedInAs } from '../../test/api';
import { server } from '../../test/server';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import {
  completeReply,
  makeAssistantMessage,
  makeConversationSummary,
  makeMessage,
  serveAiOptions,
  serveConversation,
  serveConversations,
  serveCreateConversation,
  serveReply,
} from '../../test/assistant';

// 審查（2026-11-23）找到的問題的測試：送出的防重、停止之後的重讀、焦點、報讀、連線不正常。

const NEW = makeConversationSummary({ id: 'conv-new' });
const CONVERSATIONS = '/v1/projects/SMK/assistant/conversations';

function openAssistant(path = '/projects/SMK/assistant') {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveAiOptions();
  return renderApp(path);
}

async function typeMessage(user: ReturnType<typeof renderApp>['user'], text: string) {
  const input = await screen.findByRole('textbox', { name: '訊息' });
  await user.click(input);
  await user.type(input, text);
  return input;
}

function storedConversation(id: string, messages = [makeMessage({ id: 'u1' }), makeAssistantMessage({ id: 'a1' })]) {
  return { conversation: makeConversationSummary({ id, title: 't', messageCount: messages.length }), messages };
}

describe('H1：連按兩次 Enter', () => {
  it('開對話還沒回來時再按 Enter：只開一則對話、只送一次', async () => {
    // Arrange
    serveConversations('SMK', []);
    let created = 0;
    server.use(
      http.post(CONVERSATIONS, async () => {
        created += 1;
        await delay(300);
        return ok(NEW, 201);
      }),
    );
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好的');
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, '拆驗收');
    await user.keyboard('{Enter}{Enter}');

    // Assert
    expect(await screen.findByText('好的')).toBeInTheDocument();
    expect(created).toBe(1);
    expect(reply.sent).toHaveLength(1);
  });
});

describe('H2 與 N1：停止之後重讀伺服器的對話', () => {
  it('伺服器晚才存「已停止」，停止後立刻送下一則：第二則與它的串流都留在畫面上，之後的文字照樣長出來', async () => {
    // Arrange
    const startedAt = Date.now();
    serveConversations('SMK', [storedConversation('conv-1').conversation]);
    server.use(
      http.get(`${CONVERSATIONS}/conv-1`, () => {
        const isSaved = Date.now() - startedAt > 1500;
        const first = makeAssistantMessage({ id: 'a1', status: 'stopped', parts: [{ type: 'text', text: '第一則' }] });
        return ok(storedConversation('conv-1', isSaved ? [makeMessage({ id: 'u1' }), first] : [makeMessage({ id: 'u1' })]));
      }),
    );
    let round = 0;
    const reply = serveReply('SMK', 'conv-1', (stream) => {
      round += 1;
      stream.event('start', { userMessage: makeMessage({ id: `u${String(round)}`, parts: [{ type: 'text', text: `問${String(round)}` }] }), assistantMessageId: `a${String(round)}` });
      stream.event('text', { delta: round === 1 ? '第一則' : '第二則-前' });
    });
    const { user } = openAssistant('/projects/SMK/assistant/conv-1');
    await screen.findByRole('textbox', { name: '訊息' });
    await typeMessage(user, '問1');
    await user.keyboard('{Enter}');
    await screen.findByText('第一則');

    // Act
    await user.click(screen.getByRole('button', { name: '停止' }));
    await user.type(screen.getByRole('textbox', { name: '訊息' }), '問2');
    await user.keyboard('{Enter}');
    await screen.findByText('第二則-前');
    await new Promise((resolve) => setTimeout(resolve, 1800));
    reply.stream()?.event('text', { delta: '-後' });

    // Assert
    expect(await screen.findByText('第二則-前-後')).toBeInTheDocument();
    expect(screen.getByText('問2')).toBeInTheDocument();
    expect(await screen.findByText('Claude Opus 5.5 · 高 · 已停止')).toBeInTheDocument();
  }, 20_000);

  it('停止之後對話清單那一列的「回覆中」要跟著消失', async () => {
    // Arrange
    let isSaved = false;
    server.use(
      http.get(CONVERSATIONS, () => ok([makeConversationSummary({ id: 'conv-new', title: '開始', isRunning: !isSaved })])),
      http.get(`${CONVERSATIONS}/conv-new`, () => {
        isSaved = true;
        return ok(
          storedConversation('conv-new', [
            makeMessage({ id: 'u1' }),
            makeAssistantMessage({ id: 'a1', status: 'stopped', parts: [{ type: 'text', text: '先讀到這裡' }] }),
          ]),
        );
      }),
    );
    serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('text', { delta: '先讀到這裡' });
    });
    const { user } = openAssistant();
    await typeMessage(user, '開始');
    await user.keyboard('{Enter}');
    await screen.findByText('先讀到這裡');

    // Act
    await user.click(screen.getByRole('button', { name: '停止' }));

    // Assert
    expect(await screen.findByText('Claude Opus 5.5 · 高 · 已停止')).toBeInTheDocument();
    const list = await screen.findByRole('list', { name: '對話' });
    await waitFor(() => {
      expect(within(list).queryByText('回覆中')).not.toBeInTheDocument();
    });
  });
});

describe('M1：報讀', () => {
  function liveRegion() {
    return document.querySelector('[aria-live="polite"].visually-hidden');
  }

  it('只在狀態改變時寫一句，文字一段一段長出來不會改它；完成時說完成與變更集', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user } = openAssistant();
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });

    // Act
    reply.stream()?.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });

    // Assert
    await waitFor(() => {
      expect(liveRegion()).toHaveTextContent('助理正在回覆');
    });
    reply.stream()?.event('text', { delta: '一二三' });
    await screen.findByText('一二三');
    expect(liveRegion()).toHaveTextContent(/^助理正在回覆$/);

    // Act
    const tool = { type: 'tool', callId: 'c2', name: 'propose_changeset', status: 'done', changeSetNumber: 12, error: null, itemProposalId: null, projectProposalId: null } as const;
    reply.stream()?.event('done', { message: makeAssistantMessage({ id: 'a1', parts: [tool] }) });
    reply.stream()?.close();

    // Assert
    await waitFor(() => {
      expect(liveRegion()).toHaveTextContent('助理回覆完成');
    });
    expect(liveRegion()).toHaveTextContent('已提出變更集 #12');
  });

  it('停止說「回覆已停止」；失敗說原因', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    serveConversation('SMK', {
      conversation: NEW,
      messages: [makeMessage({ id: 'u1' }), makeAssistantMessage({ id: 'a1', status: 'stopped', parts: [{ type: 'text', text: '寫著' }] })],
    });
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('text', { delta: '寫著' });
    });
    const { user } = openAssistant();
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');
    await screen.findByText('寫著');

    // Act
    await user.click(screen.getByRole('button', { name: '停止' }));

    // Assert
    await waitFor(() => {
      expect(liveRegion()).toHaveTextContent('回覆已停止');
    });
  });

  it('失敗的回覆：回覆失敗加原因', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('done', { message: makeAssistantMessage({ id: 'a1', status: 'failed', error: '模型拒絕了', parts: [] }) });
      stream.close();
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(liveRegion()).toHaveTextContent('回覆失敗：模型拒絕了');
    });
  });
});

describe('M2：焦點', () => {
  it('點「送出」再等回覆完成：焦點回到輸入框', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user } = openAssistant();
    await typeMessage(user, 'x');
    await user.click(screen.getByRole('button', { name: '送出' }));
    await screen.findByRole('button', { name: '停止' });
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });

    // Act
    const stream = reply.stream();
    if (stream === null) throw new Error('串流還沒開始');
    completeReply(stream, '好');

    // Assert
    await screen.findByText('好');
    await waitFor(() => {
      expect(screen.getByRole('textbox', { name: '訊息' })).toHaveFocus();
    });
  });

  it('按「停止」之後：焦點回到輸入框', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    serveConversation('SMK', {
      conversation: NEW,
      messages: [makeMessage({ id: 'u1' }), makeAssistantMessage({ id: 'a1', status: 'stopped', parts: [{ type: 'text', text: '寫著' }] })],
    });
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('text', { delta: '寫著' });
    });
    const { user } = openAssistant();
    await typeMessage(user, 'x');
    await user.click(screen.getByRole('button', { name: '送出' }));
    await screen.findByText('寫著');

    // Act
    await user.click(screen.getByRole('button', { name: '停止' }));

    // Assert
    await screen.findByRole('button', { name: '送出' });
    await waitFor(() => {
      expect(screen.getByRole('textbox', { name: '訊息' })).toHaveFocus();
    });
  });
});

describe('M5：連線不是串流', () => {
  const MESSAGE = '回覆的連線中斷了，請再試一次。';

  it('200 但不是 text/event-stream（例如代理回 HTML）：輸入框上面說明，字留著', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    server.use(http.post(`${CONVERSATIONS}/conv-new/messages`, () => new Response('<html></html>', { headers: { 'Content-Type': 'text/html' } })));
    const { user } = openAssistant();

    // Act
    const input = await typeMessage(user, '留著');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(MESSAGE);
    expect(input).toHaveValue('留著');
  });

  it('串流沒有 start 就結束：同樣說明，字留著', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      stream.raw(': ping\n\n');
      stream.close();
    });
    const { user } = openAssistant();

    // Act
    const input = await typeMessage(user, '留著');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(MESSAGE);
    expect(input).toHaveValue('留著');
  });
});

describe('L1、L2、L3：輸入框', () => {
  function openExisting() {
    const first = makeConversationSummary({ id: 'conv-1', title: '第一則', messageCount: 2 });
    const second = makeConversationSummary({ id: 'conv-2', title: '第二則', messageCount: 2 });
    serveConversations('SMK', [first, second]);
    serveConversation('SMK', storedConversation('conv-1'));
    serveConversation('SMK', storedConversation('conv-2'));
    return openAssistant('/projects/SMK/assistant/conv-1');
  }

  it('L1：對話還在載入時不能送出', async () => {
    // Arrange
    serveConversations('SMK', []);
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get(`${CONVERSATIONS}/conv-1`, async () => {
        await gate;
        return ok(storedConversation('conv-1'));
      }),
    );
    const reply = serveReply('SMK', 'conv-1', (stream) => {
      completeReply(stream, '好');
    });
    const { user } = openAssistant('/projects/SMK/assistant/conv-1');

    // Act
    await typeMessage(user, '太早');
    await user.keyboard('{Enter}');

    // Assert
    expect(screen.getByRole('button', { name: '送出' })).toBeDisabled();
    expect(reply.sent).toHaveLength(0);
    release();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '送出' })).toBeEnabled();
    });
  });

  it('L2：等待伺服器收下的時候又打的字，收下之後還在', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveConversation('SMK', storedConversation('conv-1'));
    const reply = serveReply('SMK', 'conv-1', () => undefined);
    const { user } = openAssistant('/projects/SMK/assistant/conv-1');
    const input = await typeMessage(user, 'abc');
    await user.keyboard('{Enter}');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });

    // Act
    await user.type(input, 'def');
    reply.stream()?.event('start', { userMessage: makeMessage({ id: 'u9' }), assistantMessageId: 'a9' });

    // Assert
    await waitFor(() => {
      expect(input).toHaveValue('def');
    });
  });

  it('L3：草稿依對話分開，換過去再換回來還在', async () => {
    // Arrange
    const { user } = openExisting();
    await typeMessage(user, '寫一半');

    // Act
    await user.click(await screen.findByRole('link', { name: /第二則/ }));

    // Assert
    expect(await screen.findByRole('textbox', { name: '訊息' })).toHaveValue('');

    // Act
    await user.click(await screen.findByRole('link', { name: /第一則/ }));

    // Assert
    expect(await screen.findByRole('textbox', { name: '訊息' })).toHaveValue('寫一半');
  });
});

describe('L6：離開確認', () => {
  it('說明不重複標題；「停止並離開」不是紅色實心', async () => {
    // Arrange
    serveConversations('SMK', []);
    serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('text', { delta: '寫著' });
    });
    const { user } = openAssistant();
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');
    await screen.findByText('寫著');

    // Act
    await user.click(screen.getByRole('link', { name: '冒煙測試' }));

    // Assert
    expect(await screen.findByText('離開會停止助理的回覆。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '停止並離開' }).className).not.toMatch(/danger/);
  });
});
