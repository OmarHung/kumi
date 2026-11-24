import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Badge } from './Badge';

describe('Badge', () => {
  it('意思寫在文字裡', () => {
    // Arrange
    render(<Badge tone="danger">已過期</Badge>);

    // Act
    const badge = screen.getByText('已過期');

    // Assert
    expect(badge).toHaveTextContent(/^已過期$/);
  });

  it('藍圖選項的藍與紫有自己的色調（不借強調色）：色調標在 data-tone，預設是中性', () => {
    // Arrange
    render(
      <>
        <Badge tone="blue">藍</Badge>
        <Badge tone="purple">紫</Badge>
        <Badge>內建</Badge>
      </>,
    );

    // Act
    const tones = ['藍', '紫', '內建'].map((text) => screen.getByText(text).getAttribute('data-tone'));

    // Assert
    expect(tones).toEqual(['blue', 'purple', 'neutral']);
  });

  it('可以帶圖示；圖示是裝飾，不改變文字', () => {
    // Arrange
    render(
      <Badge tone="stateDone" icon="stateDone">
        完成
      </Badge>,
    );

    // Act
    const badge = screen.getByText('完成');
    const icon = badge.firstElementChild;

    // Assert
    expect(badge).toHaveTextContent(/^完成$/);
    expect(icon).toHaveAttribute('data-icon', 'stateDone');
    expect(icon).toHaveAttribute('aria-hidden', 'true');
  });
});

describe('Badge：前綴', () => {
  it('純文字的標籤前面沒有任何小圓點或圖示，帶 icon 才有圖示', () => {
    // Arrange / Act
    const { container } = render(
      <>
        <Badge>內建</Badge>
        <Badge tone="stateDone" icon="stateDone">
          完成
        </Badge>
      </>,
    );

    // Assert
    const [plain, withIcon] = Array.from(container.querySelectorAll('[data-tone]'));
    expect(plain?.children).toHaveLength(0);
    expect(withIcon?.querySelector('svg')).not.toBeNull();
  });
});
