import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ItemDetail } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const requirement = detailOf(agyItem({ number: 3, title: '官網需求' }), '第一段說明');

function stubNarrowScreen(): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(max-width: 48rem)',
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

async function openItem(item: ItemDetail = requirement) {
  const detail = agyDetail();
  signedInAs(exampleOwner, { details: [detail] });
  serveItem(item);
  serveItems(detail.project.key, []);
  serveActivity(item.key, [created()]);
  renderApp(`/projects/${detail.project.key}/items/${item.key}`);
  await screen.findByRole('heading', { level: 1 });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('項目頁的頁列：窄螢幕（48rem 以下）', () => {
  it('關注與分享只有圖示（名稱照舊），位置在頁列上', async () => {
    // Arrange
    stubNarrowScreen();

    // Act
    await openItem();

    // Assert
    const follow = await screen.findByRole('button', { name: '關注' });
    const share = screen.getByRole('button', { name: '分享' });
    expect(follow).toHaveTextContent('');
    expect(share).toHaveTextContent('');
    expect(screen.getByRole('button', { name: 'AGY-3 的更多動作' })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: '位置' })).getByText('AGY-3')).toBeInTheDocument();
  });

  it('寬螢幕照舊：關注與分享是帶字的按鈕', async () => {
    // Arrange / Act
    await openItem();

    // Assert
    expect(await screen.findByRole('button', { name: '關注' })).toHaveTextContent('關注');
    expect(screen.getByRole('button', { name: '分享' })).toHaveTextContent('分享');
  });
});
