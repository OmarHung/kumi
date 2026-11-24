import { screen, waitFor, within } from '@testing-library/react';
import { createRef } from 'react';
import { describe, expect, it } from 'vitest';
import { PRODUCT_NAME } from '../../config/product';
import { renderInRouter } from '../../test/renderInRouter';
import { PageBar } from './PageBar';

describe('PageBar', () => {
  it('標題是 h1，也是瀏覽器分頁的標題', async () => {
    // Arrange
    renderInRouter(<PageBar title="我的工作" />);

    // Act
    const heading = screen.getByRole('heading', { level: 1, name: '我的工作' });

    // Assert
    expect(heading).not.toHaveAttribute('tabindex');
    await waitFor(() => {
      expect(document.title).toBe(`我的工作 · ${PRODUCT_NAME}`);
    });
  });

  it('位置是 nav「位置」裡的連結，後面接著標題', () => {
    // Arrange
    renderInRouter(
      <PageBar
        title="設定"
        crumbs={[
          { label: '專案', to: '/projects' },
          { label: 'AGY', to: '/projects/AGY', mono: true },
        ]}
        actions={<button type="button">所有變更集</button>}
      />,
    );

    // Act
    const location = screen.getByRole('navigation', { name: '位置' });

    // Assert
    expect(within(location).getAllByRole('link').map((link) => link.textContent)).toEqual(['專案', 'AGY']);
    expect(within(location).getByRole('link', { name: 'AGY' })).toHaveAttribute('href', '/projects/AGY');
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '所有變更集' })).toBeInTheDocument();
  });

  it('標題在內容區時（項目頁）頁列沒有 h1，位置的最後一個是目前的頁面', async () => {
    // Arrange
    renderInRouter(
      <PageBar
        title="AGY-7 首頁"
        titleInBar={false}
        crumbs={[{ label: 'AGY', to: '/projects/AGY', mono: true }, { label: 'AGY-7', mono: true }]}
      />,
    );

    // Act
    const current = screen.getByText('AGY-7');

    // Assert
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
    expect(current).toHaveAttribute('aria-current', 'page');
    await waitFor(() => {
      expect(document.title).toBe(`AGY-7 首頁 · ${PRODUCT_NAME}`);
    });
  });

  it('給了 headingRef 時標題可以用程式取得焦點（Tab 不停在它上面）', () => {
    // Arrange
    const headingRef = createRef<HTMLHeadingElement>();
    renderInRouter(<PageBar title="專案" headingRef={headingRef} />);

    // Act
    headingRef.current?.focus();

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: '專案' })).toHaveFocus();
    expect(headingRef.current).toHaveAttribute('tabindex', '-1');
  });

  it('documentTitle：分頁的標題和 h1 不同時（設定頁「設定 · 流程」），h1 照舊是 title', async () => {
    // Arrange / Act
    renderInRouter(<PageBar title="設定" documentTitle="設定 · 流程" />);

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: '設定' })).toBeInTheDocument();
    await waitFor(() => {
      expect(document.title).toBe(`設定 · 流程 · ${PRODUCT_NAME}`);
    });
  });
});
