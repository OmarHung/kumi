import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Toolbar } from './Toolbar';

describe('Toolbar', () => {
  it('排出控制項，靠右的另外放；不加 role="toolbar"（那要方向鍵操作）', () => {
    // Arrange
    render(
      <Toolbar end={<button type="button">顯示的欄位</button>}>
        <label>
          搜尋
          <input />
        </label>
      </Toolbar>,
    );

    // Act
    const search = screen.getByRole('textbox', { name: '搜尋' });

    // Assert
    expect(search).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '顯示的欄位' })).toBeInTheDocument();
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
  });
});
