import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { http } from 'msw';
import { failure, signedInAs } from '../../test/api';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';
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
import { aFile, installFakeUploads } from '../../test/attachments';
import { uploadedAs } from '../assistant/assistantTestKit';
import { PANEL_PROJECT_STORAGE_KEY } from './panelMemory';

const NEW = makeConversationSummary({ id: 'conv-org' });

// 附件的測試換掉了 XMLHttpRequest（上傳用它回報進度）。
afterEach(() => {
  vi.unstubAllGlobals();
});

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
  it('專案選單第一項是「不選專案」；選了之後沒有項目選擇、照樣能附加檔案，提示字換成組織版', async () => {
    // Arrange
    const { user } = openApp();
    const panel = await openPanel(user);

    // Act
    await chooseNoProject(user, panel);

    // Assert
    const input = await within(panel).findByRole('textbox', { name: '訊息' });
    expect(input).toHaveAttribute('placeholder', '問模板、規劃，或請助理幫你開新專案…');
    expect(within(panel).getByRole('button', { name: '附加檔案' })).toBeInTheDocument();
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

    const tool = { type: 'tool', callId: 'c1', name: 'propose_project', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: 'pp-1', newItemProposalId: null, memberProposalId: null } as const;
    const reply = serveReply(null, 'conv-org', (stream) => {
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
    // 組織對話沒有項目；這一則沒附檔案：送出的內容不帶 itemKey、attachmentIds。
    expect(reply.sent[0]?.body.itemKey ?? null).toBeNull();
    expect(reply.sent[0]?.body.attachmentIds ?? []).toEqual([]);
  });

  it('建好之後「在這個專案開 AI 助理」：面板切到那個專案、輸入框換成專案版', async () => {
    // Arrange
    const summary = makeConversationSummary({ id: 'conv-org', title: '開專案', messageCount: 2 });
    const tool = { type: 'tool', callId: 'c1', name: 'propose_project', status: 'done', changeSetNumber: null, error: null, itemProposalId: null, projectProposalId: 'pp-1', newItemProposalId: null, memberProposalId: null } as const;
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
    expect(await within(panel).findByRole('textbox', { name: '訊息' })).toHaveAttribute('placeholder', '描述你想做的事：建任務、指派、調整設定…');
  });
  it('附件上傳到組織對話自己的端點；送出時帶上編號，訊息裡的附件連到組織的下載網址', async () => {
    // Arrange
    const uploads = installFakeUploads();
    serveCreateConversation(null, NEW);
    const attachment = uploadedAs('需求.md');
    const reply = serveReply(null, 'conv-org', (stream) => {
      stream.event('start', { userMessage: makeMessage({ parts: [{ type: 'text', text: '照這份開專案' }], attachments: [attachment] }), assistantMessageId: 'msg-a1' });
      stream.event('done', { message: makeAssistantMessage({ parts: [{ type: 'text', text: '看過了。' }] }) });
      stream.close();
    });
    const { user } = openApp();
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);
    const input = await within(panel).findByRole('textbox', { name: '訊息' });

    // Act
    await user.upload(within(panel).getByLabelText('選擇附件檔案'), [aFile('需求.md', 100, 'text/markdown')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    uploads[0]?.respond(201, attachment);
    await user.type(input, '照這份開專案');
    const send = within(panel).getByRole('button', { name: '送出' });
    await waitFor(() => {
      expect(send).toBeEnabled();
    });
    await user.click(send);

    // Assert
    expect(uploads[0]?.url).toBe('/v1/assistant/attachments');
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body.attachmentIds).toEqual([attachment.id]);
    const sent = await within(panel).findByRole('list', { name: '附件' });
    expect(within(sent).getByRole('link')).toHaveAttribute('href', `/v1/assistant/attachments/${encodeURIComponent(attachment.id)}/content`);
  });

  it('組織對話移除還沒送出的附件：DELETE 走組織的端點', async () => {
    // Arrange
    const uploads = installFakeUploads();
    const deleted: string[] = [];
    server.use(
      http.delete('/v1/assistant/attachments/:id', ({ params }) => {
        deleted.push(String(params.id));
        return new Response(null, { status: 204 });
      }),
    );
    const { user } = openApp();
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);
    await within(panel).findByRole('textbox', { name: '訊息' });
    await user.upload(within(panel).getByLabelText('選擇附件檔案'), [aFile('截圖.png', 100, 'image/png')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    uploads[0]?.respond(201, uploadedAs('截圖.png'));
    const thumbnail = await within(panel).findByRole('presentation', { hidden: true });

    // Act
    await user.click(within(panel).getByRole('button', { name: '移除附件 截圖.png' }));

    // Assert
    expect(thumbnail).toHaveAttribute('src', `/v1/assistant/attachments/${encodeURIComponent('att-截圖.png')}/content`);
    await waitFor(() => {
      expect(deleted).toEqual(['att-截圖.png']);
    });
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
    expect(screen.getByRole('button', { name: '附加檔案' })).toBeInTheDocument();
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

  it('回覆中專案選單停用，不能在回覆中換專案', async () => {
    // Arrange
    serveCreateConversation(null, NEW);
    const reply = serveReply(null, 'conv-org', (stream) => {
      stream.event('start', { userMessage: makeMessage(), assistantMessageId: 'msg-a1' });
      stream.event('text', { delta: '想一下…' });
    });
    const { user } = openApp();
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);

    // Act
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '你好{Enter}');

    // Assert
    expect(await within(panel).findByText('想一下…')).toBeInTheDocument();
    expect(within(panel).getByRole('combobox', { name: '專案' })).toBeDisabled();
    reply.stream()?.close();
  });

  it('「不選專案」回覆中切到專案頁：頁首的專案名稱不是空的', async () => {
    // Arrange
    serveCreateConversation(null, NEW);
    const reply = serveReply(null, 'conv-org', (stream) => {
      stream.event('start', { userMessage: makeMessage(), assistantMessageId: 'msg-a1' });
      stream.event('text', { delta: '想一下…' });
    });
    const { user, router } = openApp();
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '你好{Enter}');
    await within(panel).findByText('想一下…');

    // Act
    await act(async () => {
      await router.navigate('/projects/AGY');
    });

    // Assert
    expect(await within(panel).findByRole('combobox', { name: '專案' })).toHaveTextContent('不選專案');
    reply.stream()?.close();
  });

  it('組織對話開不起來（403）：在輸入框上方顯示伺服器的說明，字留著', async () => {
    // Arrange
    server.use(http.post('/v1/assistant/conversations', () => failure(403, 'ai.assistant_forbidden', '你不能使用 AI 助理。')));
    const { user } = openApp();
    const panel = await openPanel(user);
    await chooseNoProject(user, panel);

    // Act
    await user.type(await within(panel).findByRole('textbox', { name: '訊息' }), '你好{Enter}');

    // Assert
    expect(await within(panel).findByText('你不能使用 AI 助理。')).toBeInTheDocument();
    expect(within(panel).getByRole('textbox', { name: '訊息' })).toHaveValue('你好');
  });
});
