import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { FloatingPanel } from './FloatingPanel';

describe('FloatingPanel', () => {
  it('點遮罩：焦點先離開編輯中的輸入框（觸發它的 blur 送出）再關上', async () => {
    // Arrange
    const user = userEvent.setup();
    const calls: string[] = [];
    const onClose = vi.fn(() => calls.push('close'));
    render(
      <FloatingPanel label="項目 X-1" title="X-1" onClose={onClose}>
        <input
          aria-label="標題"
          onBlur={() => {
            calls.push('blur');
          }}
        />
      </FloatingPanel>,
    );
    await user.click(screen.getByRole('textbox', { name: '標題' }));

    // Act
    await user.click(screen.getByRole('dialog').parentElement as HTMLElement);

    // Assert
    expect(calls).toEqual(['blur', 'close']);
  });
});
