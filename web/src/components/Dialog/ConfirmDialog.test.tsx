import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ConfirmDialog } from './ConfirmDialog';

interface ConfirmProps {
  readonly isPending: boolean;
  readonly keepsFocusWhilePending?: boolean;
  readonly onConfirm?: () => void;
  readonly onCancel?: () => void;
}

function Confirm({ isPending, keepsFocusWhilePending, onConfirm = () => undefined, onCancel = () => undefined }: ConfirmProps) {
  return (
    <ConfirmDialog
      title="停用「範例成員」的帳號？"
      confirmLabel="停用帳號"
      pendingLabel="停用中…"
      tone="danger"
      isPending={isPending}
      keepsFocusWhilePending={keepsFocusWhilePending}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

describe('ConfirmDialog', () => {
  it('送出中：兩個按鈕都停用，確認鈕寫送出中的文字', () => {
    // Act
    render(<Confirm isPending />);

    // Assert
    expect(screen.getByRole('button', { name: '停用中…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: '取消' })).toBeDisabled();
  });

  it('keepsFocusWhilePending：送出中確認鈕不停用、改標 aria-disabled，焦點留在它身上，再按不會再確認一次', async () => {
    // Arrange
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    const { rerender } = render(<Confirm isPending={false} keepsFocusWhilePending onConfirm={onConfirm} />);
    await user.click(screen.getByRole('button', { name: '停用帳號' }));

    // Act
    rerender(<Confirm isPending keepsFocusWhilePending onConfirm={onConfirm} />);
    const confirm = screen.getByRole('button', { name: '停用中…' });
    await user.click(confirm);

    // Assert
    expect(confirm).toBeEnabled();
    expect(confirm).toHaveAttribute('aria-disabled', 'true');
    expect(confirm).toHaveFocus();
    expect(screen.getByRole('button', { name: '取消' })).toBeDisabled();
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('keepsFocusWhilePending：送出中按 Esc 不關上；送完（失敗）之後 Esc 照常關上', async () => {
    // Arrange
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const { rerender } = render(<Confirm isPending keepsFocusWhilePending onCancel={onCancel} />);
    screen.getByRole('button', { name: '停用中…' }).focus();

    // Act
    await user.keyboard('{Escape}');
    const whilePending = onCancel.mock.calls.length;
    rerender(<Confirm isPending={false} keepsFocusWhilePending onCancel={onCancel} />);
    await user.keyboard('{Escape}');

    // Assert
    expect(whilePending).toBe(0);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('keepsFocusWhilePending 不在送出中時是一般的按鈕', () => {
    // Act
    render(<Confirm isPending={false} keepsFocusWhilePending />);

    // Assert
    expect(screen.getByRole('button', { name: '停用帳號' })).not.toHaveAttribute('aria-disabled');
  });
});
