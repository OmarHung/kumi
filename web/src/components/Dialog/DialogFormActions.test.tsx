import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DialogFormActions } from './Dialog';

function Actions({ isPending, keepsFocusWhilePending }: { readonly isPending: boolean; readonly keepsFocusWhilePending?: boolean }) {
  return (
    <form>
      <DialogFormActions
        submitLabel="填好並移到「結案」"
        pendingLabel="送出中…"
        isPending={isPending}
        keepsFocusWhilePending={keepsFocusWhilePending}
        onCancel={() => undefined}
      />
    </form>
  );
}

describe('DialogFormActions', () => {
  it('送出中：兩個按鈕都停用，主要按鈕寫送出中的文字', () => {
    // Act
    render(<Actions isPending />);

    // Assert
    expect(screen.getByRole('button', { name: '送出中…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: '取消' })).toBeDisabled();
  });

  it('keepsFocusWhilePending：送出中主要按鈕不停用、改標 aria-disabled，焦點留在它身上', () => {
    // Arrange
    const { rerender } = render(<Actions isPending={false} keepsFocusWhilePending />);
    screen.getByRole('button', { name: '填好並移到「結案」' }).focus();

    // Act
    rerender(<Actions isPending keepsFocusWhilePending />);

    // Assert
    const submit = screen.getByRole('button', { name: '送出中…' });
    expect(submit).toBeEnabled();
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    expect(submit).toHaveFocus();
    expect(screen.getByRole('button', { name: '取消' })).toBeDisabled();
  });

  it('keepsFocusWhilePending 不在送出中時是一般的按鈕', () => {
    // Act
    render(<Actions isPending={false} keepsFocusWhilePending />);

    // Assert
    expect(screen.getByRole('button', { name: '填好並移到「結案」' })).not.toHaveAttribute('aria-disabled');
  });
});
