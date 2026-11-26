import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import {
  completeReply,
  makeAiOptions,
  makeAssistantMessage,
  makeConversationSummary,
  makeMessage,
  makeProjectProposal,
  serveAiOptions,
  serveConversation,
  serveConversations,
  serveCreateConversation,
  serveProjectProposal,
  serveReply,
} from '../../test/assistant';
import { PANEL_PROJECT_STORAGE_KEY } from './panelMemory';

const NEW = makeConversationSummary({ id: 'conv-org' });

function openApp(path = '/projects') {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  serveConversations(null, []);
  return renderApp(path);
}

async function openPanel(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: 'AI 助理' }));
  return screen.findByRole('dialog', { name: 'AI 助理' });
}

async function chooseNoProject(user: ReturnType<typeof renderApp>['user'], panel: HTMLElement) {
  await chooseOption(user, within(panel).getByRole('combobox', { name: '專案' }), '不選專案');
}

describe('面板：不選專案', () => {
  it('專案選單第一項是「不選專案」；選了之後沒有附加檔案、沒有項目選擇，提示字換成組織版', async () => {
    // Arrange
    const { user } = openApp();
    const panel = await openPanel(user);

    // Act
    await chooseNoProject(user, panel);

    // Assert
    const input = await within(panel).findByRole('textbox', { name: '訊息' });
    expect(input).toHaveAttribute('placeholder', '問模板、規劃，或請助理幫你開新專案…');
    expect(within(panel).queryByRole('button', { name: '附加檔案' })).not.toBeInTheDocument();
    expect(within(panel).queryByRole('combobox', { name: '項目' })).not.toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: '不指定項目' })).not.toBeInTheDocument();
    expect(within(panel).getByRole('combobox', { name: '專案' })).toHaveTextContent('不選專案');
  });

  it('選了記在瀏覽器：和「還沒選」分得開，下次不在專案頁時直接是不選專案', async () => {
    // Arrange
    const first = openApp();
    const panel = await openPanel(first.user);
    expect(localStorage.getItem(PANEL_PROJECT_STORAGE_KEY)).toBeNull();

    // Act
    await chooseNoProject(first.user, panel);
    await within(panel).findByRole('textbox', { name: '訊息' });

    // Assert
    expect(localStorage.getItem(PANEL_PROJECT_STORAGE_KEY)).toBe('@organization');
    first.unmount();
    const second = openApp();
    const again = await openPanel(second.user);
    expect(await within(again).findByRole('textbox', { name: '訊息' })).toHaveAttribute('placeholder', '問模板、規劃，或請助理幫你開新專案…');
    expect(within(again).getByRole('combobox', { name: '專案' })).toHaveTextContent('不選專案');
  });

  it('送出走組織的端點；回覆裡的新專案建議畫成卡片', async () => {
    // Arrange
    serveCreateConversation(null, NEW);
    serveProjectProposal(makeProjectProposal());
    const tool = { type: 'tool', callId: 'c1', name: 'propose_project', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: 'pp-1' } as const;
    serveReply(null, 'conv-org', (stream) => {
      stream.event('start', { userMessage: makeMessage({ parts: [{ type: 'text', text: '開一個網站改版專案' }] }), assistantMessageId: 'msg-a1' });
      stream.event('tool', { tool });
      stream.event('done', { message: makeAssistantMessage({ parts: [tool, { type: 'text', text: '我提出了建議。' }] }) });
      stream.close();
    });
    const { user } = openApp();
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);

    // Act
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '開一個網站改版專案{Enter}');

    // Assert
    expect(await within(panel).findByRole('region', { name: '新專案建議 WEB' })).toBeInTheDocument();
    expect(within(panel).getByText('提出新專案')).toBeInTheDocument();
  });

  it('建好之後「在這個專案開 AI 助理」：面板切到那個專案、輸入框換成專案版', async () => {
    // Arrange
    const summary = makeConversationSummary({ id: 'conv-org', title: '開專案', messageCount: 2 });
    const tool = { type: 'tool', callId: 'c1', name: 'propose_project', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: 'pp-1' } as const;
    serveProjectProposal(makeProjectProposal({ key: 'AGY', name: '接案', status: 'applied', decidedAt: '2026-11-26T02:05:00Z', createdProjectKey: 'AGY' }));
    const { user } = openApp();
    serveConversations(null, [summary]);
    serveConversation(null, { conversation: summary, messages: [makeMessage(), makeAssistantMessage({ parts: [tool] })] });
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);
    await user.click(within(panel).getByRole('button', { name: '對話' }));
    await user.click(await screen.findByRole('menuitemradio', { name: '開專案' }));

    // Act
    await user.click(await within(panel).findByRole('button', { name: '在這個專案開 AI 助理' }));

    // Assert
    expect(await within(panel).findByRole('combobox', { name: '專案' })).toHaveTextContent('接案測試');
    expect(await within(panel).findByRole('textbox', { name: '訊息' })).toHaveAttribute('placeholder', '描述你想怎麼改這個專案的設定…');
  });
});

describe('整頁：不選專案', () => {
  it('/assistant 是組織的對話：沒有麵包屑與項目選擇，面板收起來；開新對話換到 /assistant/:id', async () => {
    // Arrange
    serveCreateConversation(null, NEW);
    serveReply(null, 'conv-org', (stream) => {
      completeReply(stream, '好的。');
    });
    const { user, router } = openApp('/assistant');

    // Act
    await user.type(await screen.findByRole('textbox', { name: '訊息' }), '你好{Enter}');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/assistant/conv-org');
    });
    expect(await screen.findByText('好的。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '附加檔案' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'AI 助理' })).not.toBeInTheDocument();
  });

  it('面板的「開成整頁」在不選專案時連到 /assistant', async () => {
    // Arrange
    const { user, router } = openApp();
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);

    // Act
    await user.click(await within(panel).findByRole('link', { name: '開成整頁' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/assistant');
    });
    await act(async () => Promise.resolve());
  });

  it('組織沒開放可以用的 AI：說明原因，不是空白頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions(makeAiOptions({ available: false, canAddPersonal: false, connections: [], default: null }));

    // Act
    renderApp('/assistant');

    // Assert
    expect(await screen.findByText('AI 助理還不能用')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到專案清單' })).toHaveAttribute('href', '/projects');
  });
});
