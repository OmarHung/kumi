import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { makeAssistantMessage, makeConversationSummary, makeMessage, serveConversation, serveConversations, serveReply, completeReply } from '../../../test/assistant';
import { exampleOwner, smkDetail } from '../../../test/fixtures';
import { signedInAs } from '../../../test/api';
import { renderApp } from '../../../test/renderApp';
import { makeAiOptions, serveAiOptions } from '../../../test/assistant';
import type { AssistantMessage } from '../../../api/aiTypes';

const BLOCK = '```kumi-questions\n{"questions":[{"question":"誰負責？","options":[{"label":"阿哲"},{"label":"Ivan"}]}]}\n```';

function openConversation(messages: readonly AssistantMessage[]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveAiOptions(makeAiOptions());
  const summary = makeConversationSummary({ id: 'conv-1', title: 'x', messageCount: messages.length });
  serveConversations('SMK', [summary]);
  serveConversation('SMK', { conversation: summary, messages });
  return renderApp('/projects/SMK/assistant/conv-1');
}

const asking = (overrides: Partial<AssistantMessage> = {}) =>
  makeAssistantMessage({ id: 'a1', parts: [{ type: 'text', text: `先問一題。\n\n${BLOCK}` }], ...overrides });

describe('助理回覆裡的問題卡片', () => {
  it('最新一則已結束的回覆：區塊前的文字照常、區塊畫成能作答的卡片；回答走一般的送出', async () => {
    // Arrange
    const reply = serveReply('SMK', 'conv-1', (stream) => {
      completeReply(stream, '收到');
    });
    const { user } = openConversation([makeMessage({ id: 'u1' }), asking()]);

    // Act
    expect(await screen.findByText('先問一題。')).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Ivan' }));
    await user.click(screen.getByRole('button', { name: '送出回答' }));

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body.text).toBe('回答你的問題：\n1. 誰負責？\n   → Ivan');
    expect(reply.sent[0]?.body.selection.model).toBe('claude-opus-5-5');
  });

  it('不是最新一則（後面已有使用者的訊息）時唯讀', async () => {
    openConversation([makeMessage({ id: 'u1' }), asking(), makeMessage({ id: 'u2', parts: [{ type: 'text', text: '好' }] })]);

    expect(await screen.findByRole('region', { name: '助理的問題' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '送出回答' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ivan' })).not.toBeInTheDocument();
  });

  it('停止或失敗的回覆也唯讀', async () => {
    openConversation([makeMessage({ id: 'u1' }), asking({ status: 'stopped' })]);

    expect(await screen.findByRole('region', { name: '助理的問題' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ivan' })).not.toBeInTheDocument();
  });

  it('讀不出來的區塊照一般的程式碼區塊顯示，不畫卡片', async () => {
    openConversation([makeMessage({ id: 'u1' }), asking({ parts: [{ type: 'text', text: '```kumi-questions\n{壞掉\n```' }] })]);

    expect(await screen.findByText(/壞掉/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: '助理的問題' })).not.toBeInTheDocument();
  });
});
