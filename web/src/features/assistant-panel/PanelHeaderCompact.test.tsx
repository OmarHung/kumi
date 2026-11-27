import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { makeAiOptions, serveAiOptions, serveConversations } from '../../test/assistant';

function stubNarrowScreen(): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(max-width: 48rem)',
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function openApp() {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  serveConversations('AGY', []);
  return renderApp('/projects/SMK');
}

async function openPanel(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: 'AI 助理' }));
  return screen.findByRole('dialog', { name: 'AI 助理' });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('AI 助理面板的頁首：窄螢幕（48rem 以下）', () => {
  it('一行：對話、專案（只有代號）、新對話、開成整頁、關閉', async () => {
    // Arrange
    stubNarrowScreen();
    const { user } = openApp();

    // Act
    const dialog = await openPanel(user);

    // Assert
    expect(await within(dialog).findByRole('button', { name: '對話：新的對話' })).toBeInTheDocument();
    expect(within(dialog).getByRole('combobox', { name: '專案' })).toHaveTextContent('SMK');
    expect(within(dialog).getByRole('combobox', { name: '專案' })).not.toHaveTextContent('Smoke');
    expect(within(dialog).getByRole('button', { name: '新對話' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: '關閉' })).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: '開成整頁' })).toBeInTheDocument();
  });

  it('寬螢幕照舊：標題列加專案與「對話」選單', async () => {
    // Arrange
    const { user } = openApp();

    // Act
    const dialog = await openPanel(user);

    // Assert
    expect(await within(dialog).findByRole('button', { name: '對話' })).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: '開成整頁' })).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: '新對話' })).not.toBeInTheDocument();
  });
});
