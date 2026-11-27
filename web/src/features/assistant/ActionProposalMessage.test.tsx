import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AssistantMessage } from '../../api/aiTypes';
import { toolLabel } from '../ai/aiLabels';
import { ok, signedInAs } from '../../test/api';
import {
  makeActionProposal,
  makeAiOptions,
  makeAssistantMessage,
  makeConversationSummary,
  makeMessage,
  serveActionProposal,
  serveAiOptions,
  serveConversation,
  serveConversations,
} from '../../test/assistant';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { changeRows } from './proposalChanges';

const TOOLS = [
  'propose_knowledge_changes',
  'propose_people_changes',
  'propose_attachment_changes',
  'propose_saved_view_changes',
  'propose_description_template_changes',
] as const;

const tool = { type: 'tool', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: null, newItemProposalId: null, memberProposalId: null, itemDeletionProposalId: null, itemBatchProposalId: null, proposalId: null } as const;

afterEach(() => {
  vi.unstubAllGlobals();
});

function openConversation(parts: AssistantMessage['parts']) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  server.use(http.get('/v1/projects/SMK/items', () => ok({ items: [], nextCursor: null })));
  serveConversation('SMK', {
    conversation: makeConversationSummary({ id: 'conv-1', messageCount: 2 }),
    messages: [makeMessage({ id: 'u1', parts: [{ type: 'text', text: '整理一下' }] }), makeAssistantMessage({ id: 'a1', parts })],
  });
  return renderApp('/projects/SMK/assistant/conv-1');
}

describe('MessageView：五個動作建議工具', () => {
  it.each(TOOLS)('%s 做完且帶 proposalId 時畫成動作建議卡片', async (name) => {
    // Arrange
    serveActionProposal(makeActionProposal({ title: `卡片 ${name}` }));

    // Act
    openConversation([{ ...tool, callId: 'c1', name, proposalId: 'ac-1' }]);

    // Assert
    const card = await screen.findByRole('region', { name: `動作建議 卡片 ${name}` });
    expect(within(card).getByRole('button', { name: '套用' })).toBeInTheDocument();
  });

  it('沒有 proposalId 時不畫卡片', async () => {
    // Arrange / Act
    openConversation([{ ...tool, callId: 'c1', name: 'propose_people_changes', proposalId: null }]);

    // Assert
    expect(await screen.findByText('提出人員的變更')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: /^動作建議/ })).not.toBeInTheDocument();
  });
});

describe('十四個新工具的說法', () => {
  it.each([
    ['list_spaces', '看知識庫空間'],
    ['get_page_tree', '看頁面樹'],
    ['get_page', '讀知識庫頁面'],
    ['list_page_trash', '看知識庫垃圾桶'],
    ['list_saved_views', '看存的檢視'],
    ['list_description_templates', '看說明格式'],
    ['get_overview', '看專案總覽'],
    ['list_notifications', '看收件匣'],
    ['list_changesets', '看變更集'],
    ['propose_knowledge_changes', '提出知識庫的變更'],
    ['propose_people_changes', '提出人員的變更'],
    ['propose_attachment_changes', '提出附件的變更'],
    ['propose_saved_view_changes', '提出檢視的變更'],
    ['propose_description_template_changes', '提出說明格式的變更'],
  ])('%s → %s', (name, label) => {
    expect(toolLabel(name)).toBe(label);
  });
});

describe('修改建議的 following 欄位', () => {
  it('true 寫「關注」、false 寫「取消關注」，不寫之前的值', () => {
    // Arrange / Act
    const [on] = changeRows([{ field: 'following', value: true }], undefined, undefined);
    const [off] = changeRows([{ field: 'following', value: false }], undefined, undefined);

    // Assert
    expect(on).toMatchObject({ kind: 'value', label: '關注狀態', before: null, after: '開始關注' });
    expect(off).toMatchObject({ label: '關注狀態', before: null, after: '取消關注' });
  });
});

describe('窄螢幕頁列的「對話」鈕', () => {
  it('只有圖示，名稱是對話', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: true,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions(makeAiOptions());
    serveConversations('SMK', []);

    // Act
    renderApp('/projects/SMK/assistant');
    const button = await screen.findByRole('button', { name: '對話' });

    // Assert
    expect(button).toHaveAttribute('aria-label', '對話');
    expect(button.textContent).toBe('');
  });
});
