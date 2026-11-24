import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Dialog } from '../Dialog/Dialog';
import { DatePicker } from './DatePicker';
import { WEEK_START_STORAGE_KEY } from '../../lib/weekStart';
import { cell, findCalendar, renderPicker, fixTodayForEachTest } from './datePickerTestUtils';

// DatePicker 日期格的按鍵、滑鼠與焦點的去處。
fixTodayForEachTest();

describe('DatePicker 日曆裡的按鍵', () => {
  async function focusGridAt(value: string) {
    const rendered = renderPicker({ value });
    rendered.input.focus();
    await rendered.user.keyboard('{ArrowDown}');
    await findCalendar();
    return rendered;
  }

  it('←→ 一天、↑↓ 一週；走到別的月份時標題跟著換', async () => {
    // Arrange
    const { user } = await focusGridAt('2026-11-30');

    // Act
    await user.keyboard('{ArrowRight}');
    const tomorrow = document.activeElement?.getAttribute('aria-label');
    const monthAfterRight = screen.queryByRole('grid', { name: '2026 年 12 月' });
    await user.keyboard('{ArrowUp}{ArrowLeft}');
    const weekBefore = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{ArrowDown}{ArrowDown}');

    // Assert
    expect(tomorrow).toBe('2026 年 12 月 1 日 星期二');
    expect(monthAfterRight).not.toBeNull();
    expect(weekBefore).toBe('2026 年 11 月 23 日 星期一');
    expect(screen.getByRole('grid', { name: '2026 年 12 月' })).toBeInTheDocument();
    expect(cell('2026 年 12 月 7 日 星期一')).toHaveFocus();
  });

  it('Home、End 到這一週的週一、週日（一週從週一開始）', async () => {
    // Arrange
    const { user } = await focusGridAt('2026-11-17');

    // Act
    await user.keyboard('{Home}');
    const monday = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{End}');

    // Assert
    expect(monday).toBe('2026 年 11 月 16 日 星期一');
    expect(cell('2026 年 11 月 22 日 星期日')).toHaveFocus();
  });

  it('偏好是週日開始：表頭週日在最前面，Home、End 到這一週的週日、週六', async () => {
    // Arrange
    localStorage.setItem(WEEK_START_STORAGE_KEY, 'sunday');
    const { user } = await focusGridAt('2026-11-17');

    // Act
    const headers = within(screen.getByRole('grid', { name: '2026 年 11 月' }))
      .getAllByRole('columnheader')
      .map((header) => header.textContent);
    await user.keyboard('{Home}');
    const sunday = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{End}');

    // Assert
    expect(headers).toEqual(['日', '一', '二', '三', '四', '五', '六']);
    expect(sunday).toBe('2026 年 11 月 15 日 星期日');
    expect(cell('2026 年 11 月 21 日 星期六')).toHaveFocus();
  });

  it('PageUp、PageDown 一個月（日子超出就停在月底）；Shift 加上是一年', async () => {
    // Arrange
    const { user } = await focusGridAt('2026-01-31');

    // Act
    await user.keyboard('{PageDown}');
    const nextMonth = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{Shift>}{PageDown}{/Shift}');
    const nextYear = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{PageUp}{Shift>}{PageUp}{/Shift}');

    // Assert
    expect(nextMonth).toBe('2026 年 2 月 28 日 星期六');
    expect(nextYear).toBe('2027 年 2 月 28 日 星期日');
    expect(screen.getByRole('grid', { name: '2026 年 1 月' })).toBeInTheDocument();
    expect(cell('2026 年 1 月 28 日 星期三')).toHaveFocus();
  });

  it('只有焦點那一格停得到：移動之後 tabIndex 跟著走', async () => {
    // Arrange
    const { user } = await focusGridAt('2026-11-17');

    // Act
    await user.keyboard('{ArrowRight}');
    const stops = screen.getAllByRole('gridcell').filter((day) => day.tabIndex === 0);

    // Assert
    expect(stops.map((day) => day.getAttribute('aria-label'))).toEqual(['2026 年 11 月 18 日 星期三']);
  });

  it('Enter 選焦點那一天：先 onChange 再 onPick，關上，焦點回到輸入框', async () => {
    // Arrange
    const { user, input, calls, onDismiss } = await focusGridAt('2026-11-17');

    // Act
    await user.keyboard('{ArrowRight}{Enter}');

    // Assert
    expect(calls).toEqual(['change:2026-11-18', 'pick:2026-11-18']);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(input).toHaveFocus();
    expect(input).toHaveValue('2026/11/18');
    expect(onDismiss).not.toHaveBeenCalled();
  });

  it('空白鍵也是選；選了目前的值等於沒改：不呼叫 onChange，onPick 照樣呼叫', async () => {
    // Arrange
    const { user, calls } = await focusGridAt('2026-11-17');

    // Act
    await user.keyboard(' ');

    // Assert
    expect(calls).toEqual(['pick:2026-11-17']);
  });

  it('月份按鈕：上一個月、下一個月換標題；用鍵盤按時焦點留在按鈕上', async () => {
    // Arrange
    const { user } = await focusGridAt('2026-11-17');
    await user.tab({ shift: true });

    // Act
    await user.keyboard('{Enter}{Enter}');
    const afterTwoNext = screen.getAllByRole('grid').length === 1 && screen.queryByRole('grid', { name: '2027 年 1 月' }) !== null;
    await user.tab({ shift: true });
    await user.tab({ shift: true });
    await user.keyboard('{Enter}');

    // Assert
    expect(afterTwoNext).toBe(true);
    expect(screen.getByRole('button', { name: '上一個月' })).toHaveFocus();
    expect(screen.getByRole('grid', { name: '2026 年 12 月' })).toBeInTheDocument();
  });
});

describe('DatePicker 的滑鼠', () => {
  it('點日期：onChange、onPick，關上，焦點回到輸入框', async () => {
    // Arrange
    const { user, input, calls } = renderPicker();
    await user.click(input);
    await findCalendar();

    // Act
    await user.click(cell('2026 年 11 月 20 日 星期五'));

    // Assert
    expect(calls).toEqual(['change:2026-11-20', 'pick:2026-11-20']);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(input).toHaveFocus();
  });

  it('點月份按鈕換月份，焦點留在輸入框（照樣可以打字）', async () => {
    // Arrange
    const { user, input } = renderPicker();
    await user.click(input);
    await findCalendar();

    // Act
    await user.click(screen.getByRole('button', { name: '上一個月' }));

    // Assert
    expect(screen.getByRole('grid', { name: '2026 年 10 月' })).toBeInTheDocument();
    expect(input).toHaveFocus();
  });

  it('「今天」選今天、「清除」清成沒有值：都是明確的選擇（onPick）', async () => {
    // Arrange
    const { user, input, calls } = renderPicker({ value: '2026-12-15' });
    await user.click(input);

    // Act
    await user.click(within(await findCalendar()).getByRole('button', { name: '今天' }));
    await user.click(input);
    await user.click(within(await findCalendar()).getByRole('button', { name: '清除' }));

    // Assert
    expect(calls).toEqual(['change:2026-11-17', 'pick:2026-11-17', 'change:', 'pick:']);
    expect(input).toHaveValue('');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('送出中（aria-busy）：打得開，但選了不收新的值', async () => {
    // Arrange
    const { user, input, onChange, onPick } = renderPicker({ 'aria-busy': true, value: '2026-12-15' });
    await user.click(input);
    await findCalendar();

    // Act
    await user.click(cell('2026 年 12 月 20 日 星期日'));

    // Assert
    expect(input).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
    expect(onPick).not.toHaveBeenCalled();
  });
});

describe('DatePicker 的焦點去處（審查發現的兩個情境）', () => {
  it('對話框裡：焦點在日期格時按對話框的背景 → 日曆關上，焦點回到輸入框（不掉到頁面本體，焦點陷阱照樣有效）', async () => {
    // Arrange
    const user = userEvent.setup();
    function InDialog() {
      const [value, setValue] = useState('2026-12-15');
      return (
        <Dialog title="補填" onClose={vi.fn()}>
          <button type="button">第一個</button>
          <DatePicker aria-label="到期日" value={value} onChange={setValue} />
          <button type="button">送出</button>
        </Dialog>
      );
    }
    render(<InDialog />);
    const input = screen.getByRole('combobox', { name: '到期日' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' });
    const backdrop = screen.getByRole('dialog', { name: '補填' }).parentElement;

    // Act
    await user.pointer({ keys: '[MouseLeft]', target: backdrop ?? document.body });

    // Assert
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
    expect(input).toHaveFocus();
  });

  it('焦點在日期格時點外面另一個停得到的元素：焦點到那個元素（不被搶回輸入框），onBlur 叫一次', async () => {
    // Arrange
    const { user, onBlur, onDismiss } = renderPicker({ value: '2026-12-15' });
    screen.getByRole('combobox', { name: '到期日' }).focus();
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' });

    // Act
    await user.click(screen.getByRole('button', { name: '下一個' }));

    // Assert
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '下一個' })).toHaveFocus();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('outside');
    expect(onBlur).toHaveBeenCalledOnce();
  });

  it('焦點在日期格時程式把焦點移到外面（報讀軟體跳走、程式的 focus()）：日曆關上，焦點留在那裡、不被搬回輸入框', async () => {
    // Arrange
    const { user, onDismiss } = renderPicker({ value: '2026-12-15' });
    screen.getByRole('combobox', { name: '到期日' }).focus();
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' });
    const outside = screen.getByRole('button', { name: '前一個' });

    // Act
    act(() => {
      outside.focus();
    });

    // Assert
    expect(outside).toHaveFocus();
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('outside');
  });

  it('焦點在日期格時用滑鼠點「下一個月」：焦點移到下個月的同一天（原本那一格不見了，焦點不掉到頁面本體），之後 Esc 照常關上日曆', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-12-15' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('gridcell', { name: '2026 年 12 月 15 日 星期二' });

    // Act
    await user.click(screen.getByRole('button', { name: '下一個月' }));
    const moved = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{Escape}');

    // Assert
    expect(moved).toBe('2027 年 1 月 15 日 星期五');
    expect(screen.queryByRole('dialog', { name: '選擇日期' })).not.toBeInTheDocument();
    expect(input).toHaveFocus();
  });

  it('焦點在月份格時用滑鼠點「下一年」：焦點移到下一年的同一個月', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-12-15' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await findCalendar();
    await user.tab({ shift: true });
    await user.tab({ shift: true });
    await user.keyboard('{Enter}');
    await screen.findByRole('grid', { name: '2026 年' });

    // Act
    await user.click(screen.getByRole('button', { name: '下一年' }));

    // Assert
    expect(within(screen.getByRole('grid', { name: '2027 年' })).getByRole('gridcell', { name: '2027 年 12 月' })).toHaveFocus();
  });
});
