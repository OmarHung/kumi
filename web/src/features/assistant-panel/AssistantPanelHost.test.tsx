import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { created, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { makeAiOptions, serveAiOptions, serveConversations } from '../../test/assistant';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const first = smkItem({ number: 1, title: '首頁設計稿' });

/** 項目開在浮動視窗（modal）裡：背後 inert、有遮罩。 */
async function openFloatingItem() {
  localStorage.setItem('shell.itemPanelMode', 'modal');
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [first]);
  serveItem(detailOf(first, '說明'));
  serveActivity(first.key, [created()]);
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  await view.user.click(within(table).getByRole('link', { name: first.title }));
  const modal = await screen.findByRole('dialog', { name: `任務 ${first.key}` });
  return { ...view, modal };
}

describe('項目的浮動視窗開著時的 AI 助理', () => {
  it('按鈕放進浮動視窗裡：看得到、不是 inert、點得到', async () => {
    // Arrange / Act
    const { modal } = await openFloatingItem();

    // Assert
    const button = await within(modal).findByRole('button', { name: 'AI 助理' });
    expect(button.closest('[inert]')).toBeNull();
    expect(button.closest('[data-inert-exempt]')).not.toBeNull();
  });

  it('點按鈕打開面板：面板也在視窗裡，點面板不會關掉視窗', async () => {
    // Arrange
    const { user, modal } = await openFloatingItem();

    // Act
    await user.click(await within(modal).findByRole('button', { name: 'AI 助理' }));
    const panel = await screen.findByRole('dialog', { name: 'AI 助理' });
    await user.click(panel);

    // Assert
    expect(modal).toContainElement(panel);
    expect(screen.getByRole('dialog', { name: `任務 ${first.key}` })).toBeInTheDocument();
  });

  it('面板裡按 Esc 只收起面板，項目的視窗還在', async () => {
    // Arrange
    const { user, modal } = await openFloatingItem();
    await user.click(await within(modal).findByRole('button', { name: 'AI 助理' }));
    const panel = await screen.findByRole('dialog', { name: 'AI 助理' });
    const input = await within(panel).findByRole('textbox', { name: '訊息' });
    input.focus();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(panel).toHaveAttribute('data-open', 'false');
    });
    expect(screen.getByRole('dialog', { name: `任務 ${first.key}` })).toBeInTheDocument();
  });

  it('視窗關掉：按鈕回到 body；視窗再開又進去', async () => {
    // Arrange
    const { user, modal } = await openFloatingItem();
    const button = await within(modal).findByRole('button', { name: 'AI 助理' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: `任務 ${first.key}` })).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(button.closest('[data-inert-exempt]')?.parentElement).toBe(document.body);
    });
  });

  it('確認類的對話框開在上面：按鈕回到 body 擋在後面，不浮上來', async () => {
    // Arrange
    const { modal } = await openFloatingItem();
    const button = await within(modal).findByRole('button', { name: 'AI 助理' });
    const confirm = document.createElement('div');
    confirm.setAttribute('role', 'dialog');
    confirm.setAttribute('aria-modal', 'true');

    // Act
    await act(async () => {
      document.body.appendChild(confirm);
      await Promise.resolve();
    });

    // Assert
    await waitFor(() => {
      expect(button.closest('[data-inert-exempt]')?.parentElement).toBe(document.body);
    });
    fireEvent.keyDown(confirm, { key: 'Escape' });
    confirm.remove();
  });
});
