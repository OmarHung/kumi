import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ToolbarDivider } from './Toolbar';

describe('ToolbarDivider', () => {
  it('是裝飾：報讀軟體略過', () => {
    // Arrange / Act
    const { container } = render(<ToolbarDivider />);

    // Assert
    expect(container.firstElementChild).toHaveAttribute('aria-hidden', 'true');
  });
});
