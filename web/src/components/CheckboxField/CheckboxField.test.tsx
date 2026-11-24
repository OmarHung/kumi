import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { CheckboxField, CheckboxGroup } from './CheckboxField';

function Controlled() {
  const [checked, setChecked] = useState(false);
  return (
    <CheckboxField
      label="限制轉換"
      hint="只有勾起來的轉換可以走。"
      checked={checked}
      onChange={(event) => {
        setChecked(event.target.checked);
      }}
    />
  );
}

describe('CheckboxField', () => {
  it('原生的勾選框，標籤是名稱，提示連到勾選框', () => {
    // Arrange
    render(<Controlled />);

    // Act
    const checkbox = screen.getByRole('checkbox', { name: '限制轉換' });

    // Assert
    expect(checkbox).not.toBeChecked();
    expect(checkbox).toHaveAccessibleDescription('只有勾起來的轉換可以走。');
  });

  it('點標籤的文字也會勾起來', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Controlled />);

    // Act
    await user.click(screen.getByText('限制轉換'));

    // Assert
    expect(screen.getByRole('checkbox', { name: '限制轉換' })).toBeChecked();
  });

  it('呼叫端給的 aria-describedby 也留著', () => {
    // Arrange
    render(
      <>
        <CheckboxField label="必填" aria-describedby="note" defaultChecked />
        <p id="note">送出前要填。</p>
      </>,
    );

    // Act
    const checkbox = screen.getByRole('checkbox', { name: '必填' });

    // Assert
    expect(checkbox).toBeChecked();
    expect(checkbox).toHaveAccessibleDescription('送出前要填。');
  });
});

describe('CheckboxGroup', () => {
  it('一組勾選是有名稱的 fieldset，說明與錯誤連到這一組', () => {
    // Arrange
    render(
      <CheckboxGroup legend="套用到" hint="不勾就不套用。" error="至少選一個類型。">
        <CheckboxField label="需求" />
        <CheckboxField label="任務" />
      </CheckboxGroup>,
    );

    // Act
    const group = screen.getByRole('group', { name: '套用到' });

    // Assert
    expect(group).toHaveAccessibleDescription('不勾就不套用。 至少選一個類型。');
    expect(screen.getAllByRole('checkbox')).toHaveLength(2);
  });

  it('標題可以只給報讀軟體', () => {
    // Arrange
    render(
      <CheckboxGroup legend="顯示的欄位" isLegendHidden>
        <CheckboxField label="備註" />
      </CheckboxGroup>,
    );

    // Act
    const group = screen.getByRole('group', { name: '顯示的欄位' });

    // Assert
    expect(group).toBeInTheDocument();
  });

  it('預設排成一列，layout="column" 一個一行', () => {
    // Arrange
    render(
      <>
        <CheckboxGroup legend="預設">
          <CheckboxField label="甲" />
        </CheckboxGroup>
        <CheckboxGroup legend="直排" layout="column">
          <CheckboxField label="乙" />
        </CheckboxGroup>
      </>,
    );

    // Act
    const layoutOf = (name: string) => screen.getByRole('group', { name }).querySelector('[data-layout]')?.getAttribute('data-layout');

    // Assert
    expect([layoutOf('預設'), layoutOf('直排')]).toEqual(['row', 'column']);
  });
});
