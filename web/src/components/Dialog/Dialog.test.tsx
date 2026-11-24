import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { Dialog } from './Dialog';

function Opener({ size }: { readonly size?: 'md' | 'lg' }) {
  const [isOpen, setIsOpen] = useState(false);
  return (
    <>
      <button
        type="button"
        onClick={() => {
          setIsOpen(true);
        }}
      >
        成員
      </button>
      {isOpen && (
        <Dialog
          title="成員"
          size={size}
          onClose={() => {
            setIsOpen(false);
          }}
        >
          <button type="button">加入成員</button>
        </Dialog>
      )}
    </>
  );
}

describe('Dialog', () => {
  it('打開時焦點進去，Esc 關上之後回到打開它的按鈕', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Opener />);
    const opener = screen.getByRole('button', { name: '成員' });

    // Act
    await user.click(opener);
    const focusedInside = document.activeElement?.textContent;
    await user.keyboard('{Escape}');

    // Assert
    expect(focusedInside).toBe('加入成員');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
  });

  it('點在對話框外面的背景時焦點留在對話框裡（不掉到頁面本體，之後的 Esc 才不會交給背後的面板）', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Opener />);
    await user.click(screen.getByRole('button', { name: '成員' }));
    const dialog = screen.getByRole('dialog', { name: '成員' });
    const backdrop = dialog.parentElement as HTMLElement;

    // Act
    const isNotPrevented = fireEvent.mouseDown(backdrop);

    // Assert
    expect(isNotPrevented).toBe(false);
    expect(screen.getByRole('button', { name: '加入成員' })).toHaveFocus();
    // 對話框裡面的點擊照常（例如把焦點放進輸入框）。
    expect(fireEvent.mouseDown(screen.getByRole('button', { name: '加入成員' }))).toBe(true);
  });

  it('輸入法選字中的 Esc 是取消選字，不關掉對話框', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Opener />);
    await user.click(screen.getByRole('button', { name: '成員' }));
    const inside = screen.getByRole('button', { name: '加入成員' });

    // Act
    fireEvent.keyDown(inside, { key: 'Escape', isComposing: true });
    fireEvent.keyDown(inside, { key: 'Escape', keyCode: 229 });

    // Assert
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('size="lg" 是有表格的對話框：比較寬的那一種', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Opener size="lg" />);

    // Act
    await user.click(screen.getByRole('button', { name: '成員' }));

    // Assert
    expect(screen.getByRole('dialog', { name: '成員' })).toHaveAttribute('data-size', 'lg');
  });

  it('沒給 size 是一般的寬度', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Opener />);

    // Act
    await user.click(screen.getByRole('button', { name: '成員' }));

    // Assert
    expect(screen.getByRole('dialog', { name: '成員' })).toHaveAttribute('data-size', 'md');
    expect(screen.getByRole('button', { name: '加入成員' })).toHaveFocus();
  });
});
