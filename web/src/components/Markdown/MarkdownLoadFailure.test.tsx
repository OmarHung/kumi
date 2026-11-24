import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Markdown } from './Markdown';

// 重新部署之後，舊分頁要的渲染器檔案不在了：動態載入會失敗。
vi.mock('./MarkdownContent', () => {
  throw new Error('Failed to fetch dynamically imported module');
});

describe('Markdown 延後載入失敗', () => {
  it('繼續顯示純文字，不讓整頁掉進錯誤頁', async () => {
    // Arrange & Act
    const { container } = render(
      <div>
        <Markdown text={'第一行\n**粗體**'} mentionNames={new Map()} />
        <p>頁面其他的內容</p>
      </div>,
    );

    // Assert
    expect(await screen.findByText('頁面其他的內容')).toBeInTheDocument();
    await vi.waitFor(() => {
      expect(container.querySelector('[data-state]')).toHaveAttribute('data-state', 'plain');
    });
    expect(container.querySelector('[data-state="plain"]')).toHaveTextContent('第一行 **粗體**');
  });
});
