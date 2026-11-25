import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { accountNotification, notification, serveNotifications } from '../../test/notifications';
import { currentPath, renderApp } from '../../test/renderApp';

const tokenCreated = accountNotification(
  {
    event: 'access_token_created',
    tokens: [{ name: 'CI', scopes: ['items:read', 'items:write'], expiresAt: null }],
    machineName: null,
  },
  { createdAt: '2026-11-18T09:00:00Z' },
);
const assigned = notification({ kind: 'assigned', itemKey: 'SMK-7', itemTitle: '首頁設計稿' });

function setup(items = [tokenCreated, assigned]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const requests = serveNotifications(items);
  return { requests, ...renderApp('/inbox') };
}

describe('收件匣：帳號通知', () => {
  it('畫成一則連到「命令列與整合」的列：整句話、「帳號安全」，沒有做事的人的頭像', async () => {
    // Arrange & Act
    setup();

    // Assert
    const link = await screen.findByRole('link', { name: /建立了存取權杖「CI」/ });
    expect(link).toHaveAttribute('href', '/account/integrations');
    expect(link).toHaveAccessibleName(expect.stringContaining('未讀：建立了存取權杖「CI」（讀取項目、修改項目），從 203.0.113.5'));
    expect(within(link).getByText('帳號安全')).toBeInTheDocument();
    expect(within(link).queryByText(exampleOwner.displayName)).not.toBeInTheDocument();
    expect(link.querySelector('[data-icon="lock"]')).not.toBeNull();
  });

  it('項目的通知照舊連到項目', async () => {
    // Arrange & Act
    setup();

    // Assert
    expect(await screen.findByRole('link', { name: /指派給你/ })).toHaveAttribute('href', '/projects/SMK/items/SMK-7');
  });

  it('點了換到「命令列與整合」（不在旁邊開項目的面板），並且標成已讀', async () => {
    // Arrange
    const { requests, router } = setup();
    const user = userEvent.setup();

    // Act
    await user.click(await screen.findByRole('link', { name: /建立了存取權杖「CI」/ }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/account/integrations');
    });
    expect(requests.reads).toEqual([[tokenCreated.notificationId]]);
  });

  it('沒有任何項目的通知時照樣列出來（未讀 1 則）', async () => {
    // Arrange & Act
    setup([tokenCreated]);

    // Assert
    expect(await screen.findByRole('link', { name: /建立了存取權杖「CI」/ })).toBeInTheDocument();
    expect(screen.queryByText('還沒有通知')).not.toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getAllByRole('link', { name: '收件匣' })[0]).toHaveAccessibleDescription('1 則未讀');
    });
  });
});
