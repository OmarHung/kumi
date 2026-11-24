import { screen, waitFor } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { AiOptions } from '../../api/aiTypes';
import { signedInAs } from '../../test/api';
import { exampleMember, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { ok } from '../../test/api';
import { server } from '../../test/server';
import {
  completeReply,
  makeAiOptions,
  makeAssistantMessage,
  makeConversationSummary,
  makeMessage,
  rejectReply,
  serveAiOptions,
  serveConversations,
  serveCreateConversation,
  serveReply,
} from '../../test/assistant';

const NEW = makeConversationSummary({ id: 'conv-new' });
const HOSTED_SELECTION = { connectionId: 'src-1', model: 'opus', effort: null } as const;

/** 成員的助理：只有一個主機來源（預設選它）。 */
function hostedOptions(notice: string | null = null): AiOptions {
  return makeAiOptions({
    default: notice === null ? HOSTED_SELECTION : null,
    connections: [
      {
        id: 'src-1',
        name: '主機上的 Claude Code',
        kind: 'claude_code',
        personal: false,
        hosted: true,
        notice,
        models: [{ id: 'opus', displayName: 'Opus', efforts: [], defaultEffort: null }],
      },
    ],
  });
}

function openAssistant(options: AiOptions) {
  // 成員（不是管理員）：不靠組織的 AI 開關，也看得到主機來源。
  signedInAs(exampleMember, { details: [smkDetail()] });
  serveAiOptions(options);
  serveConversations('SMK', []);
  serveCreateConversation('SMK', NEW);
  return renderApp('/projects/SMK/assistant');
}

/** 畫面上看得到的那一句（報讀軟體用的隱藏區域也有同一句，不算）。 */
async function findVisibleText(text: string) {
  const found = await screen.findAllByText(text);
  const visible = found.find((element) => element.closest('.visually-hidden') === null);
  if (visible === undefined) throw new Error(`畫面上沒有「${text}」`);
  return visible;
}

async function sendMessage(user: ReturnType<typeof renderApp>['user'], text: string) {
  const input = await screen.findByRole('textbox', { name: '訊息' });
  await user.click(input);
  await user.type(input, text);
  await user.keyboard('{Enter}');
}

describe('助理：主機來源的排隊與執行', () => {
  it('queued 顯示前面還有幾件，位置往前變成「下一個就輪到你」，started 之後寫「執行中」，文字照常長出來', async () => {
    // Arrange
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user } = openAssistant(hostedOptions());
    await sendMessage(user, '幫我看設定');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });

    // Act
    reply.stream()?.event('start', { userMessage: makeMessage(), assistantMessageId: 'a1' });
    reply.stream()?.event('queued', { position: 3 });

    // Assert
    expect(await findVisibleText('排隊中，前面還有 2 件')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '停止' })).toBeEnabled();

    // Act
    reply.stream()?.event('queued', { position: 1 });

    // Assert
    expect(await findVisibleText('排隊中，下一個就輪到你')).toBeInTheDocument();
    expect(screen.queryAllByText('排隊中，前面還有 2 件')).toHaveLength(0);

    // Act
    reply.stream()?.event('started', {});
    reply.stream()?.event('text', { delta: '先讀目前的設定。' });

    // Assert
    expect(await findVisibleText('執行中…')).toBeInTheDocument();
    expect(await screen.findByText('先讀目前的設定。')).toBeInTheDocument();
    expect(screen.queryAllByText(/排隊中/)).toHaveLength(0);

    // Act
    reply.stream()?.event('done', { message: makeAssistantMessage({ id: 'a1', parts: [{ type: 'text', text: '先讀目前的設定。' }] }) });
    reply.stream()?.close();

    // Assert
    await waitFor(() => {
      expect(screen.queryByText('執行中…')).not.toBeInTheDocument();
    });
  });

  it('queued 比 start 先到也照樣顯示', async () => {
    // Arrange
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user } = openAssistant(hostedOptions());
    await sendMessage(user, '早到');
    await waitFor(() => {
      expect(reply.stream()).not.toBeNull();
    });

    // Act
    reply.stream()?.event('queued', { position: 2 });
    reply.stream()?.event('start', { userMessage: makeMessage(), assistantMessageId: 'a1' });

    // Assert
    expect(await findVisibleText('排隊中，前面還有 1 件')).toBeInTheDocument();
  });

  it('沒有主機事件的來源照舊寫「回覆中」', async () => {
    // Arrange
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage(), assistantMessageId: 'a1' });
    });
    const { user } = openAssistant(makeAiOptions());
    await sendMessage(user, '一般');

    // Assert
    expect(await findVisibleText('回覆中…')).toBeInTheDocument();
  });

  it('送出被擋下（排隊滿了）：顯示伺服器的說明，輸入的字留著', async () => {
    // Arrange
    rejectReply('SMK', 'conv-new', 409, 'ai.host_queue_full', '你在這個來源已經有一則在等了。');
    const { user } = openAssistant(hostedOptions());

    // Act
    await sendMessage(user, '再問一次');

    // Assert
    expect(await screen.findByText('你在這個來源已經有一則在等了。')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '訊息' })).toHaveValue('再問一次');
  });

  it('只剩現在不能用的主機來源：輸入框旁說明原因，不能送', async () => {
    // Arrange / Act
    openAssistant(hostedOptions('主機離線中，請聯絡組織擁有者。'));

    // Assert
    expect(await screen.findByText('主機上的 Claude Code：主機離線中，請聯絡組織擁有者。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '送出' })).toBeDisabled();
  });

  it('完整的一輪：done 之後沒有殘留的進度文字', async () => {
    // Arrange
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('queued', { position: 1 });
      stream.event('started', {});
      completeReply(stream, '好了');
    });
    const { user } = openAssistant(hostedOptions());

    // Act
    await sendMessage(user, '快速');

    // Assert
    expect(await screen.findByText('好了')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByText('執行中…')).not.toBeInTheDocument();
    });
  });
});

describe('助理：送出被擋之後重讀能選的 AI', () => {
  it('主機來源不能用（422）：說明顯示出來，選單的資料重讀一次', async () => {
    // Arrange
    rejectReply('SMK', 'conv-new', 422, 'ai.host_source_unavailable', '主機離線中。');
    const { user } = openAssistant(hostedOptions());
    let reads = 0;
    server.use(
      http.get('/v1/ai/options', () => {
        reads += 1;
        return ok(hostedOptions(reads > 1 ? '主機離線中。' : null));
      }),
    );
    await screen.findByRole('textbox', { name: '訊息' });
    await waitFor(() => {
      expect(reads).toBeGreaterThan(0);
    });
    const before = reads;

    // Act
    await sendMessage(user, '問一下');

    // Assert
    expect(await screen.findAllByText(/主機離線中。/)).not.toHaveLength(0);
    await waitFor(() => {
      expect(reads).toBeGreaterThan(before);
    });
  });

  it('好幾個主機來源都不能用：每一個都說明原因', async () => {
    // Arrange
    const options = makeAiOptions({
      default: null,
      connections: ['A', 'B'].map((name) => ({
        id: `src-${name}`,
        name: `來源 ${name}`,
        kind: 'claude_code' as const,
        personal: false,
        hosted: true,
        notice: `${name} 主機離線中。`,
        models: [{ id: 'opus', displayName: 'Opus', efforts: [], defaultEffort: null }],
      })),
    });

    // Act
    openAssistant(options);

    // Assert
    expect(await screen.findByText('來源 A：A 主機離線中。')).toBeInTheDocument();
    expect(screen.getByText('來源 B：B 主機離線中。')).toBeInTheDocument();
  });
});
