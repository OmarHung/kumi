import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderInRouter } from '../../test/renderInRouter';
import { PageBar } from '../PageBar/PageBar';
import { Toolbar } from '../Toolbar/Toolbar';
import { PageLayout } from './PageLayout';

describe('PageLayout', () => {
  it('由上而下是頁列、工具列、內容', () => {
    // Arrange
    renderInRouter(
      <PageLayout
        variant="workspace"
        bar={<PageBar title="接案測試" />}
        toolbar={
          <Toolbar>
            <button type="button">清除條件</button>
          </Toolbar>
        }
      >
        <p>第一筆內容</p>
      </PageLayout>,
    );

    // Act
    const heading = screen.getByRole('heading', { level: 1, name: '接案測試' });
    const button = screen.getByRole('button', { name: '清除條件' });
    const content = screen.getByText('第一筆內容');

    // Assert
    expect(heading.compareDocumentPosition(button) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(button.compareDocumentPosition(content) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('工具列可以省略', () => {
    // Arrange
    renderInRouter(
      <PageLayout variant="reading" bar={<PageBar title="變更集 #3" />}>
        <p>摘要</p>
      </PageLayout>,
    );

    // Act
    const content = screen.getByText('摘要');

    // Assert
    expect(content).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
