import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { makeAssistantMessage, makeConversationSummary, makeMessage, serveConversation, serveCreateConversation, serveReply } from '../../test/assistant';
import { openAssistant, typeMessage } from './assistantTestKit';

function openConversation(messages = [makeMessage(), makeAssistantMessage()]) {
  serveConversation('SMK', { conversation: makeConversationSummary({ id: 'conv-1', title: '舊的', messageCount: messages.length }), messages });
  return openAssistant('/projects/SMK/assistant/conv-1');
}

describe('助理：複製訊息', () => {
  it('助理的複製鈕在最下面那行小字旁；按了複製文字段落（多段用空行接起來），換成打勾並報讀「已複製」，2 秒後換回來', async () => {
    // Arrange
    const reply = makeAssistantMessage({
      parts: [
        { type: 'text', text: '第一段' },
        { type: 'tool', callId: 'c1', name: 'get_blueprint', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null },
        { type: 'text', text: '第二段' },
      ],
    });
    const { user } = openConversation([makeMessage(), reply]);
    const footer = (await screen.findByText('Claude Opus 5.5 · 高')).closest('div');
    if (footer === null) throw new Error('找不到回覆下面的小字');
    const writeText = vi.spyOn(navigator.clipboard, 'writeText');

    // Act
    await user.click(within(footer).getByRole('button', { name: '複製訊息' }));

    // Assert
    expect(writeText).toHaveBeenCalledWith('第一段\n\n第二段');
    expect(await within(footer).findByText('已複製')).toBeInTheDocument();
    expect(within(footer).getByText('已複製')).toHaveAttribute('role', 'status');
    expect(footer.querySelector('[data-icon="check"]')).not.toBeNull();
    await waitFor(() => {
      expect(within(footer).queryByText('已複製')).not.toBeInTheDocument();
    });
    expect(footer.querySelector('[data-icon="copy"]')).not.toBeNull();
  });

  it('使用者的訊息也能複製他打的字', async () => {
    // Arrange
    const { user } = openConversation([makeMessage({ parts: [{ type: 'text', text: '請拆驗收' }] }), makeAssistantMessage()]);
    const bubble = (await screen.findByText('請拆驗收')).closest('div');
    const block = bubble?.parentElement;
    if (block === null || block === undefined) throw new Error('找不到訊息');
    const writeText = vi.spyOn(navigator.clipboard, 'writeText');

    // Act
    await user.click(within(block).getByRole('button', { name: '複製訊息' }));

    // Assert
    expect(writeText).toHaveBeenCalledWith('請拆驗收');
  });

  it('剪貼簿不能用：顯示「無法複製」', async () => {
    // Arrange
    const { user } = openConversation();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    const buttons = await screen.findAllByRole('button', { name: '複製訊息' });

    // Act
    await user.click(buttons[1] as HTMLElement);

    // Assert
    expect(await screen.findByText('無法複製')).toBeInTheDocument();
  });

  it('回覆中的那一則沒有複製鈕', async () => {
    // Arrange
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage(), assistantMessageId: 'a1' });
      stream.event('text', { delta: '寫到一半' });
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, '你好');
    await user.keyboard('{Enter}');

    // Assert：只有使用者那一則有
    expect(await screen.findByText('寫到一半')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: '複製訊息' })).toHaveLength(1);
  });

  it('回覆中的停止鈕帶方形的停止圖示', async () => {
    // Arrange
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
    serveReply('SMK', 'conv-new', (stream) => {
      stream.event('start', { userMessage: makeMessage(), assistantMessageId: 'a1' });
    });
    const { user } = openAssistant();

    // Act
    await typeMessage(user, '你好');
    await user.keyboard('{Enter}');

    // Assert
    const stop = await screen.findByRole('button', { name: '停止' });
    expect(stop.querySelector('[data-icon="stop"]')).not.toBeNull();
  });
});
