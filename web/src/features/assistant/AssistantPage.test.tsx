import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { failure, signedInAs } from '../../test/api';
import { server } from '../../test/server';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import {
  CLAUDE_SELECTION,
  completeReply,
  makeAiOptions,
  makeAssistantMessage,
  makeConversationSummary,
  makeMessage,
  rejectReply,
  serveAiOptions,
  serveConversation,
  serveConversations,
  serveCreateConversation,
  serveDeleteConversation,
  serveReply,
} from '../../test/assistant';
import { SELECTION_STORAGE_KEY } from './selection';

const NEW = makeConversationSummary({ id: 'conv-new' });

function openAssistant(path = '/projects/SMK/assistant', detail = smkDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveAiOptions();
  serveConversations('SMK', []);
  return renderApp(path);
}

async function typeMessage(user: ReturnType<typeof renderApp>['user'], text: string) {
  const input = await screen.findByRole('textbox', { name: '訊息' });
  await user.click(input);
  await user.type(input, text);
  return input;
}

describe('進入頁面', () => {
  it('組織沒開 AI：說明原因，不顯示輸入框', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveConversations('SMK', []);

    // Act
    renderApp('/projects/SMK/assistant');

    // Assert
    expect(await screen.findByText(/組織還沒開放可以用的 AI/)).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '訊息' })).not.toBeInTheDocument();
  });

  it('沒有 blueprint.read：說明沒有權限', async () => {
    // Arrange / Act
    openAssistant('/projects/SMK/assistant', smkDetail({ myPermissions: ['item.read_own'] }));

    // Assert
    expect(await screen.findByText(/沒有讀取這個專案設定的權限/)).toBeInTheDocument();
  });

  it('沒有對話：說明與三個例子，按例子填進輸入框而不送出', async () => {
    // Arrange
    const { user } = openAssistant();
    const sent = serveReply('SMK', 'conv-x', () => undefined);

    // Act
    await user.click(await screen.findByRole('button', { name: '加一個「客戶」單選欄位' }));

    // Assert
    expect(screen.getByRole('textbox', { name: '訊息' })).toHaveValue('加一個「客戶」單選欄位');
    expect(sent.sent).toHaveLength(0);
    expect(screen.getAllByRole('list', { name: '例子' })[0]?.querySelectorAll('li')).toHaveLength(3);
  });
});

describe('送出與串流', () => {
  it('沒有對話時先開一則、換網址再送；回覆一段一段長出來，結束後有模型與思考深度的小字', async () => {
    // Arrange
    const created = serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好，我先讀目前的設定。', makeMessage({ id: 'u1', parts: [{ type: 'text', text: '拆驗收' }] }));
    });
    const { user, router } = openAssistant();

    // Act
    const input = await typeMessage(user, '拆驗收');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByText('好，我先讀目前的設定。')).toBeInTheDocument();
    expect(await screen.findByText('Claude Opus 5.5 · 高')).toBeInTheDocument();
    expect(created).toHaveLength(1);
    expect(currentPath(router)).toBe('/projects/SMK/assistant/conv-new');
    expect(screen.getByText('拆驗收')).toBeInTheDocument();
    expect(input).toHaveValue('');
  });

  it('送出帶著 CSRF 標頭、文字去掉前後空白與目前選的模型', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好');
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, '  你好  ');
    await user.click(screen.getByRole('button', { name: '送出' }));

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body).toEqual({ text: '你好', selection: CLAUDE_SELECTION, itemKey: null, attachmentIds: [] });
    expect(reply.sent[0]?.headers.get('X-PmTool-Csrf')).toBe('1');
  });

  it('工具一列一列、進行中接「…中」；提出變更集後有卡片連到變更集頁', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const tool = { type: 'tool', callId: 'c1', name: 'get_blueprint', status: 'running', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null } as const;
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user } = openAssistant();
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });

    // Act
    reply.stream()?.event('start', { userMessage: makeMessage(), assistantMessageId: 'a1' });
    reply.stream()?.event('tool', tool);

    // Assert
    expect(await screen.findByText('讀取目前的設定…中')).toBeInTheDocument();

    // Act
    reply.stream()?.event('tool', { ...tool, status: 'done' });
    reply.stream()?.event('tool', { ...tool, callId: 'c2', name: 'propose_changeset', status: 'done', changeSetNumber: 12 });
    reply.stream()?.event('done', {
      message: makeAssistantMessage({
        id: 'a1',
        parts: [tool, { ...tool, callId: 'c2', name: 'propose_changeset', status: 'done', changeSetNumber: 12 }].map((part) => ({
          ...part,
          status: 'done' as const,
        })),
      }),
    });
    reply.stream()?.close();

    // Assert
    expect(await screen.findByText('變更集 #12 等待核准')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '查看變更集' })).toHaveAttribute('href', '/projects/SMK/changesets/12');
    expect(screen.queryByText('讀取目前的設定…中')).not.toBeInTheDocument();
  });

  it('失敗的工具顯示原因；失敗的回覆用警告顯示原因', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage(), assistantMessageId: 'a1' });
      stream.event('done', {
        message: makeAssistantMessage({
          id: 'a1',
          status: 'failed',
          error: '模型拒絕了這個要求',
          parts: [
            { type: 'tool', callId: 'c1', name: 'preview_changeset', status: 'failed', changeSetNumber: null, error: '設定格式不對', itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null },
          ],
        }),
      });
      stream.close();
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByText('：設定格式不對')).toBeInTheDocument();
    expect(screen.getByText('模型拒絕了這個要求')).toBeInTheDocument();
  });

  it('按停止：中斷請求，重抓這則對話，顯示伺服器存下來的「已停止」', async () => {
    // Arrange
    const stopped = makeAssistantMessage({ id: 'a1', status: 'stopped', parts: [{ type: 'text', text: '先讀到這裡' }] });
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('text', { delta: '先讀到這裡' });
    });
    const { user } = openAssistant();
    await typeMessage(user, '開始');
    await user.keyboard('{Enter}');
    expect(await screen.findByText('先讀到這裡')).toBeInTheDocument();
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-new', title: '開始', messageCount: 2 }),
      messages: [makeMessage({ id: 'u1', parts: [{ type: 'text', text: '開始' }] }), stopped],
    });

    // Act
    await user.click(screen.getByRole('button', { name: '停止' }));

    // Assert
    expect(await screen.findByText('Claude Opus 5.5 · 高 · 已停止')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '送出' })).toBeInTheDocument();
    expect(reply.stream()).not.toBeNull();
  });

  it('串流中途斷線（沒收到 done）：重抓這則對話', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('text', { delta: '寫到一半' });
      stream.close();
    });
    const read = serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-new', title: 'x', messageCount: 2 }),
      messages: [makeMessage({ id: 'u1' }), makeAssistantMessage({ id: 'a1', parts: [{ type: 'text', text: '伺服器存下來的完整回覆' }] })],
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByText('伺服器存下來的完整回覆')).toBeInTheDocument();
    expect(read.reads()).toBeGreaterThan(0);
  });
});

describe('送出之前的失敗', () => {
  it('409：輸入框上面用 Notice 說明原因，輸入的字留著', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    rejectReply('SMK', 'conv-new', 409, 'ai.conversation_busy', '這則對話還在回覆。');
    const { user } = openAssistant();

    // Act
    const input = await typeMessage(user, '留著的字');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('這則對話還在回覆。');
    expect(input).toHaveValue('留著的字');
  });

  it('409 ai.usage_limit_reached：顯示伺服器說的是哪一項，輸入的字留著', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    rejectReply('SMK', 'conv-new', 409, 'ai.usage_limit_reached', '你今天的 AI 回覆已經到了上限（200 則）。');
    const { user } = openAssistant();

    // Act
    const input = await typeMessage(user, '要保留的字');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('你今天的 AI 回覆已經到了上限（200 則）。');
    expect(input).toHaveValue('要保留的字');
  });

  it('開不了對話（422 ai.unavailable）：說明原因，字留著', async () => {
    // Arrange
    server.use(http.post('/v1/projects/SMK/assistant/conversations', () => failure(422, 'ai.unavailable', 'AI 助理沒有開啟。')));
    const { user } = openAssistant();

    // Act
    const input = await typeMessage(user, '字');
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('AI 助理沒有開啟。');
    expect(input).toHaveValue('字');
  });
});

describe('輸入框的按鍵', () => {
  it('空白時送出停用', async () => {
    openAssistant();
    expect(await screen.findByRole('button', { name: '送出' })).toBeDisabled();
  });

  it('Shift+Enter 換行、不送出', async () => {
    // Arrange
    const created = serveCreateConversation('SMK', NEW);
    const { user } = openAssistant();

    // Act
    const input = await typeMessage(user, '第一行');
    await user.keyboard('{Shift>}{Enter}{/Shift}第二行');

    // Assert
    expect(input).toHaveValue('第一行\n第二行');
    expect(created).toHaveLength(0);
  });

  it('輸入法組字中按 Enter 不送出（isComposing 與 keyCode 229 都算）', async () => {
    // Arrange
    const created = serveCreateConversation('SMK', NEW);
    const { user } = openAssistant();
    const input = await typeMessage(user, 'ㄋㄧ');

    // Act
    fireEvent.keyDown(input, { key: 'Enter', isComposing: true });
    fireEvent.keyDown(input, { key: 'Enter', keyCode: 229 });

    // Assert
    expect(created).toHaveLength(0);
    expect(input).toHaveValue('ㄋㄧ');
  });
});

describe('模型與思考深度', () => {
  it('預設用組織的預設；換了記在瀏覽器；換成不能選思考深度的模型就不顯示思考深度', async () => {
    // Arrange
    const { user } = openAssistant();
    const model = await screen.findByRole('combobox', { name: '模型' });
    expect(model).toHaveTextContent('Claude Opus 5.5');
    expect(screen.getByRole('combobox', { name: '思考深度' })).toHaveTextContent('高');

    // Act
    await user.click(model);
    await user.click(await screen.findByRole('option', { name: 'Claude Haiku 5' }));

    // Assert
    expect(screen.queryByRole('combobox', { name: '思考深度' })).not.toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem(SELECTION_STORAGE_KEY) ?? 'null')).toEqual({
      connectionId: 'conn-1',
      model: 'claude-haiku-5',
      effort: null,
    });
  });

  it('記的選擇現在不能選了：退回組織的預設', async () => {
    // Arrange
    localStorage.setItem(SELECTION_STORAGE_KEY, JSON.stringify({ connectionId: 'gone', model: 'x', effort: null }));

    // Act
    openAssistant();

    // Assert
    expect(await screen.findByRole('combobox', { name: '模型' })).toHaveTextContent('Claude Opus 5.5');
  });

  it('記的選擇還能用：用它送出', async () => {
    // Arrange
    localStorage.setItem(SELECTION_STORAGE_KEY, JSON.stringify({ connectionId: 'conn-1', model: 'claude-haiku-5', effort: null }));
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好');
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent[0]?.body.selection.model).toBe('claude-haiku-5');
    });
  });

  it('兩個以上的連線：模型依連線分組，回覆下面的小字前面加連線名稱', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const options = makeAiOptions();
    serveAiOptions({
      ...options,
      connections: [
        ...options.connections,
        { id: 'conn-2', name: '自架', kind: 'openai_compatible', personal: false, hosted: false, notice: null, models: [{ id: 'local', displayName: 'Llama', efforts: [], defaultEffort: null }] },
      ],
    });
    serveConversations('SMK', [makeConversationSummary({ id: 'conv-1', title: '舊的', messageCount: 2 })]);
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', title: '舊的', messageCount: 2 }),
      messages: [makeMessage(), makeAssistantMessage()],
    });
    const { user } = renderApp('/projects/SMK/assistant/conv-1');

    // Act
    await user.click(await screen.findByRole('combobox', { name: '模型' }));

    // Assert
    const list = await screen.findByRole('listbox');
    expect(within(list).getByText('自架')).toBeInTheDocument();
    expect(await screen.findByText('Anthropic · Claude Opus 5.5 · 高')).toBeInTheDocument();
  });
});

describe('對話清單', () => {
  const OLD = makeConversationSummary({ id: 'conv-1', title: '拆驗收', messageCount: 2, isRunning: false });

  function openWithList() {
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    serveConversations('SMK', [OLD, makeConversationSummary({ id: 'conv-2', title: null, isRunning: true })]);
    serveConversation('SMK', { conversation: OLD, messages: [makeMessage(), makeAssistantMessage()] });
    return renderApp('/projects/SMK/assistant/conv-1');
  }

  it('列出對話：標題、沒標題寫「新的對話」、回覆中有小字，目前的那則標 aria-current', async () => {
    // Act
    openWithList();

    // Assert
    const list = await screen.findByRole('list', { name: '對話' });
    expect(within(list).getByRole('link', { name: /拆驗收/ })).toHaveAttribute('aria-current', 'true');
    expect(within(list).getByText('回覆中')).toBeInTheDocument();
    expect(within(list).getByRole('link', { name: /新的對話/ })).toBeInTheDocument();
  });

  it('刪除要確認；刪掉目前這則會回到「新的對話」', async () => {
    // Arrange
    const deleted = serveDeleteConversation('SMK', 'conv-1');
    const { user, router } = openWithList();
    await user.click(await screen.findByRole('button', { name: '「拆驗收」的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));

    // Act
    expect(deleted).toHaveLength(0);
    await user.click(await screen.findByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(deleted).toEqual(['conv-1']);
    });
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/assistant');
    });
  });

  it('刪除失敗（回覆中）：對話框留著並說明原因', async () => {
    // Arrange
    serveDeleteConversation('SMK', 'conv-1', () => failure(409, 'ai.conversation_busy', '這則對話正在回覆，不能刪除。'));
    const { user } = openWithList();
    await user.click(await screen.findByRole('button', { name: '「拆驗收」的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));

    // Act
    await user.click(await screen.findByRole('button', { name: '刪除' }));

    // Assert
    expect(await screen.findByText('這則對話正在回覆，不能刪除。')).toBeInTheDocument();
  });

  it('找不到的對話：說明找不到', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    serveConversations('SMK', []);
    server.use(
      http.get('/v1/projects/SMK/assistant/conversations/nope', () => failure(404, 'ai.conversation_not_found', '找不到這則對話。')),
    );

    // Act
    renderApp('/projects/SMK/assistant/nope');

    // Assert
    expect(await screen.findByText('找不到這則對話')).toBeInTheDocument();
  });

  it('既有的對話：訊息照原文，助理的文字用 Markdown 畫', async () => {
    // Arrange / Act
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    serveConversations('SMK', [OLD]);
    serveConversation('SMK', {
      conversation: OLD,
      messages: [
        makeMessage({ parts: [{ type: 'text', text: '第一行\n第二行' }] }),
        makeAssistantMessage({ parts: [{ type: 'text', text: '**粗體**字' }] }),
      ],
    });
    renderApp('/projects/SMK/assistant/conv-1');

    // Assert
    expect(await screen.findByText('粗體')).toHaveProperty('tagName', 'STRONG');
    expect(screen.getByText(/第一行/)).toBeInTheDocument();
  });

  it('助理回覆裡的外部連結一律接上實際的網域，使用者自己寫的訊息不接', async () => {
    // Arrange / Act
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    serveConversations('SMK', [OLD]);
    serveConversation('SMK', {
      conversation: OLD,
      messages: [
        makeMessage({ parts: [{ type: 'text', text: '[我的文件](https://mine.example/a)' }] }),
        makeAssistantMessage({ parts: [{ type: 'text', text: '請看 [說明](https://docs.example/a)' }] }),
      ],
    });
    renderApp('/projects/SMK/assistant/conv-1');

    // Assert
    expect(await screen.findByText('（docs.example）')).toBeInTheDocument();
    expect(screen.queryByText('（mine.example）')).toBeNull();
  });
});

describe('回覆中離開', () => {
  it('回覆還沒完成時按連結離開：先確認，確認後中斷', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage({ id: 'u1' }), assistantMessageId: 'a1' });
      stream.event('text', { delta: '寫著' });
    });
    const { user, router } = openAssistant();
    await typeMessage(user, 'x');
    await user.keyboard('{Enter}');
    await screen.findByText('寫著');

    // Act
    await user.click(screen.getByRole('link', { name: '冒煙測試' }));

    // Assert
    expect(await screen.findByText('離開會停止助理的回覆。')).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SMK/assistant/conv-new');

    // Act
    await user.click(screen.getByRole('button', { name: '停止並離開' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toMatch(/^\/projects\/SMK/);
    });
    expect(currentPath(router)).not.toContain('assistant');
    expect(reply.stream()).not.toBeNull();
  });
});
