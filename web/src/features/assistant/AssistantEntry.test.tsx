import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { makeAiOptions, serveAiOptions, serveConversations } from '../../test/assistant';

async function projectReady() {
  await screen.findByRole('link', { name: '資訊' });
}

describe('專案頁列的「AI 助理」入口', () => {
  it('組織開了 AI、而且有 blueprint.read：在「資訊」前面，連到助理頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    serveConversations('SMK', []);

    // Act
    const { user, router } = renderApp('/projects/SMK');
    const entry = await screen.findByRole('link', { name: 'AI 助理' });

    // Assert
    const info = screen.getByRole('link', { name: '資訊' });
    expect(entry.compareDocumentPosition(info) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await user.click(entry);
    expect(currentPath(router)).toBe('/projects/SMK/assistant');
  });

  it('組織沒開 AI：沒有入口', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions(makeAiOptions({ available: false }));

    // Act
    renderApp('/projects/SMK');
    await projectReady();

    // Assert
    expect(screen.queryByRole('link', { name: 'AI 助理' })).not.toBeInTheDocument();
  });

  it('沒有 blueprint.read：沒有入口', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read_own'] })] });
    serveAiOptions();

    // Act
    renderApp('/projects/SMK');
    await projectReady();

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('link', { name: 'AI 助理' })).not.toBeInTheDocument();
    });
  });

  it('窄螢幕：收進「專案的動作」選單', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: true,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions();
    serveConversations('SMK', []);
    const { user, router } = renderApp('/projects/SMK');

    // Act
    await user.click(await screen.findByRole('button', { name: '專案的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: 'AI 助理' }));
    vi.unstubAllGlobals();

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK/assistant');
  });
});
