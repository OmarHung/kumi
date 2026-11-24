import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { DatePicker } from './DatePicker';
import { cell, findCalendar, renderPicker, fixTodayForEachTest } from './datePickerTestUtils';

// DatePicker 的打字、onBlur、報讀與觸控。
fixTodayForEachTest();

describe('DatePicker 的打字', () => {
  it('打成完整的日期：onChange 收到 ISO（打字不呼叫 onPick），日曆跳到那個月並標出那一天', async () => {
    // Arrange
    const { user, input, onChange, onPick } = renderPicker();
    await user.click(input);
    await findCalendar();

    // Act
    await user.type(input, '2026/12/15');

    // Assert
    expect(onChange).toHaveBeenLastCalledWith('2026-12-15');
    expect(onPick).not.toHaveBeenCalled();
    expect(screen.getByRole('grid', { name: '2026 年 12 月' })).toBeInTheDocument();
    expect(cell('2026 年 12 月 15 日 星期二')).toHaveAttribute('aria-selected', 'true');
  });

  it.each([
    ['2026-12-15', '2026-12-15'],
    ['2026/1/5', '2026-01-05'],
    ['20261215', '2026-12-15'],
  ])('打「%s」：onChange 收到 %s', async (typed, expected) => {
    // Arrange
    const { user, input, onChange } = renderPicker();

    // Act
    await user.type(input, typed);

    // Assert
    expect(onChange).toHaveBeenLastCalledWith(expected);
  });

  it('解析不了的字傳原始文字（不默默吃掉）；清空傳空字串', async () => {
    // Arrange
    const { user, input, onChange } = renderPicker({ value: '2026-12-15' });

    // Act
    await user.clear(input);
    const cleared = onChange.mock.lastCall?.[0];
    await user.type(input, '2026/13/40');

    // Assert
    expect(cleared).toBe('');
    expect(onChange).toHaveBeenLastCalledWith('2026/13/40');
    expect(input).toHaveValue('2026/13/40');
  });

  it('打到一半不重新排版（2026/1/1 不會跳成 2026/01/01）；離開欄位時才排成 2026/01/15', async () => {
    // Arrange
    const { user, input } = renderPicker();

    // Act
    await user.type(input, '2026/1/15');
    const whileTyping = (input as HTMLInputElement).value;
    await user.tab();

    // Assert
    expect(whileTyping).toBe('2026/1/15');
    expect(input).toHaveValue('2026/01/15');
  });

  it('外面換了值，輸入框跟著顯示', async () => {
    // Arrange
    const user = userEvent.setup();
    function Outer() {
      const [value, setValue] = useState('2026-12-15');
      return (
        <>
          <DatePicker aria-label="開始日" value={value} onChange={setValue} />
          <button
            type="button"
            onClick={() => {
              setValue('2027-01-02');
            }}
          >
            換值
          </button>
        </>
      );
    }
    render(<Outer />);

    // Act
    await user.click(screen.getByRole('button', { name: '換值' }));

    // Assert
    expect(screen.getByRole('combobox', { name: '開始日' })).toHaveValue('2027/01/02');
  });
});

describe('DatePicker 的 onBlur', () => {
  it('焦點在輸入框與日曆之間移動不算離開；離開整個元件才叫一次', async () => {
    // Arrange
    const { user, input, onBlur } = renderPicker({ value: '2026-12-15' });
    input.focus();

    // Act
    await user.keyboard('{ArrowDown}');
    await findCalendar();
    await user.keyboard('{Escape}');
    const blurredInside = onBlur.mock.calls.length;
    await user.tab();

    // Assert
    expect(blurredInside).toBe(0);
    await waitFor(() => {
      expect(onBlur).toHaveBeenCalledOnce();
    });
  });
});

describe('DatePicker 的報讀與觸控', () => {
  it('打字時日曆跟著換月，標題不唸（不和打字的回音搶）；按鈕換月時才唸', async () => {
    // Arrange
    const { user, input } = renderPicker();
    await user.click(input);
    const calendar = await findCalendar();
    const live = calendar.querySelector('[aria-live="polite"]');

    // Act
    await user.type(input, '2027/3/5');
    const whileTyping = live?.textContent;
    await user.click(screen.getByRole('button', { name: '下一個月' }));

    // Assert
    expect(screen.getByRole('grid', { name: '2027 年 4 月' })).toBeInTheDocument();
    expect(whileTyping).toBe('');
    expect(live?.textContent).toBe('2027 年 4 月');
  });

  it('用手指點開時輸入框暫時不叫出螢幕鍵盤（inputMode="none"，鍵盤才不會蓋住日曆）；再點一次輸入框或關上日曆就恢復可以打字', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-12-15' });

    // Act
    await user.pointer({ keys: '[TouchA]', target: input });
    const opened = screen.queryByRole('dialog', { name: '選擇日期' }) !== null;
    const whileOpen = input.getAttribute('inputmode');
    await user.pointer({ keys: '[TouchA]', target: input });
    const afterSecondTap = input.getAttribute('inputmode');
    await user.keyboard('{Escape}');
    await user.pointer({ keys: '[TouchA]', target: input });
    const reopened = input.getAttribute('inputmode');
    await user.keyboard('{Escape}');

    // Assert
    expect(opened).toBe(true);
    expect(whileOpen).toBe('none');
    expect(afterSecondTap).toBeNull();
    expect(reopened).toBe('none');
    expect(input).not.toHaveAttribute('inputmode');
  });

  it('用滑鼠點開時照樣可以打字（不設 inputMode）', async () => {
    // Arrange
    const { user, input } = renderPicker();

    // Act
    await user.click(input);
    await findCalendar();

    // Assert
    expect(input).not.toHaveAttribute('inputmode');
  });
});
