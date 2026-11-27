import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { notification, serveNotifications } from '../../test/notifications';
import { renderApp } from '../../test/renderApp';

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('窄螢幕的收件匣', () => {
  it('一則是頭像、誰做了什麼加時間、標題、摘要、最後是項目編號；標題與摘要各最多兩行', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveNotifications([notification({ kind: 'mentioned', itemKey: 'SMK-8', itemTitle: '匯出報表時保留篩選條件，下次打開沿用上次的設定', excerpt: '麻煩看一下' })]);

    // Act
    renderApp('/inbox');

    // Assert
    const link = await screen.findByRole('link', { name: /SMK-8/ });
    const title = within(link).getByText('匯出報表時保留篩選條件，下次打開沿用上次的設定');
    expect(title).toHaveAttribute('title', '匯出報表時保留篩選條件，下次打開沿用上次的設定');
    expect(title.className).toMatch(/title/);
    expect(within(link).getByText('麻煩看一下')).toBeInTheDocument();
    expect(within(link).getByText('SMK-8')).toBeInTheDocument();
    expect(within(link).getByText(/提及你/)).toBeInTheDocument();
    expect(link.querySelector('time')).not.toBeNull();
    const text = link.textContent;
    expect(text.indexOf('提及你')).toBeLessThan(text.indexOf('匯出報表'));
    expect(text.indexOf('麻煩看一下')).toBeLessThan(text.indexOf('SMK-8'));
  });

  it('未讀的有小圓點（裝飾）與看不見的「未讀：」，已讀的沒有', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveNotifications([
      notification({ itemKey: 'SMK-7' }),
      notification({ itemKey: 'SMK-9', readAt: '2026-11-18T08:30:00Z' }),
    ]);
    const { user } = renderApp('/inbox');
    await user.click(await screen.findByRole('button', { name: '只看未讀' }));

    // Act
    const unread = await screen.findByRole('link', { name: /SMK-7/ });
    const read = screen.getByRole('link', { name: /SMK-9/ });

    // Assert
    expect(unread).toHaveAccessibleName(/^未讀：/);
    expect(unread.querySelector('[aria-hidden="true"][class*="dot"]')).not.toBeNull();
    expect(read).not.toHaveAccessibleName(/未讀/);
    expect(read.querySelector('[class*="dot"]')).toBeNull();
  });
});
