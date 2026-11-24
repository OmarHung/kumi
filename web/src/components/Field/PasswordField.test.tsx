import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { PasswordField } from './PasswordField';

describe('PasswordField', () => {
  it('預設遮住；「顯示新密碼」是切換按鈕，按了變成看得到的文字，焦點留在按鈕上', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<PasswordField label="新密碼" value="example-pass" onChange={() => undefined} />);
    const input = screen.getByLabelText('新密碼');
    const toggle = screen.getByRole('button', { name: '顯示新密碼' });

    // Act
    await user.click(toggle);

    // Assert
    expect(input).toHaveAttribute('type', 'text');
    expect(toggle).toHaveAttribute('aria-pressed', 'true');
    expect(toggle).toHaveFocus();
  });

  it('再按一次遮回去；名稱不變，狀態在 aria-pressed', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<PasswordField label="新密碼" value="example-pass" onChange={() => undefined} />);
    const toggle = screen.getByRole('button', { name: '顯示新密碼' });
    await user.click(toggle);

    // Act
    await user.click(toggle);

    // Assert
    expect(screen.getByLabelText('新密碼')).toHaveAttribute('type', 'password');
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
  });

  it('顯示出來時不讓瀏覽器拼字檢查、自動大寫或修正密碼', () => {
    // Arrange
    render(<PasswordField label="新密碼" value="" onChange={() => undefined} />);

    // Act
    const input = screen.getByLabelText('新密碼');

    // Assert
    expect(input).toHaveAttribute('spellcheck', 'false');
    expect(input).toHaveAttribute('autocapitalize', 'none');
    expect(input).toHaveAttribute('autocorrect', 'off');
  });

  it('提示與錯誤連到輸入框，有錯時標成無效', () => {
    // Arrange
    render(<PasswordField label="新密碼" hint="至少 10 個字元" error="密碼至少要 10 個字元。" value="" onChange={() => undefined} />);

    // Act
    const input = screen.getByLabelText('新密碼');

    // Assert
    expect(input).toHaveAccessibleDescription('至少 10 個字元 密碼至少要 10 個字元。');
    expect(input).toHaveAttribute('aria-invalid', 'true');
  });

  it('ref 指到輸入框，輸入照常交給 onChange', async () => {
    // Arrange
    const user = userEvent.setup();
    const ref = createRef<HTMLInputElement>();
    const onChange = vi.fn();
    render(<PasswordField ref={ref} label="目前的密碼" value="" onChange={onChange} />);

    // Act
    await user.type(screen.getByLabelText('目前的密碼'), 'a');

    // Assert
    expect(ref.current).toBe(screen.getByLabelText('目前的密碼'));
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('停用時切換按鈕也停用', () => {
    // Arrange
    render(<PasswordField label="新密碼" value="" disabled onChange={() => undefined} />);

    // Act
    const toggle = screen.getByRole('button', { name: '顯示新密碼' });

    // Assert
    expect(toggle).toBeDisabled();
  });
});
