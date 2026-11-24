import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { DatePicker } from './DatePicker';
import { cell, findCalendar, renderPicker, fixTodayForEachTest } from './datePickerTestUtils';

// DatePicker 的結構、無障礙的合約、打開與關上（其他主題：.grid、.views、.input 的測試檔）。
fixTodayForEachTest();

describe('DatePicker 的結構與無障礙的合約', () => {
  it('輸入框是 combobox：彈出的是對話框、關著時沒有日曆；值顯示成 2026/12/15', () => {
    // Arrange
    const { input } = renderPicker({ value: '2026-12-15' });

    // Act
    const calendar = screen.queryByRole('dialog');

    // Assert
    expect(input.tagName).toBe('INPUT');
    expect(input).toHaveAttribute('aria-haspopup', 'dialog');
    expect(input).toHaveAttribute('aria-expanded', 'false');
    expect(input).not.toHaveAttribute('aria-controls');
    expect(input).toHaveValue('2026/12/15');
    expect(calendar).not.toBeInTheDocument();
  });

  it('打開時：「選擇日期」對話框裡是月份為名的表格，表頭從星期一開始，六列 42 天；選的那一天 aria-selected、今天 aria-current', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-11-20' });

    // Act
    await user.click(input);
    const calendar = await findCalendar();
    const grid = within(calendar).getByRole('grid', { name: '2026 年 11 月' });

    // Assert
    expect(input).toHaveAttribute('aria-expanded', 'true');
    expect(input).toHaveAttribute('aria-controls', calendar.id);
    expect(within(grid).getAllByRole('columnheader').map((header) => header.getAttribute('aria-label'))).toEqual([
      '星期一',
      '星期二',
      '星期三',
      '星期四',
      '星期五',
      '星期六',
      '星期日',
    ]);
    expect(within(grid).getAllByRole('gridcell')).toHaveLength(42);
    expect(cell('2026 年 11 月 20 日 星期五')).toHaveAttribute('aria-selected', 'true');
    expect(cell('2026 年 11 月 17 日 星期二')).toHaveAttribute('aria-current', 'date');
    expect(within(grid).getAllByRole('gridcell').filter((day) => day.getAttribute('aria-selected') === 'true')).toHaveLength(1);
  });

  it('沒有值時打開在今天的月份、沒有選的那一天；前後補上別的月份，從週一開始到週日結束', async () => {
    // Arrange
    const { user, input } = renderPicker();

    // Act
    await user.click(input);
    const grid = within(await findCalendar()).getByRole('grid', { name: '2026 年 11 月' });
    const days = within(grid).getAllByRole('gridcell');

    // Assert
    expect(days[0]).toHaveAccessibleName('2026 年 10 月 26 日 星期一');
    expect(days[41]).toHaveAccessibleName('2026 年 12 月 6 日 星期日');
    expect(days.some((day) => day.getAttribute('aria-selected') === 'true')).toBe(false);
  });

  it('日曆不用 table 元素（只用角色）：放在表格的格子裡時不吃到外層表格的樣式，外層數 tr 也不會數到日曆的列', async () => {
    // Arrange
    const user = userEvent.setup();
    render(
      <table>
        <tbody>
          <tr>
            <td>
              <DatePicker aria-label="格子裡的到期日" variant="bare" value="2026-12-15" onChange={vi.fn()} />
            </td>
          </tr>
        </tbody>
      </table>,
    );

    // Act
    await user.click(screen.getByRole('combobox', { name: '格子裡的到期日' }));
    const calendar = await findCalendar();
    const grid = within(calendar).getByRole('grid', { name: '2026 年 12 月' });

    // Assert
    expect(calendar.querySelector('table, thead, tbody, tr, th, td')).toBeNull();
    expect(within(grid).getAllByRole('row')).toHaveLength(7);
    expect(within(grid).getAllByRole('columnheader')).toHaveLength(7);
    expect(within(grid).getAllByRole('gridcell')).toHaveLength(42);
    expect(document.querySelectorAll('tr')).toHaveLength(1);
  });

  it('日期格只有一格停得到（roving tabindex）：選的那一天，沒有值時是今天', async () => {
    // Arrange
    const { user, input } = renderPicker();

    // Act
    await user.click(input);
    const days = within(await findCalendar()).getAllByRole('gridcell');

    // Assert
    expect(days.filter((day) => day.tabIndex === 0).map((day) => day.getAttribute('aria-label'))).toEqual(['2026 年 11 月 17 日 星期二']);
  });
});

describe('DatePicker 的打開與關上', () => {
  it('用滑鼠點輸入框就打開，焦點留在輸入框（可以直接打字）', async () => {
    // Arrange
    const { user, input } = renderPicker();

    // Act
    await user.click(input);

    // Assert
    expect(await findCalendar()).toBeInTheDocument();
    expect(input).toHaveFocus();
  });

  it('用 Tab 走進來不打開', async () => {
    // Arrange
    const { user, input } = renderPicker();
    await user.click(screen.getByRole('button', { name: '前一個' }));

    // Act
    await user.tab();

    // Assert
    expect(input).toHaveFocus();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('↓ 打開並把焦點移到選的那一天；Alt+↓ 也是', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-12-15' });
    input.focus();

    // Act
    await user.keyboard('{ArrowDown}');
    const first = await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' });
    const firstHasFocus = document.activeElement === first;
    await user.keyboard('{Escape}');
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');

    // Assert
    expect(firstHasFocus).toBe(true);
    expect(await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' })).toHaveFocus();
  });

  it('沒有值時 ↓ 把焦點移到今天；已經開著（點了輸入框）時 ↓ 也是把焦點移進日曆', async () => {
    // Arrange
    const { user, input } = renderPicker();
    await user.click(input);
    await findCalendar();

    // Act
    await user.keyboard('{ArrowDown}');

    // Assert
    expect(cell('2026 年 11 月 17 日 星期二')).toHaveFocus();
  });

  it('輸入框裡按 Esc：關上、值不變、不往外傳（格子、對話框才不會跟著反應）', async () => {
    // Arrange
    const { user, input, onChange, onDismiss, onKeyDown, onOuterKeyDown } = renderPicker({ value: '2026-12-15' });
    await user.click(input);
    await findCalendar();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('escape');
    expect(onChange).not.toHaveBeenCalled();
    expect(onKeyDown).not.toHaveBeenCalled();
    expect(onOuterKeyDown).not.toHaveBeenCalled();
    expect(input).toHaveFocus();
  });

  it('關著時的 Esc 不處理：交給呼叫端的 onKeyDown 與外層（格子要靠它取消編輯）', async () => {
    // Arrange
    const { user, input, onKeyDown, onOuterKeyDown, onDismiss } = renderPicker();
    input.focus();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(onKeyDown).toHaveBeenCalledOnce();
    expect(onOuterKeyDown).toHaveBeenCalledOnce();
    expect(onDismiss).not.toHaveBeenCalled();
  });

  it('日曆裡按 Esc：關上、焦點回到輸入框、不往外傳', async () => {
    // Arrange
    const { user, input, onDismiss, onOuterKeyDown } = renderPicker({ value: '2026-12-15' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' });
    // ↓ 已經處理過（defaultPrevented，外層看得出來不必理）；這裡只看 Esc 有沒有往外傳。
    onOuterKeyDown.mockClear();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(input).toHaveFocus();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('escape');
    expect(onOuterKeyDown).not.toHaveBeenCalled();
  });

  it('點在外面：關上、值不變（outside）', async () => {
    // Arrange
    const { user, input, onChange, onDismiss } = renderPicker({ value: '2026-12-15' });
    await user.click(input);
    await findCalendar();

    // Act
    await user.click(screen.getByRole('button', { name: '前一個' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('outside');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('開著時在輸入框按 Tab：關上（tab），焦點照常到下一個', async () => {
    // Arrange
    const { user, input, onDismiss } = renderPicker();
    await user.click(input);
    await findCalendar();

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('tab');
    expect(screen.getByRole('button', { name: '下一個' })).toHaveFocus();
  });

  it('日曆裡 Tab 依序走過上一個月、標題（選年份與月份）、下一個月、日期格、「今天」「清除」；從「清除」再 Tab 是關上並往下走，onBlur 只叫一次', async () => {
    // Arrange
    const { user, input, onDismiss, onBlur } = renderPicker({ value: '2026-12-15' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' });
    await user.tab({ shift: true });
    await user.tab({ shift: true });
    await user.tab({ shift: true });
    const order: (string | null)[] = [document.activeElement?.getAttribute('aria-label') ?? null];

    // Act
    for (let step = 0; step < 5; step += 1) {
      await user.tab();
      order.push(document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.textContent ?? null);
    }
    await user.tab();

    // Assert
    expect(order).toEqual(['上一個月', '2026 年 12 月，選擇年份與月份', '下一個月', '2026 年 12 月 15 日 星期二', '今天', '清除']);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('tab');
    expect(screen.getByRole('button', { name: '下一個' })).toHaveFocus();
    expect(onBlur).toHaveBeenCalledOnce();
  });

  it('輸入框被捲出看得到的範圍就關上（scrolled），焦點在日曆裡的話回到輸入框', async () => {
    // Arrange
    const observer = stubIntersectionObserver();
    const { user, input, onDismiss } = renderPicker();
    input.focus();
    await user.keyboard('{ArrowDown}');
    await findCalendar();

    // Act
    observer.report(false);

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('scrolled');
    expect(input).toHaveFocus();
  });

  it('isOpenInitially：掛上就打開（格子用滑鼠進入編輯時），不搶焦點', async () => {
    // Arrange / Act
    renderPicker({ isOpenInitially: true, value: '2026-12-15' });

    // Assert
    expect(await findCalendar()).toBeInTheDocument();
    expect(screen.getByRole('grid', { name: '2026 年 12 月' })).toBeInTheDocument();
    expect(document.activeElement).toBe(document.body);
  });

  it('停用的打不開：點不了、也停不到', async () => {
    // Arrange
    const { user, input } = renderPicker({ disabled: true, isOpenInitially: true });

    // Act
    await user.click(input);

    // Assert
    expect(input).toBeDisabled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('唯讀的打不開：點了、按 ↓ 都沒有日曆', async () => {
    // Arrange
    const { user, input } = renderPicker({ readOnly: true, value: '2026-12-15' });

    // Act
    await user.click(input);
    await user.keyboard('{ArrowDown}');

    // Assert
    expect(input).toHaveAttribute('readonly');
    expect(input).toHaveValue('2026/12/15');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
