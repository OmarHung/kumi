import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { completeReply, makeConversationSummary, serveCreateConversation, serveReply } from '../../test/assistant';
import { openAssistant, typeMessage } from './assistantTestKit';
import { currentSendKey, readSendKey, SEND_KEY_STORAGE_KEY } from './sendKey';

const MOD = /mac|iphone|ipad/i.test(navigator.platform) ? '⌘' : 'Ctrl';

function serveSend() {
  serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
  return serveReply('SMK', 'conv-new', (stream) => {
    completeReply(stream, '好');
  });
}

async function chooseMode(user: ReturnType<typeof openAssistant>['user'], name: string) {
  await user.click(await screen.findByRole('combobox', { name: '送出的按法' }));
  await user.click(await screen.findByRole('option', { name }));
}

describe('助理：送出的按法', () => {
  it('預設 Enter 送出、Shift+Enter 換行；鈕上寫「Enter 送出」，輸入框的說明是目前的按法', async () => {
    // Arrange
    const reply = serveSend();
    const { user } = openAssistant();
    const input = await typeMessage(user, '第一行');

    // Assert
    expect(screen.getByRole('combobox', { name: '送出的按法' })).toHaveTextContent('Enter 送出');
    expect(input).toHaveAccessibleDescription('Enter 送出，Shift+Enter 換行');

    // Act
    await user.keyboard('{Shift>}{Enter}{/Shift}第二行');

    // Assert
    expect(input).toHaveValue('第一行\n第二行');
    expect(reply.sent).toHaveLength(0);

    // Act
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body.text).toBe('第一行\n第二行');
  });

  it('選了組合鍵：Enter 與 Shift+Enter 換行，組合鍵 + Enter 送出；偏好記在瀏覽器', async () => {
    // Arrange
    const reply = serveSend();
    const { user } = openAssistant();
    const input = await typeMessage(user, '甲');

    // Act
    await chooseMode(user, `${MOD}+Enter 送出，Enter 換行`);
    await user.click(input);
    await user.keyboard('{Enter}乙{Shift>}{Enter}{/Shift}丙');

    // Assert
    expect(input).toHaveValue('甲\n乙\n丙');
    expect(reply.sent).toHaveLength(0);
    expect(screen.getByRole('combobox', { name: '送出的按法' })).toHaveTextContent(`${MOD}+Enter 送出`);
    expect(input).toHaveAccessibleDescription(`${MOD}+Enter 送出，Enter 換行`);
    expect(screen.getByRole('button', { name: '送出' })).toHaveAttribute('title', `${MOD}+Enter 送出，Enter 換行`);
    expect(localStorage.getItem(SEND_KEY_STORAGE_KEY)).toBe('mod-enter');

    // Act
    fireEvent.keyDown(input, { key: 'Enter', ctrlKey: true, metaKey: true });

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
  });

  it.each([
    ['預設', null],
    ['組合鍵', 'mod-enter'],
  ])('%s：輸入法組字中的 Enter 不送出', async (_name, stored) => {
    // Arrange
    if (stored !== null) localStorage.setItem(SEND_KEY_STORAGE_KEY, stored);
    const reply = serveSend();
    const { user } = openAssistant();
    const input = await typeMessage(user, '注音');

    // Act
    fireEvent.keyDown(input, { key: 'Enter', isComposing: true, ctrlKey: true, metaKey: true });
    fireEvent.keyDown(input, { key: 'Enter', keyCode: 229, ctrlKey: true, metaKey: true });

    // Assert
    expect(reply.sent).toHaveLength(0);
  });

  it('其他分頁改了偏好（storage 事件）：這個輸入框跟著變', async () => {
    // Arrange
    openAssistant();
    const button = await screen.findByRole('combobox', { name: '送出的按法' });

    // Act
    localStorage.setItem(SEND_KEY_STORAGE_KEY, 'mod-enter');
    fireEvent(window, new StorageEvent('storage', { key: SEND_KEY_STORAGE_KEY }));

    // Assert
    await waitFor(() => {
      expect(within(button).getByText(`${MOD}+Enter 送出`)).toBeInTheDocument();
    });
  });

  it('localStorage 壞掉：讀不到當 Enter，選了這一頁照樣生效', async () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { user } = openAssistant();
    expect(readSendKey()).toBe('enter');

    // Act
    await chooseMode(user, `${MOD}+Enter 送出，Enter 換行`);

    // Assert
    expect(currentSendKey()).toBe('mod-enter');
    expect(screen.getByRole('combobox', { name: '送出的按法' })).toHaveTextContent(`${MOD}+Enter 送出`);
  });

  it('不認得的值當 Enter', () => {
    // Arrange
    localStorage.setItem(SEND_KEY_STORAGE_KEY, 'shift-enter');

    // Act & Assert
    expect(readSendKey()).toBe('enter');
  });
});
