import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { knowledgeSpaceOf, serveKnowledge } from '../../../test/knowledge';
import { currentPath, renderApp } from '../../../test/renderApp';
import { BELOW_WIDE_QUERY } from '../../../lib/useMediaQuery';

afterEach(() => {
  vi.unstubAllGlobals();
});

/** 窄螢幕（不到 64rem）：只有「不到 64rem」的查詢成立。 */
function stubBelowWide(isBelow: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: isBelow && query === BELOW_WIDE_QUERY,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function open(path = '/knowledge/DOCS', options: Parameters<typeof serveKnowledge>[0] = {}) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge(options);
  return { backend, ...renderApp(path) };
}

describe('寬螢幕：兩欄', () => {
  it('左邊是頁面樹，沒有「頁面」按鈕；頁列右邊是垃圾桶與空間設定', async () => {
    // Arrange
    stubBelowWide(false);

    // Act
    open();

    // Assert
    expect(await screen.findByRole('tree', { name: '頁面樹' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '頁面' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: '垃圾桶' })).toHaveAttribute('href', '/knowledge/DOCS/trash');
    expect(screen.getByRole('link', { name: '空間設定' })).toHaveAttribute('href', '/knowledge/DOCS/settings');
  });

  it('沒有 canManage：沒有「空間設定」', async () => {
    // Arrange
    stubBelowWide(false);
    const space = knowledgeSpaceOf({ myPermissions: { canEdit: true, canComment: true, canManage: false } });

    // Act
    open('/knowledge/DOCS', { spaces: [space] });

    // Assert
    expect(await screen.findByRole('link', { name: '垃圾桶' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '空間設定' })).not.toBeInTheDocument();
  });
});

describe('窄螢幕：頁面樹收進「頁面」按鈕', () => {
  it('平常看不到樹；按「頁面」打開，選了頁面就關掉並打開那一頁', async () => {
    // Arrange
    stubBelowWide(true);
    const { user, router } = open();
    const button = await screen.findByRole('button', { name: '頁面' });
    expect(screen.queryByRole('tree')).not.toBeInTheDocument();
    expect(button).toHaveAttribute('aria-expanded', 'false');

    // Act
    await user.click(button);
    await user.click(within(await screen.findByRole('tree', { name: '頁面樹' })).getByRole('link', { name: '會議記錄' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/4');
    });
    expect(screen.queryByRole('tree')).not.toBeInTheDocument();
  });

  it('Esc 關上，焦點回到「頁面」按鈕', async () => {
    // Arrange
    stubBelowWide(true);
    const { user } = open();
    const button = await screen.findByRole('button', { name: '頁面' });
    await user.click(button);
    await screen.findByRole('tree', { name: '頁面樹' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('tree')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('點在外面也關上', async () => {
    // Arrange
    stubBelowWide(true);
    const { user } = open();
    await user.click(await screen.findByRole('button', { name: '頁面' }));
    await screen.findByRole('tree', { name: '頁面樹' });

    // Act
    await user.click(screen.getByRole('heading', { level: 1, name: '團隊文件' }));

    // Assert
    expect(screen.queryByRole('tree')).not.toBeInTheDocument();
  });

  it('空間首頁的「新增頁面」：打開面板，輸入框在樹上', async () => {
    // Arrange
    stubBelowWide(true);
    const { user } = open();

    // Act
    await user.click(await screen.findByRole('button', { name: '新增頁面' }));

    // Assert
    expect(await screen.findByRole('textbox', { name: '新頁面的標題' })).toBeInTheDocument();
    expect(screen.getByRole('tree', { name: '頁面樹' })).toBeInTheDocument();
  });
});

describe('空間首頁的新增', () => {
  it('沒有頁面時按「新增第一頁」：樹上出現輸入框，建立後進到那一頁的編輯', async () => {
    // Arrange
    stubBelowWide(false);
    const { user, router, backend } = open('/knowledge/DOCS', { trees: { DOCS: [] } });
    await user.click(await screen.findByRole('button', { name: '新增第一頁' }));

    // Act
    await user.type(await screen.findByRole('textbox', { name: '新頁面的標題' }), '第一頁{Enter}');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/knowledge/DOCS/1');
    });
    expect(backend.requests[0]?.body).toEqual({ parentNumber: null, title: '第一頁', beforeNumber: null });
  });
});
