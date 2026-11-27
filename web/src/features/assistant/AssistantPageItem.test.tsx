import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { ok, signedInAs } from '../../test/api';
import {
  completeReply,
  makeAiOptions,
  makeAssistantMessage,
  makeConversationSummary,
  makeItemBatchProposal,
  makeItemDeletionProposal,
  makeItemProposal,
  makeMemberProposal,
  makeNewItemProposal,
  makeMessage,
  serveAiOptions,
  serveConversation,
  serveConversations,
  serveCreateConversation,
  serveItemBatchProposal,
  serveItemDeletionProposal,
  serveItemProposal,
  serveMemberProposal,
  serveNewItemProposal,
  serveReply,
} from '../../test/assistant';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const LOGIN = smkItem({ number: 12, title: '修正登入頁' });

function openPage(path = '/projects/SMK/assistant') {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  serveItem(detailOf(LOGIN));
  server.use(http.get('/v1/projects/SMK/items', () => ok({ items: [LOGIN], nextCursor: null })));
  return renderApp(path);
}

describe('整頁的 AI 助理：選項目', () => {
  it('一開始不指定；在頁首選一個項目之後，提示字換掉、送出帶 itemKey', async () => {
    // Arrange
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好');
    });
    const { user } = openPage();
    await user.click(await screen.findByRole('button', { name: '項目：不指定' }));

    // Act
    await user.click(await screen.findByRole('option', { name: /修正登入頁/ }));

    // Assert
    expect(await screen.findByRole('button', { name: '項目：SMK-12' })).toBeInTheDocument();
    const input = screen.getByRole('textbox', { name: '訊息' });
    expect(input).toHaveAttribute('placeholder', '問這個項目的事，或請助理建議怎麼改…');

    // Act
    await user.type(input, '整理重點{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent[0]?.body.itemKey).toBe('SMK-12');
    });
  });

  it('對話裡的項目工具與修改建議卡片、使用者訊息上的「關於」', async () => {
    // Arrange
    const proposal = makeItemProposal({ projectKey: 'SMK', itemKey: 'SMK-12', itemTitle: '修正登入頁' });
    serveItemProposal(proposal);
    const tool = { type: 'tool', status: 'done', changeSetNumber: null, error: null } as const;
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', messageCount: 2 }),
      messages: [
        makeMessage({ id: 'u1', itemKey: 'SMK-12', parts: [{ type: 'text', text: '改成完成' }] }),
        makeAssistantMessage({
          id: 'a1',
          parts: [
            { ...tool, callId: 'c1', name: 'get_item', itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null },
            { ...tool, callId: 'c2', name: 'search_items', itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null },
            { ...tool, callId: 'c3', name: 'propose_item_changes', itemProposalId: proposal.id, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null },
          ],
        }),
      ],
    });

    // Act
    openPage('/projects/SMK/assistant/conv-1');

    // Assert
    expect(await screen.findByText('讀取項目')).toBeInTheDocument();
    expect(screen.getByText('搜尋項目')).toBeInTheDocument();
    expect(screen.getByText('建議修改')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '關於 SMK-12' })).toBeInTheDocument();
    const card = await screen.findByRole('region', { name: '修改建議 SMK-12' });
    expect(within(card).getByRole('button', { name: '套用' })).toBeInTheDocument();
  });

  it('對話裡的 propose_new_items 畫成新增項目建議的卡片', async () => {
    // Arrange
    const proposal = makeNewItemProposal();
    serveNewItemProposal(proposal);
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    const tool = { type: 'tool', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null } as const;
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', messageCount: 2 }),
      messages: [
        makeMessage({ id: 'u1', parts: [{ type: 'text', text: '拆成兩個任務' }] }),
        makeAssistantMessage({ id: 'a1', parts: [{ ...tool, callId: 'c1', name: 'propose_new_items', newItemProposalId: proposal.id }] }),
      ],
    });

    // Act
    openPage('/projects/SMK/assistant/conv-1');

    // Assert
    expect(await screen.findByText('建議新增項目')).toBeInTheDocument();
    const card = await screen.findByRole('region', { name: '新增項目建議 SMK' });
    expect(within(card).getByRole('button', { name: '全部建立' })).toBeInTheDocument();
  });

  it('對話裡的 propose_member_changes 畫成成員建議的卡片，工具列寫建議成員變動', async () => {
    // Arrange
    const proposal = makeMemberProposal();
    serveMemberProposal(proposal);
    const tool = { type: 'tool', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null } as const;
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', messageCount: 2 }),
      messages: [
        makeMessage({ id: 'u1', parts: [{ type: 'text', text: '把小美加進來' }] }),
        makeAssistantMessage({ id: 'a1', parts: [{ ...tool, callId: 'c1', name: 'propose_member_changes', memberProposalId: proposal.id }] }),
      ],
    });

    // Act
    openPage('/projects/SMK/assistant/conv-1');

    // Assert
    expect(await screen.findByText('建議成員變動')).toBeInTheDocument();
    const card = await screen.findByRole('region', { name: '成員建議 SMK' });
    expect(within(card).getByRole('button', { name: '套用' })).toBeInTheDocument();
  });

  it('對話裡的 propose_item_deletions 與 propose_bulk_item_changes 畫成各自的卡片，工具列用新的說法', async () => {
    // Arrange
    const deletion = makeItemDeletionProposal();
    const batch = makeItemBatchProposal();
    serveItemDeletionProposal(deletion);
    serveItemBatchProposal(batch);
    server.use(http.get('/v1/projects/SMK', () => ok(smkDetail())));
    const tool = { type: 'tool', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null } as const;
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', messageCount: 2 }),
      messages: [
        makeMessage({ id: 'u1', parts: [{ type: 'text', text: '刪掉重複的，其餘延後' }] }),
        makeAssistantMessage({
          id: 'a1',
          parts: [
            { ...tool, callId: 'c1', name: 'propose_item_deletions', itemDeletionProposalId: deletion.id },
            { ...tool, callId: 'c2', name: 'propose_bulk_item_changes', itemBatchProposalId: batch.id },
          ],
        }),
      ],
    });

    // Act
    openPage('/projects/SMK/assistant/conv-1');

    // Assert
    expect(await screen.findByText('建議刪除項目')).toBeInTheDocument();
    expect(await screen.findByText('建議修改多個項目')).toBeInTheDocument();
    const deletionCard = await screen.findByRole('region', { name: '刪除建議 SMK' });
    expect(within(deletionCard).getByRole('button', { name: /^刪除 2 個項目/ })).toBeInTheDocument();
    const batchCard = await screen.findByRole('region', { name: '批次修改建議 SMK' });
    expect(within(batchCard).getByRole('button', { name: '全部套用' })).toBeInTheDocument();
  });

  it('網址的 ?item= 是進來時的項目；開新對話換了網址也不會掉', async () => {
    // Arrange
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好');
    });
    const { user } = openPage('/projects/SMK/assistant?item=SMK-12');
    expect(await screen.findByRole('button', { name: '項目：SMK-12' })).toBeInTheDocument();

    // Act
    const input = await screen.findByRole('textbox', { name: '訊息' });
    await user.type(input, '整理重點{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent[0]?.body.itemKey).toBe('SMK-12');
    });
    expect(await screen.findByRole('button', { name: '項目：SMK-12' })).toBeInTheDocument();
  });
});
