import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { openAssistant } from './assistantTestKit';
import { currentSendKey } from './sendKey';

const MOD = /mac|iphone|ipad/i.test(navigator.platform) ? '⌘' : 'Ctrl';

function stubNarrowScreen(): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(max-width: 48rem)',
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
});

describe('助理輸入框：窄螢幕（48rem 以下）', () => {
  it('模型、思考深度與送出的按法收成一顆設定鈕，三個下拉都不出現', async () => {
    // Arrange
    stubNarrowScreen();
    openAssistant();

    // Act
    const settings = await screen.findByRole('button', { name: /^模型與思考深度：/ });

    // Assert
    expect(settings).toHaveAccessibleName('模型與思考深度：Claude Opus 5.5，高');
    expect(settings).toHaveTextContent('Claude Opus 5.5');
    expect(screen.queryByRole('combobox', { name: '模型' })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: '思考深度' })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: '送出的按法' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '附加檔案' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '送出' })).toBeInTheDocument();
  });

  it('打開設定鈕選得到模型、思考深度與送出的按法，名稱跟著更新', async () => {
    // Arrange
    stubNarrowScreen();
    const { user } = openAssistant();
    await user.click(await screen.findByRole('button', { name: /^模型與思考深度：/ }));

    // Act
    await user.click(await screen.findByRole('menuitemradio', { name: '低' }));
    await user.click(await screen.findByRole('button', { name: /^模型與思考深度：/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: `${MOD}+Enter 送出，Enter 換行` }));

    // Assert
    await waitFor(() => {
      expect(currentSendKey()).toBe('mod-enter');
    });
    const settings = await screen.findByRole('button', { name: /^模型與思考深度：/ });
    expect(settings).toHaveAccessibleName('模型與思考深度：Claude Opus 5.5，低');

    // Act
    await user.click(settings);
    await user.click(await screen.findByRole('menuitemradio', { name: 'Claude Haiku 5' }));

    // Assert
    expect(await screen.findByRole('button', { name: '模型與思考深度：Claude Haiku 5，預設' })).toBeInTheDocument();
  });

  it('寬螢幕照舊：三個下拉各自在，沒有設定鈕', async () => {
    // Arrange
    openAssistant();

    // Act
    await screen.findByRole('combobox', { name: '模型' });

    // Assert
    expect(screen.getByRole('combobox', { name: '思考深度' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '送出的按法' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^模型與思考深度：/ })).not.toBeInTheDocument();
  });
});
