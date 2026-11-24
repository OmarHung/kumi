import { screen, within } from '@testing-library/react';
import type userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { cell, findCalendar, renderPicker, fixTodayForEachTest } from './datePickerTestUtils';

// DatePicker 的年份與月份畫面。
fixTodayForEachTest();

describe('DatePicker 的年份與月份', () => {
  async function openWithMouse(value: string) {
    const rendered = renderPicker({ value });
    await rendered.user.click(rendered.input);
    await findCalendar();
    return rendered;
  }

  /** 從日期格往回走到標題按鈕（上一個月｜標題｜下一個月｜日期格）。 */
  async function focusTitleFromGrid(user: ReturnType<typeof userEvent.setup>) {
    await user.tab({ shift: true });
    await user.tab({ shift: true });
  }

  it('日期畫面的標題是按鈕（看得到的字在名稱開頭）；點了換到月份畫面：「2026 年」裡的 12 個月，選的月實心、今天所在的月 aria-current', async () => {
    // Arrange
    const { user, input, onChange, onPick } = await openWithMouse('2026-12-15');

    // Act
    await user.click(screen.getByRole('button', { name: '2026 年 12 月，選擇年份與月份' }));
    const grid = await screen.findByRole('grid', { name: '2026 年' });
    const months = within(grid).getAllByRole('gridcell');

    // Assert
    expect(months.map((month) => month.getAttribute('aria-label'))).toEqual(Array.from({ length: 12 }, (_, index) => `2026 年 ${String(index + 1)} 月`));
    expect(within(grid).getAllByRole('row')).toHaveLength(4);
    expect(within(grid).getByRole('gridcell', { name: '2026 年 12 月' })).toHaveAttribute('aria-selected', 'true');
    expect(within(grid).getByRole('gridcell', { name: '2026 年 11 月' })).toHaveAttribute('aria-current', 'date');
    expect(screen.getByRole('button', { name: '上一年' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '下一年' })).toBeInTheDocument();
    expect(input).toHaveFocus();
    expect(onChange).not.toHaveBeenCalled();
    expect(onPick).not.toHaveBeenCalled();
  });

  it('月份畫面：下一年、點月份 → 回到日期畫面顯示那個月；換月份不算選了（值不變、日曆開著）', async () => {
    // Arrange
    const { user, input, onChange, onPick } = await openWithMouse('2026-12-15');
    await user.click(screen.getByRole('button', { name: '2026 年 12 月，選擇年份與月份' }));
    await screen.findByRole('grid', { name: '2026 年' });

    // Act
    await user.click(screen.getByRole('button', { name: '下一年' }));
    await user.click(within(screen.getByRole('grid', { name: '2027 年' })).getByRole('gridcell', { name: '2027 年 3 月' }));

    // Assert
    expect(await screen.findByRole('grid', { name: '2027 年 3 月' })).toBeInTheDocument();
    expect(cell('2027 年 3 月 15 日 星期一')).toHaveAttribute('tabindex', '0');
    expect(onChange).not.toHaveBeenCalled();
    expect(onPick).not.toHaveBeenCalled();
    expect(input).toHaveFocus();
    expect(input).toHaveValue('2026/12/15');
  });

  it('年份畫面：一頁 12 年（含目前的年份），標題是範圍（不是按鈕）；前 12 年、後 12 年；點年份 → 回到月份畫面', async () => {
    // Arrange
    const { user } = await openWithMouse('2026-12-15');
    await user.click(screen.getByRole('button', { name: '2026 年 12 月，選擇年份與月份' }));
    await user.click(await screen.findByRole('button', { name: '2026 年，選擇年份' }));

    // Act
    const grid = await screen.findByRole('grid', { name: '2024～2035 年' });
    const years = within(grid).getAllByRole('gridcell').map((year) => year.getAttribute('aria-label'));
    const selected = within(grid).getByRole('gridcell', { name: '2026 年' });
    const hasPreviousPage = screen.queryByRole('button', { name: '前 12 年' }) !== null;
    const titleIsButton = screen.queryByRole('button', { name: /2024～2035/ }) !== null;
    const titleShown = screen.queryAllByText('2024～2035').length === 1;
    await user.click(screen.getByRole('button', { name: '後 12 年' }));
    await user.click(within(screen.getByRole('grid', { name: '2036～2047 年' })).getByRole('gridcell', { name: '2040 年' }));

    // Assert
    expect(years[0]).toBe('2024 年');
    expect(years[11]).toBe('2035 年');
    expect(selected).toHaveAttribute('aria-selected', 'true');
    expect(selected).toHaveAttribute('aria-current', 'date');
    expect(hasPreviousPage).toBe(true);
    expect(titleIsButton).toBe(false);
    expect(titleShown).toBe(true);
    expect(await screen.findByRole('grid', { name: '2040 年' })).toBeInTheDocument();
  });

  it('鍵盤：標題按 Enter 換到月份畫面、焦點到選的那個月；←→ 一個月、↑↓ 三個月、Home／End 這一列頭尾、PageDown 一年；Enter 回到日期畫面、焦點在同一天', async () => {
    // Arrange
    const { user, input, onChange } = renderPicker({ value: '2026-12-15' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await findCalendar();
    await focusTitleFromGrid(user);

    // Act
    await user.keyboard('{Enter}');
    const entered = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{ArrowRight}');
    const right = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{ArrowUp}');
    const up = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{End}');
    const end = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{Home}');
    const home = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{End}{PageDown}{Enter}');

    // Assert
    expect([entered, right, up, end, home]).toEqual(['2026 年 12 月', '2027 年 1 月', '2026 年 10 月', '2026 年 12 月', '2026 年 10 月']);
    expect(await screen.findByRole('grid', { name: '2027 年 12 月' })).toBeInTheDocument();
    expect(cell('2027 年 12 月 15 日 星期三')).toHaveFocus();
    expect(onChange).not.toHaveBeenCalled();
  });

  it('鍵盤：年份畫面 ←→ 一年、↓ 三年、PageDown 12 年（換一頁）；Enter 回到月份畫面、焦點在同一個月', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-12-15' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await findCalendar();
    await focusTitleFromGrid(user);
    await user.keyboard('{Enter}');
    await screen.findByRole('grid', { name: '2026 年' });
    await focusTitleFromGrid(user);

    // Act
    await user.keyboard('{Enter}');
    const entered = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{ArrowRight}{ArrowDown}');
    const moved = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{PageDown}');
    const paged = screen.queryByRole('grid', { name: '2036～2047 年' });
    await user.keyboard('{Enter}');

    // Assert
    expect(entered).toBe('2026 年');
    expect(moved).toBe('2030 年');
    expect(paged).not.toBeNull();
    expect(await screen.findByRole('grid', { name: '2042 年' })).toBeInTheDocument();
    expect(within(screen.getByRole('grid', { name: '2042 年' })).getByRole('gridcell', { name: '2042 年 12 月' })).toHaveFocus();
  });

  it('Esc 在年份、月份畫面是回到上一層畫面（焦點到對應的那一格），在日期畫面才關上日曆', async () => {
    // Arrange
    const { user, input, onDismiss } = renderPicker({ value: '2026-12-15' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await findCalendar();
    await focusTitleFromGrid(user);
    await user.keyboard('{Enter}');
    await focusTitleFromGrid(user);
    await user.keyboard('{Enter}');
    await screen.findByRole('grid', { name: '2024～2035 年' });

    // Act
    await user.keyboard('{Escape}');
    const fromYears = document.activeElement?.getAttribute('aria-label');
    await user.keyboard('{Escape}');
    const fromMonths = document.activeElement?.getAttribute('aria-label');
    const stillOpen = screen.queryByRole('dialog') !== null;
    await user.keyboard('{Escape}');

    // Assert
    expect(fromYears).toBe('2026 年 12 月');
    expect(fromMonths).toBe('2026 年 12 月 15 日 星期二');
    expect(stillOpen).toBe(true);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('escape');
    expect(input).toHaveFocus();
  });

  it('從月份畫面選了月份：1 月 31 日 → 2 月是 28 日（夾到月底）；從 2 月 28 日再進月份畫面選 3 月是 28 日（記的是進月份畫面時的那一天）', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-01-31' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await findCalendar();
    await focusTitleFromGrid(user);
    await user.keyboard('{Enter}');

    // Act
    await user.keyboard('{ArrowRight}{Enter}');
    const february = document.activeElement?.getAttribute('aria-label');
    await focusTitleFromGrid(user);
    await user.keyboard('{Enter}{ArrowRight}{Enter}');

    // Assert
    expect(february).toBe('2026 年 2 月 28 日 星期六');
    expect(document.activeElement?.getAttribute('aria-label')).toBe('2026 年 3 月 28 日 星期六');
  });

  it('從月份畫面連續走兩個月：1 月 31 日 → 3 月是 31 日（不會因為經過 2 月變成 28 日）', async () => {
    // Arrange
    const { user, input } = renderPicker({ value: '2026-01-31' });
    input.focus();
    await user.keyboard('{ArrowDown}');
    await findCalendar();
    await focusTitleFromGrid(user);
    await user.keyboard('{Enter}');

    // Act
    await user.keyboard('{ArrowRight}{ArrowRight}{Enter}');

    // Assert
    expect(cell('2026 年 3 月 31 日 星期二')).toHaveFocus();
  });

  it('打開日曆一律從日期畫面開始', async () => {
    // Arrange
    const { user, input } = await openWithMouse('2026-12-15');
    await user.click(screen.getByRole('button', { name: '2026 年 12 月，選擇年份與月份' }));
    await screen.findByRole('grid', { name: '2026 年' });
    await user.click(screen.getByRole('button', { name: '前一個' }));

    // Act
    await user.click(input);

    // Assert
    expect(await screen.findByRole('grid', { name: '2026 年 12 月' })).toBeInTheDocument();
  });

  it('月份、年份畫面裡「今天」照樣是選了今天（關上、onPick）', async () => {
    // Arrange
    const { user, calls } = await openWithMouse('2026-12-15');
    await user.click(screen.getByRole('button', { name: '2026 年 12 月，選擇年份與月份' }));

    // Act
    await user.click(within(await findCalendar()).getByRole('button', { name: '今天' }));

    // Assert
    expect(calls).toEqual(['change:2026-11-17', 'pick:2026-11-17']);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
