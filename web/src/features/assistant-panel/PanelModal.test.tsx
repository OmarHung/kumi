import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { makeAiOptions, serveAiOptions, serveConversations } from '../../test/assistant';

function stubCompact(isCompact: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: isCompact && query.includes('max-width'),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openCompactPanel() {
  stubCompact(true);
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  const { user } = renderApp('/projects/SMK');
  await user.click(await screen.findByRole('button', { name: 'AI 助理' }));
  return { user, panel: await screen.findByRole('dialog', { name: 'AI 助理' }) };
}

describe('窄螢幕的全螢幕面板當作 modal', () => {
  it('aria-modal 為 true、背後的內容 inert、背景不能捲；關上就解除', async () => {
    // Arrange / Act
    const { user, panel } = await openCompactPanel();

    // Assert
    expect(panel).toHaveAttribute('aria-modal', 'true');
    expect(screen.getByRole('main', { hidden: true }).closest('[inert]')).not.toBeNull();
    expect(document.documentElement).toHaveAttribute('data-assistant-modal');
    expect(panel).not.toHaveAttribute('inert');

    // Act
    await user.click(within(panel).getByRole('button', { name: '關閉' }));

    // Assert
    expect(document.documentElement).not.toHaveAttribute('data-assistant-modal');
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'AI 助理' })).toHaveFocus();
  });

  it('面板裡的連結換頁時面板收起來', async () => {
    // Arrange
    const { user, panel } = await openCompactPanel();

    // Act
    await user.click(await within(panel).findByRole('link', { name: '開成整頁' }));

    // Assert
    expect(document.documentElement).not.toHaveAttribute('data-assistant-modal');
  });
});
