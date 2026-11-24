import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { IconButton } from './IconButton';

describe('IconButton', () => {
  it('label 同時是無障礙名稱與滑過時的提示', () => {
    // Arrange
    render(<IconButton icon="close" label="關閉" />);

    // Act
    const button = screen.getByRole('button', { name: '關閉' });

    // Assert
    expect(button).toHaveAttribute('title', '關閉');
    expect(button).toHaveAttribute('type', 'button');
  });

  it('點了會呼叫 onClick；停用時不會', async () => {
    // Arrange
    const user = userEvent.setup();
    const onClick = vi.fn();
    const onDisabledClick = vi.fn();
    render(
      <>
        <IconButton icon="arrowUp" label="上移" onClick={onClick} />
        <IconButton icon="arrowDown" label="下移" onClick={onDisabledClick} disabled />
      </>,
    );

    // Act
    await user.click(screen.getByRole('button', { name: '上移' }));
    await user.click(screen.getByRole('button', { name: '下移' }));

    // Assert
    expect(onClick).toHaveBeenCalledTimes(1);
    expect(onDisabledClick).not.toHaveBeenCalled();
  });

  it('其他屬性與 ref 照樣傳給按鈕', () => {
    // Arrange
    const ref = createRef<HTMLButtonElement>();
    render(
      <>
        <IconButton ref={ref} icon="delete" label="刪除" aria-describedby="reason" variant="secondary" size="sm" />
        <p id="reason">最後一個類型不能刪除。</p>
      </>,
    );

    // Act
    const button = screen.getByRole('button', { name: '刪除' });

    // Assert
    expect(ref.current).toBe(button);
    expect(button).toHaveAccessibleDescription('最後一個類型不能刪除。');
  });
});
