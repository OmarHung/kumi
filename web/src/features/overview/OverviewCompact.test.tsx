import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { appOverview, serveOverview, webOverview } from '../../test/overview';
import { renderApp } from '../../test/renderApp';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('窄螢幕的專案總覽', () => {
  it('一個專案一張卡片：沒有表格，數字一格一格有名稱，連結照舊帶篩選', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner);
    serveOverview([webOverview, appOverview]);

    // Act
    renderApp('/overview');

    // Assert
    const list = await screen.findByRole('list', { name: '專案總覽' });
    const cards = within(list).getAllByRole('listitem').filter((item) => item.querySelector('h2') !== null);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(cards).toHaveLength(2);
    const web = cards.find((card) => within(card).queryByRole('heading', { name: /^WEB/ }) !== null);
    expect(web).toBeDefined();
    expect(within(web as HTMLElement).getByText('已過期')).toBeInTheDocument();
    expect(within(web as HTMLElement).getByRole('link', { name: 'WEB 停滯 1 個' })).toHaveAttribute('href', '/projects/WEB?stale=1&open=1');
    expect(within(web as HTMLElement).getByText('完成 3／8')).toBeInTheDocument();
  });

  it('範圍用下拉選（和我的工作一樣），選了換成全部看得到的', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner);
    const requests = serveOverview([webOverview], [webOverview, appOverview]);
    const { user } = renderApp('/overview');
    await screen.findByRole('list', { name: '專案總覽' });

    // Act
    await user.click(screen.getByRole('combobox', { name: '範圍' }));
    await user.click(await screen.findByRole('option', { name: '全部看得到的' }));

    // Assert
    expect(await screen.findByRole('heading', { name: /^APP/ })).toBeInTheDocument();
    expect(requests.at(-1)?.searchParams.get('scope')).toBe('visible');
  });
});
