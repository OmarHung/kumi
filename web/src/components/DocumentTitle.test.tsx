import { render, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PRODUCT_NAME } from '../config/product';
import { DocumentTitle, DocumentTitleCountContext, formatDocumentTitle } from './DocumentTitle';

describe('formatDocumentTitle', () => {
  it('沒有未讀時只有頁名與產品名稱', () => {
    // Arrange & Act
    const unknown = formatDocumentTitle('收件匣', undefined);
    const none = formatDocumentTitle('收件匣', 0);

    // Assert
    expect(unknown).toBe(`收件匣 · ${PRODUCT_NAME}`);
    expect(none).toBe(`收件匣 · ${PRODUCT_NAME}`);
  });

  it('有未讀時前面加上括號裡的數字', () => {
    // Arrange & Act
    const title = formatDocumentTitle('我的工作', 3);

    // Assert
    expect(title).toBe(`(3) 我的工作 · ${PRODUCT_NAME}`);
  });

  it('超過 99 則寫成 99+，分頁的寬度留給頁名', () => {
    // Arrange & Act
    const atLimit = formatDocumentTitle('我的工作', 99);
    const overLimit = formatDocumentTitle('我的工作', 120);

    // Assert
    expect(atLimit).toBe(`(99) 我的工作 · ${PRODUCT_NAME}`);
    expect(overLimit).toBe(`(99+) 我的工作 · ${PRODUCT_NAME}`);
  });
});

describe('DocumentTitle', () => {
  it('外框提供的未讀數放在分頁標題前面', async () => {
    // Arrange & Act
    render(
      <DocumentTitleCountContext value={3}>
        <DocumentTitle title="收件匣" />
      </DocumentTitleCountContext>,
    );

    // Assert
    await waitFor(() => {
      expect(document.title).toBe(`(3) 收件匣 · ${PRODUCT_NAME}`);
    });
  });

  it('外框以外的頁面（登入頁這類）不帶數字', async () => {
    // Arrange & Act
    render(<DocumentTitle title="登入" />);

    // Assert
    await waitFor(() => {
      expect(document.title).toBe(`登入 · ${PRODUCT_NAME}`);
    });
  });
});
