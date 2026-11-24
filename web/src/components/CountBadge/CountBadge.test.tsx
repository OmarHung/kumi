import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CountBadge, CountDescription } from './CountBadge';

describe('CountBadge', () => {
  it('畫面上只有數字（aria-hidden）；中性的沒有圖示', () => {
    // Arrange / Act
    render(
      <p>
        流程 <CountBadge value={2} />
      </p>,
    );

    // Assert
    const count = screen.getByText('2');
    expect(count).toHaveAttribute('aria-hidden', 'true');
    expect(count).toHaveAttribute('data-tone', 'neutral');
    expect(count.firstElementChild).toBeNull();
  });

  it('危險的除了顏色還有錯誤的圖示：不只靠顏色', () => {
    // Arrange / Act
    render(<CountBadge value={1} tone="danger" />);

    // Assert
    const count = screen.getByText('1');
    expect(count).toHaveAttribute('data-tone', 'danger');
    expect(count.firstElementChild).toHaveAttribute('data-icon', 'error');
    expect(count.firstElementChild).toHaveAttribute('aria-hidden', 'true');
  });

  it('完整的說法放在 hidden 的元素裡，用 aria-describedby 連過去；名稱不變', () => {
    // Arrange / Act
    render(
      <>
        <a href="/my-work" aria-describedby="my-work-counts">
          我的工作
          <CountBadge value={3} />
          <CountBadge value={1} tone="danger" />
        </a>
        <CountDescription id="my-work-counts" labels={['3 項', '1 項已過期']} />
      </>,
    );

    // Assert
    const link = screen.getByRole('link', { name: '我的工作' });
    expect(link).toHaveAccessibleDescription('3 項、1 項已過期');
    expect(screen.queryByText('3 項、1 項已過期')).not.toBeVisible();
  });
});
