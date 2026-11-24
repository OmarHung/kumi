import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PRODUCT_NAME } from '../../config/product';
import { renderInRouter } from '../../test/renderInRouter';
import { StatusPage } from './StatusPage';

describe('StatusPage', () => {
  it('照樣有頁列：設分頁的標題，頁列上沒有 h1，內容照常顯示', async () => {
    // Arrange & Act
    renderInRouter(
      <StatusPage title="設定">
        <p role="status">正在載入設定…</p>
      </StatusPage>,
    );

    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('正在載入設定…');
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: '位置' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(document.title).toBe(`設定 · ${PRODUCT_NAME}`);
    });
  });

  it('h1 交給內容：錯誤頁的大標題是頁面唯一的 h1', () => {
    // Arrange & Act
    renderInRouter(
      <StatusPage title="找不到這個頁面">
        <h1>找不到這個頁面</h1>
      </StatusPage>,
    );

    // Assert
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.getByRole('heading', { level: 1, name: '找不到這個頁面' })).toBeInTheDocument();
  });
});
