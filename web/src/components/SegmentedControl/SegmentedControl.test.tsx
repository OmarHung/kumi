import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { SegmentedControl } from './SegmentedControl';

type View = 'comments' | 'all';

const OPTIONS: readonly { readonly value: View; readonly label: string }[] = [
  { value: 'comments', label: '留言' },
  { value: 'all', label: '全部' },
];

function renderControl(value: View = 'comments', onChange: (value: View) => void = () => undefined) {
  return render(<SegmentedControl label="顯示" options={OPTIONS} value={value} onChange={onChange} />);
}

describe('SegmentedControl', () => {
  it('是有名稱的群組，每個選項一顆按鈕，目前的那顆 aria-pressed', () => {
    // Arrange
    renderControl('all');

    // Act
    const group = screen.getByRole('group', { name: '顯示' });

    // Assert
    expect(within(group).getByRole('button', { name: '全部' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group).getByRole('button', { name: '留言' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('點另一顆呼叫 onChange、帶那個選項的值；焦點留在按下的那一顆', async () => {
    // Arrange
    const onChange = vi.fn();
    renderControl('comments', onChange);

    // Act
    await userEvent.click(screen.getByRole('button', { name: '全部' }));

    // Assert
    expect(onChange).toHaveBeenCalledExactlyOnceWith('all');
    expect(screen.getByRole('button', { name: '全部' })).toHaveFocus();
  });

  it('點目前的那一顆什麼都不做', async () => {
    // Arrange
    const onChange = vi.fn();
    renderControl('comments', onChange);

    // Act
    await userEvent.click(screen.getByRole('button', { name: '留言' }));

    // Assert
    expect(onChange).not.toHaveBeenCalled();
  });

  it('鍵盤：Tab 停在每一顆，Space 與 Enter 都能按下', async () => {
    // Arrange
    const onChange = vi.fn();
    renderControl('comments', onChange);

    // Act
    await userEvent.tab();
    const first = screen.getByRole('button', { name: '留言' });
    const firstHasFocus = document.activeElement === first;
    await userEvent.tab();
    await userEvent.keyboard(' ');
    await userEvent.keyboard('{Enter}');

    // Assert
    expect(firstHasFocus).toBe(true);
    expect(screen.getByRole('button', { name: '全部' })).toHaveFocus();
    expect(onChange).toHaveBeenCalledTimes(2);
    expect(onChange).toHaveBeenCalledWith('all');
  });

  it('停用：按了不呼叫 onChange，用 aria-disabled 不用 disabled（焦點留得住），連著原因', async () => {
    // Arrange
    const onChange = vi.fn();
    render(<SegmentedControl label="顯示" options={OPTIONS} value="comments" onChange={onChange} isDisabled describedBy="why" />);
    const all = screen.getByRole('button', { name: '全部' });

    // Act
    await userEvent.click(all);

    // Assert
    expect(onChange).not.toHaveBeenCalled();
    expect(all).toHaveAttribute('aria-disabled', 'true');
    expect(all).not.toBeDisabled();
    expect(all).toHaveAttribute('aria-describedby', 'why');
    expect(all).toHaveFocus();
  });

  it('size="sm" 在群組上標出來，樣式據此縮小', () => {
    // Arrange / Act
    render(<SegmentedControl label="顯示" options={OPTIONS} value="comments" onChange={() => undefined} size="sm" />);

    // Assert
    expect(screen.getByRole('group', { name: '顯示' })).toHaveAttribute('data-size', 'sm');
  });
});
