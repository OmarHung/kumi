import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, vi } from 'vitest';
import { DatePicker, type DatePickerProps } from './DatePicker';

/*
 * DatePicker 的測試共用的設定（測試照主題分成 DatePicker.test.tsx、DatePicker.grid.test.tsx、DatePicker.views.test.tsx、
 * DatePicker.input.test.tsx）。
 */

// 今天固定是 2026 年 11 月 17 日（星期二）：只換掉 Date，user-event 的計時照常。
const TODAY = new Date(2026, 10, 17, 10, 0, 0);

/** 每個測試檔最上面呼叫一次：每個測試前把今天固定住，測試後還原（也還原 mock 與換掉的全域）。 */
export function fixTodayForEachTest() {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(TODAY);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });
}

type Overrides = Partial<Omit<DatePickerProps, 'onChange'>>;

/** 受控的日期欄位：值變了就換（和真的呼叫端一樣），呼叫的順序記在 calls。前後各一顆按鈕，外面一層聽得到沒被攔下的按鍵。 */
export function renderPicker(overrides: Overrides = {}) {
  const user = userEvent.setup();
  const calls: string[] = [];
  const onChange = vi.fn((value: string) => {
    calls.push(`change:${value}`);
  });
  const onPick = vi.fn((value: string) => {
    calls.push(`pick:${value}`);
  });
  const onDismiss = vi.fn();
  const onBlur = vi.fn();
  const onKeyDown = vi.fn();
  const onOuterKeyDown = vi.fn();
  function Harness() {
    const [value, setValue] = useState(overrides.value ?? '');
    return (
      <DatePicker
        aria-label="到期日"
        onPick={onPick}
        onDismiss={onDismiss}
        onBlur={onBlur}
        onKeyDown={onKeyDown}
        {...overrides}
        value={value}
        onChange={(next) => {
          onChange(next);
          setValue(next);
        }}
      />
    );
  }
  render(
    <div onKeyDown={onOuterKeyDown}>
      <button type="button">前一個</button>
      <Harness />
      <button type="button">下一個</button>
    </div>,
  );
  const input = screen.getByRole('combobox', { name: '到期日' });
  return { user, input, calls, onChange, onPick, onDismiss, onBlur, onKeyDown, onOuterKeyDown };
}

export function findCalendar() {
  return screen.findByRole('dialog', { name: '選擇日期' });
}

export function cell(name: string) {
  return screen.getByRole('gridcell', { name });
}
