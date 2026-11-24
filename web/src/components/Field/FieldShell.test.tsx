import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FieldShell } from './Field';

describe('FieldShell', () => {
  it('給不是 TextField 的控制項：看得到的標籤連到它，說明與錯誤用 aria-describedby 連過去', () => {
    // Arrange
    render(
      <FieldShell label="客戶驗收日" hint="客戶簽名的那一天。" error="請填「客戶驗收日」。">
        {({ id, describedBy }) => <input id={id} type="date" aria-describedby={describedBy} />}
      </FieldShell>,
    );

    // Act
    const input = screen.getByLabelText('客戶驗收日');

    // Assert
    expect(screen.getByText('客戶驗收日', { selector: 'label' })).toBeVisible();
    expect(input).toHaveAccessibleDescription('客戶簽名的那一天。 請填「客戶驗收日」。');
  });

  it('給了 id 就用它（呼叫端要找到控制項，例如把焦點放到第一個有問題的欄位）', () => {
    // Arrange
    render(
      <FieldShell label="估計" id="fill-estimate" error="請輸入數字。">
        {({ id, describedBy }) => <input id={id} aria-describedby={describedBy} />}
      </FieldShell>,
    );

    // Act
    const input = screen.getByRole('textbox', { name: '估計' });

    // Assert
    expect(input).toHaveAttribute('id', 'fill-estimate');
    expect(input).toHaveAccessibleDescription('請輸入數字。');
  });

  it('hasOwnLabel：控制項自己帶看得到的標籤（一組勾選的 legend），這裡不再畫 label', () => {
    // Arrange
    render(
      <FieldShell label="通路" hasOwnLabel error="請填「通路」。">
        {({ id, describedBy }) => (
          <fieldset id={id} aria-describedby={describedBy}>
            <legend>通路</legend>
            <label>
              <input type="checkbox" />
              網站
            </label>
          </fieldset>
        )}
      </FieldShell>,
    );

    // Act
    const group = screen.getByRole('group', { name: '通路' });

    // Assert
    expect(screen.queryByText('通路', { selector: 'label' })).not.toBeInTheDocument();
    expect(group).toHaveAccessibleDescription('請填「通路」。');
  });
});
