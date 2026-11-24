import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { openOptions } from '../../test/select';
import { SelectField, TextField } from './Field';

describe('TextField', () => {
  it('錯誤寫在欄位下方，用 aria-describedby 連過去；前面的圖示不算在說明裡', () => {
    // Arrange
    render(<TextField label="名稱" hint="最多 100 個字。" error="名稱必填。" />);

    // Act
    const input = screen.getByRole('textbox', { name: '名稱' });

    // Assert
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('最多 100 個字。 名稱必填。');
  });
});

describe('TextField 的 leadingIcon', () => {
  it('開頭的圖示是裝飾（aria-hidden），名稱與說明照舊', () => {
    // Arrange
    render(<TextField label="搜尋" hint="標題或編號。" leadingIcon="search" />);

    // Act
    const input = screen.getByRole('textbox', { name: '搜尋' });
    const icon = input.parentElement?.querySelector('[data-icon="search"]');

    // Assert
    expect(icon).toHaveAttribute('aria-hidden', 'true');
    expect(input).toHaveAccessibleDescription('標題或編號。');
  });

  it('沒給就沒有圖示，輸入框照舊直接放在欄位裡', () => {
    // Arrange
    render(<TextField label="名稱" />);

    // Act
    const input = screen.getByRole('textbox', { name: '名稱' });

    // Assert
    expect(input.parentElement?.querySelector('[data-icon]')).toBeNull();
    expect(input.parentElement?.querySelector('label')).not.toBeNull();
  });
});

const TYPE_OPTIONS = [
  { value: 'request', label: '需求' },
  { value: 'task', label: '任務' },
];

describe('SelectField', () => {
  it('標籤看得到，控制項是 Select（按鈕），值是按鈕裡的文字；沒有錯誤時不標無效', () => {
    // Arrange
    render(<SelectField label="類型" value="task" options={TYPE_OPTIONS} onChange={vi.fn()} />);

    // Act
    const select = screen.getByRole('combobox', { name: '類型' });

    // Assert
    expect(select.tagName).toBe('BUTTON');
    expect(select).toHaveTextContent('任務');
    expect(select).not.toHaveAttribute('aria-invalid');
  });

  it('有錯誤時標成無效並連到訊息', () => {
    // Arrange
    render(<SelectField label="流程" error="先選一個流程。" value="" placeholder="請選擇" options={[]} onChange={vi.fn()} />);

    // Act
    const select = screen.getByRole('combobox', { name: '流程' });

    // Assert
    expect(select).toHaveAttribute('aria-invalid', 'true');
    expect(select).toHaveAccessibleDescription('先選一個流程。');
  });

  it('options 必給：原生的下拉那條路拿掉了，沒給選項的寫法過不了型別檢查', () => {
    // Arrange / Act
    // @ts-expect-error 沒給 options：SelectField 一定是 Select，不再退回寫 <option> 的原生下拉。
    const element = <SelectField label="類型" value="" onChange={vi.fn()} />;

    // Assert
    expect(element.props).not.toHaveProperty('options');
  });
});

describe('SelectField 給了 options（components/Select）', () => {
  it('標籤看得到、名稱就是標籤；值是按鈕裡的文字，清單和按鈕同名', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<SelectField label="類型" value="task" options={TYPE_OPTIONS} onChange={vi.fn()} />);
    const select = screen.getByRole('combobox', { name: '類型' });

    // Act
    const options = await openOptions(user, select);

    // Assert
    expect(select.tagName).toBe('BUTTON');
    expect(select).toHaveTextContent('任務');
    expect(screen.getByText('類型', { selector: 'label' })).not.toHaveClass('visually-hidden');
    expect(options).toHaveLength(2);
    expect(await screen.findByRole('listbox', { name: '類型' })).toBeInTheDocument();
  });

  it('點標籤就打開清單（label 指到按鈕）；選了呼叫 onChange(value)', async () => {
    // Arrange
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<SelectField label="類型" value="task" options={TYPE_OPTIONS} onChange={onChange} />);

    // Act
    await user.click(screen.getByText('類型', { selector: 'label' }));
    await user.click(await screen.findByRole('option', { name: '需求' }));

    // Assert
    expect(onChange).toHaveBeenCalledWith('request');
  });

  it('點標籤打開時焦點補到按鈕上（Safari 點標籤不給按鈕焦點，之後的方向鍵、Esc 才接得到）', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<SelectField label="類型" value="task" options={TYPE_OPTIONS} onChange={vi.fn()} />);
    const select = screen.getByRole('combobox', { name: '類型' });

    // Act
    await user.click(screen.getByText('類型', { selector: 'label' }));

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(select).toHaveFocus();
  });

  it('清單開著時點標籤：和再按一次按鈕一樣關上，不會先關再開（指到按鈕的標籤不算「外面」）', async () => {
    // Arrange
    const user = userEvent.setup();
    const onDismiss = vi.fn();
    render(<SelectField label="類型" value="task" options={TYPE_OPTIONS} onChange={vi.fn()} onDismiss={onDismiss} />);
    const select = screen.getByRole('combobox', { name: '類型' });
    await openOptions(user, select);

    // Act
    await user.click(screen.getByText('類型', { selector: 'label' }));

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('toggle');
  });

  it('說明與錯誤寫在下方、連到按鈕，有錯時 aria-invalid', () => {
    // Arrange
    render(<SelectField label="流程" hint="之後可以改。" error="先選一個流程。" value="" placeholder="選擇流程" options={[]} onChange={vi.fn()} />);

    // Act
    const select = screen.getByRole('combobox', { name: '流程' });

    // Assert
    expect(select).toHaveAttribute('aria-invalid', 'true');
    expect(select).toHaveAccessibleDescription('之後可以改。 先選一個流程。');
    expect(select).toHaveTextContent('選擇流程');
  });

  it('size="sm" 與 isLabelHidden：矮一號、標籤只給報讀軟體，名稱不變；ref 指到按鈕', () => {
    // Arrange
    const ref = createRef<HTMLButtonElement>();
    render(<SelectField ref={ref} label="狀態" size="sm" isLabelHidden value="" options={[{ value: '', label: '所有狀態' }]} onChange={vi.fn()} />);

    // Act
    const select = screen.getByRole('combobox', { name: '狀態' });

    // Assert
    expect(select).toHaveAttribute('data-size', 'sm');
    expect(select).toHaveAttribute('data-width', 'content');
    expect(screen.getByText('狀態', { selector: 'label' })).toHaveClass('visually-hidden');
    expect(ref.current).toBe(select);
  });
});

describe('工具列上的欄位', () => {
  it('size="sm" 標在控制項上（高度是 --control-height-sm），不會變成原生的 size 屬性', () => {
    // Arrange
    render(
      <>
        <TextField label="搜尋" size="sm" />
        <SelectField label="狀態" size="sm" value="" options={[{ value: '', label: '所有狀態' }]} onChange={vi.fn()} />
      </>,
    );

    // Act
    const input = screen.getByRole('textbox', { name: '搜尋' });
    const select = screen.getByRole('combobox', { name: '狀態' });

    // Assert
    expect(input).toHaveAttribute('data-size', 'sm');
    expect(input).not.toHaveAttribute('size');
    expect(select).toHaveAttribute('data-size', 'sm');
    expect(select).not.toHaveAttribute('size');
  });

  it('isLabelHidden：標籤只給報讀軟體（global.css 的 visually-hidden），名稱不變', () => {
    // Arrange
    render(<TextField label="標題" isLabelHidden placeholder="輸入標題後按 Enter" />);

    // Act
    const label = screen.getByText('標題');

    // Assert
    expect(screen.getByRole('textbox', { name: '標題' })).toBeInTheDocument();
    expect(label).toHaveClass('visually-hidden');
  });

  it('isErrorInside：錯誤寫在輸入框的框裡（不在欄位下面多一行），名稱、說明與無效的標記照舊', () => {
    // Arrange
    render(<TextField label="標題" size="sm" isLabelHidden isErrorInside error="請輸入標題。" placeholder="輸入標題後按 Enter" />);

    // Act
    const input = screen.getByRole('textbox', { name: '標題' });
    const message = screen.getByText('請輸入標題。');

    // Assert
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('請輸入標題。');
    // 輸入框外面那一層只包著輸入框與錯誤（標籤在更外面）：錯誤畫在框裡，不是欄位下面的一行。
    expect(input.parentElement).toContainElement(message);
    expect(input.parentElement).not.toContainElement(screen.getByText('標題'));
  });

  it('isErrorInside：點框裡的錯誤訊息，焦點進到輸入框', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<TextField label="標題" size="sm" isLabelHidden isErrorInside error="請輸入標題。" />);

    // Act
    await user.click(screen.getByText('請輸入標題。'));

    // Assert
    expect(screen.getByRole('textbox', { name: '標題' })).toHaveFocus();
  });
});
