import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { exampleOwner } from '../../test/fixtures';
import { signedInAs } from '../../test/api';
import { ok } from '../../test/api';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { adminPerson, guestPerson, memberPerson, ownerPerson } from '../../test/people';
import { completeReply, makeAiOptions, makeAssistantMessage, makeConversationSummary, makeMessage, rejectReply, serveAiOptions, serveConversation, serveConversations, serveCreateConversation, serveReply } from '../../test/assistant';
import { openAssistant, typeMessage } from './assistantTestKit';

const MEMBER_ID = '0190a000-0000-7000-8000-000000000002';
const OWNER_ID = '0190a000-0000-7000-8000-000000000001';

function serveSend(scope: string | null = 'SMK') {
  serveCreateConversation(scope, makeConversationSummary({ id: 'conv-new' }));
  return serveReply(scope, 'conv-new', (stream) => {
    completeReply(stream, '好');
  });
}

type Rendered = ReturnType<typeof openAssistant>;

async function openList(user: Rendered['user'], text = '@範例') {
  const input = await typeMessage(user, text);
  return { input, list: await screen.findByRole('listbox', { name: '提及成員' }) };
}

describe('助理輸入框：@ 提及', () => {
  it('打 @ 出專案成員的清單，繼續打字篩選', async () => {
    // Arrange
    const { user } = openAssistant();
    const { list } = await openList(user, '@');

    // Assert
    expect(within(list).getAllByRole('option')).toHaveLength(2);

    // Act
    await user.keyboard('成員');

    // Assert
    await waitFor(() => {
      expect(within(screen.getByRole('listbox', { name: '提及成員' })).getAllByRole('option')).toHaveLength(1);
    });
  });

  it('↑↓ 選、Enter 選人而不是送出：輸入框只寫 @名稱', async () => {
    // Arrange
    const reply = serveSend();
    const { user } = openAssistant();
    const { input } = await openList(user);

    // Act
    await user.keyboard('{ArrowDown}{Enter}');

    // Assert
    expect(input).toHaveValue('@範例成員 ');
    expect(reply.sent).toHaveLength(0);
    await waitFor(() => {
      expect(screen.queryByRole('listbox', { name: '提及成員' })).not.toBeInTheDocument();
    });
  });

  it('Esc 關清單、輸入框的字還在', async () => {
    // Arrange
    const { user } = openAssistant();
    const { input } = await openList(user);

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('listbox', { name: '提及成員' })).not.toBeInTheDocument();
    });
    expect(input).toHaveValue('@範例');
  });

  it('送出的文字把還在的 @名稱 換成提及語法', async () => {
    // Arrange
    const reply = serveSend();
    const { user } = openAssistant();
    await openList(user, '請 @範例成');
    await user.keyboard('{Enter}');

    // Act
    await user.keyboard('看一下{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body.text).toBe(`請 @[範例成員](mention:${MEMBER_ID}) 看一下`);
  });

  it('選了又自己刪掉的不換', async () => {
    // Arrange
    const reply = serveSend();
    const { user } = openAssistant();
    const { input } = await openList(user, '@範例成');
    await user.keyboard('{Enter}');
    await user.clear(input);

    // Act
    await user.type(input, '範例成員 你好{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body.text).toBe('範例成員 你好');
  });

  it('送出失敗、字還在時：輸入框仍是 @名稱，再送一次照樣換得出來', async () => {
    // Arrange
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
    rejectReply('SMK', 'conv-new', 409, 'ai.conversation_busy', '失敗了');
    const { user } = openAssistant();
    const { input } = await openList(user, '@範例成');
    await user.keyboard('{Enter}你好{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('失敗了');

    // Assert
    expect(input).toHaveValue('@範例成員 你好');

    // Act
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好');
    });
    await user.click(screen.getByRole('button', { name: '送出' }));

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body.text).toBe(`@[範例成員](mention:${MEMBER_ID}) 你好`);
  });

  it('組織對話用組織的人（沒停用的）', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiOptions(makeAiOptions());
    serveConversations(null, []);
    server.use(http.get('/v1/people', () => ok([ownerPerson, adminPerson, memberPerson, { ...guestPerson, displayName: '已停用的人', disabled: true }])));
    const { user } = renderApp('/assistant');

    // Act
    const { list } = await openList(user, '@');

    // Assert
    expect(within(list).getAllByRole('option').map((option) => option.textContent)).toEqual(
      expect.arrayContaining([expect.stringContaining('範例管理員'), expect.stringContaining('範例成員')]),
    );
    expect(within(list).queryByText('已停用的人')).not.toBeInTheDocument();
  });

  it('訪客讀不到人員清單：不問、不出清單', async () => {
    // Arrange
    const guest = { ...exampleOwner, role: 'guest' as const };
    signedInAs(guest);
    serveAiOptions(makeAiOptions());
    serveConversations(null, []);
    const requested = vi.fn();
    server.use(
      http.get('/v1/people', () => {
        requested();
        return ok([]);
      }),
    );
    const { user } = renderApp('/assistant');

    // Act
    const input = await typeMessage(user, '@');

    // Assert
    expect(input).toHaveValue('@');
    expect(screen.queryByRole('listbox', { name: '提及成員' })).not.toBeInTheDocument();
    expect(requested).not.toHaveBeenCalled();
  });
});

describe('助理輸入框：清單接續', () => {
  async function typeLines(user: Rendered['user'], first: string) {
    return typeMessage(user, first);
  }

  it('編號換行接下一個編號（Enter 送出模式用 Shift+Enter）', async () => {
    // Arrange
    const reply = serveSend();
    const { user } = openAssistant();
    const input = await typeLines(user, '1. 第一件');

    // Act
    await user.keyboard('{Shift>}{Enter}{/Shift}第二件');

    // Assert
    expect(input).toHaveValue('1. 第一件\n2. 第二件');
    expect(reply.sent).toHaveLength(0);
  });

  it('- 換行接 -，空項目再換行結束清單', async () => {
    // Arrange
    const { user } = openAssistant();
    const input = await typeLines(user, '- 買牛奶');

    // Act
    await user.keyboard('{Shift>}{Enter}{/Shift}');

    // Assert
    expect(input).toHaveValue('- 買牛奶\n- ');

    // Act
    await user.keyboard('{Shift>}{Enter}{/Shift}');

    // Assert
    expect(input).toHaveValue('- 買牛奶\n');
  });

  it('⌘／Ctrl+Enter 送出模式：Enter 換行並接續，Ctrl+Enter 送出', async () => {
    // Arrange
    localStorage.setItem('assistant.sendKey', 'mod-enter');
    fireEvent(window, new StorageEvent('storage', { key: 'assistant.sendKey' }));
    const reply = serveSend();
    const { user } = openAssistant();
    const input = await typeLines(user, '• 甲');

    // Act
    await user.keyboard('{Enter}乙');

    // Assert
    expect(input).toHaveValue('• 甲\n• 乙');
    expect(reply.sent).toHaveLength(0);

    // Act
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    localStorage.removeItem('assistant.sendKey');
  });

  it('輸入法組字中的 Enter 不處理', async () => {
    // Arrange
    const { user } = openAssistant();
    const input = await typeLines(user, '- 買牛奶');

    // Act
    fireEvent.keyDown(input, { key: 'Enter', shiftKey: true, isComposing: true });

    // Assert
    expect(input).toHaveValue('- 買牛奶');
  });
});

describe('助理：使用者訊息裡的提及', () => {
  it('畫成 @名稱，複製也是 @名稱', async () => {
    // Arrange
    const text = `請 @[範例成員](mention:${OWNER_ID}) 看一下`;
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', messageCount: 2 }),
      messages: [makeMessage({ parts: [{ type: 'text', text }] }), makeAssistantMessage()],
    });
    const { user } = openAssistant('/projects/SMK/assistant/conv-1');
    const writeText = vi.spyOn(navigator.clipboard, 'writeText');
    const bubble = (await screen.findByText('請 @範例成員 看一下')).closest('div');
    const block = bubble?.parentElement;
    if (block === null || block === undefined) throw new Error('找不到訊息');

    // Act
    await user.click(within(block).getByRole('button', { name: '複製訊息' }));

    // Assert
    expect(screen.queryByText(/mention:/)).not.toBeInTheDocument();
    expect(writeText).toHaveBeenCalledWith('請 @範例成員 看一下');
  });
});
