import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { StateCategory } from '../../api/blueprint';
import { EmptyValue, StateBadge } from './ItemCells';

describe('StateBadge', () => {
  it('五種類別各有自己的形狀，文字是藍圖裡的狀態名稱', () => {
    // Arrange
    const cases: readonly (readonly [StateCategory, string, string])[] = [
      ['backlog', '待規劃', 'stateBacklog'],
      ['not_started', '待辦', 'stateNotStarted'],
      ['in_progress', '開發', 'stateInProgress'],
      ['done', '結案', 'stateDone'],
      ['canceled', '取消', 'stateCanceled'],
    ];

    // Act
    render(
      <>
        {cases.map(([category, label]) => (
          <StateBadge key={category} category={category} label={label} />
        ))}
      </>,
    );

    // Assert
    cases.forEach(([, label, icon]) => {
      const badge = screen.getByText(label);
      expect(badge).toHaveTextContent(new RegExp(`^${label}$`));
      expect(badge.firstElementChild).toHaveAttribute('data-icon', icon);
    });
  });
});

describe('EmptyValue', () => {
  it('畫面上是一條裝飾的短線（不是文字符號），報讀軟體唸出沒有值的說法', () => {
    // Arrange / Act
    const { container } = render(<EmptyValue label="沒有負責人" />);

    // Assert
    expect(screen.getByText('沒有負責人')).toBeInTheDocument();
    expect(container).toHaveTextContent(/^沒有負責人$/);
  });
});
