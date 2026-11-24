import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Icon, ICON_NAMES } from './Icon';

describe('Icon', () => {
  it('每個語意名稱都畫得出一個裝飾用的 SVG', () => {
    // Arrange
    const names = ICON_NAMES;

    // Act
    const icons = names.map((name) => render(<Icon name={name} />).container.firstElementChild);

    // Assert
    expect(names.length).toBeGreaterThan(40);
    icons.forEach((icon) => {
      expect(icon?.tagName.toLowerCase()).toBe('svg');
      expect(icon).toHaveAttribute('aria-hidden', 'true');
    });
  });

  it('預設 16px，可以換成 20px', () => {
    // Arrange
    const { container } = render(
      <>
        <Icon name="add" />
        <Icon name="close" size={20} />
      </>,
    );

    // Act
    const [small, large] = Array.from(container.children);

    // Assert
    expect(small).toHaveAttribute('width', '16');
    expect(large).toHaveAttribute('width', '20');
  });

  it('放在按鈕裡不影響按鈕的名稱', () => {
    // Arrange
    render(
      <button type="button">
        <Icon name="add" />
        新增項目
      </button>,
    );

    // Act
    const button = screen.getByRole('button');

    // Assert
    expect(button).toHaveAccessibleName('新增項目');
  });
});
