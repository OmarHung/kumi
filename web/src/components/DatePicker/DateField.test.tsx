import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DateField } from './DateField';

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 17, 10, 0, 0));
});

afterEach(() => {
  vi.useRealTimers();
});

describe('DateField（標籤加 DatePicker，像 SelectField 之於 Select）', () => {
  it('標籤看得到，名稱就是標籤；說明與錯誤連到輸入框，有錯時 aria-invalid', () => {
    // Arrange
    render(<DateField label="到期日" hint="格式 2026/12/15。" error="到期日不能早於開始日。" value="2026-12-15" onChange={vi.fn()} />);

    // Act
    const input = screen.getByRole('combobox', { name: '到期日' });

    // Assert
    expect(input).toHaveValue('2026/12/15');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('格式 2026/12/15。 到期日不能早於開始日。');
    expect(screen.getByText('到期日', { selector: 'label' })).not.toHaveClass('visually-hidden');
  });

  it('點標籤等於點輸入框：焦點到輸入框；點輸入框打開日曆，選了呼叫 onChange', async () => {
    // Arrange
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<DateField label="開始日" value="" onChange={onChange} />);

    // Act
    await user.click(screen.getByText('開始日', { selector: 'label' }));
    const focusedByLabel = document.activeElement === screen.getByRole('combobox', { name: '開始日' });
    await user.click(screen.getByRole('combobox', { name: '開始日' }));
    await user.click(await screen.findByRole('gridcell', { name: '2026 年 11 月 20 日 星期五' }));

    // Assert
    expect(focusedByLabel).toBe(true);
    expect(onChange).toHaveBeenCalledExactlyOnceWith('2026-11-20');
  });

  it('點標籤會打開日曆（標籤指到輸入框，點它等於點輸入框），焦點在輸入框', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<DateField label="到期日" value="2026-12-15" onChange={vi.fn()} />);

    // Act
    await user.click(screen.getByText('到期日', { selector: 'label' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: '選擇日期' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '到期日' })).toHaveFocus();
  });

  it('size="sm" 與 isLabelHidden：矮一號、標籤只給報讀軟體，名稱不變', () => {
    // Arrange
    render(<DateField label="到期日" size="sm" isLabelHidden value="" onChange={vi.fn()} />);

    // Act
    const input = screen.getByRole('combobox', { name: '到期日' });

    // Assert
    expect(input).toHaveAttribute('data-size', 'sm');
    expect(screen.getByText('到期日', { selector: 'label' })).toHaveClass('visually-hidden');
  });

  it('沒有錯時不標無效', () => {
    // Arrange
    render(<DateField label="到期日" value="" onChange={vi.fn()} />);

    // Act
    const input = screen.getByRole('combobox', { name: '到期日' });

    // Assert
    expect(input).not.toHaveAttribute('aria-invalid');
    expect(input).not.toHaveAccessibleDescription();
  });
});
