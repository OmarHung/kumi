import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Skeleton } from './Skeleton';

describe('Skeleton', () => {
  it('畫面上是內容的形狀，報讀軟體唸「載入中…」', () => {
    // Arrange
    render(<Skeleton rows={5} />);

    // Act
    const status = screen.getByRole('status');

    // Assert
    expect(status).toHaveTextContent(/^載入中…$/);
  });

  it('可以換成這個區塊自己的說法', () => {
    // Arrange
    render(<Skeleton label="正在載入項目…" variant="text" />);

    // Act
    const status = screen.getByRole('status');

    // Assert
    expect(status).toHaveTextContent('正在載入項目…');
  });
});
