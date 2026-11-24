import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef, useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { chooseOption, openOptions } from '../../test/select';
import { Select, type SelectOption, type SelectProps } from './Select';

const STATES: readonly SelectOption[] = [
  { value: 'backlog', label: '待辦' },
  { value: 'doing', label: '進行中' },
  { value: 'review', label: '審查', disabledReason: '這個流程沒有這條路' },
  { value: 'done', label: '完成', startsGroup: true },
];

const FRUITS: readonly SelectOption[] = [
  { value: 'apple', label: 'Apple' },
  { value: 'banana', label: 'Banana' },
  { value: 'blueberry', label: 'Blueberry' },
  { value: 'cherry', label: 'Cherry' },
];

const MANY: readonly SelectOption[] = Array.from({ length: 25 }, (_, index) => ({
  value: `option-${String(index + 1)}`,
  label: `選項 ${String(index + 1)}`,
}));

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

type Overrides = Partial<Omit<SelectProps, 'onChange'>>;

/** 受控的下拉：選了就換值（和真的呼叫端一樣），呼叫的順序記在 calls。外面一層聽得到沒被攔下的按鍵。 */
function renderSelect(overrides: Overrides = {}) {
  const user = userEvent.setup();
  const calls: string[] = [];
  const onChange = vi.fn((value: string) => {
    calls.push(`change:${value}`);
  });
  const onPick = vi.fn((value: string) => {
    calls.push(`pick:${value}`);
  });
  const onDismiss = vi.fn();
  const onOuterKeyDown = vi.fn();
  function Harness() {
    const [value, setValue] = useState(overrides.value ?? 'doing');
    return (
      <Select
        aria-label="狀態"
        options={STATES}
        onPick={onPick}
        onDismiss={onDismiss}
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
  const select = screen.getByRole('combobox', { name: '狀態' });
  return { user, select, calls, onChange, onPick, onDismiss, onOuterKeyDown };
}

/** 反白的選項：按鈕的 aria-activedescendant 指到它（焦點一直在按鈕上）。 */
function activeOption(select: HTMLElement): HTMLElement | null {
  const id = select.getAttribute('aria-activedescendant');
  return id === null ? null : document.getElementById(id);
}

describe('Select 的結構與無障礙的合約', () => {
  it('按鈕是 combobox：名稱另外給、值是按鈕裡的文字；關著時沒有清單', () => {
    // Arrange
    const { select } = renderSelect();

    // Act
    const listbox = screen.queryByRole('listbox');

    // Assert
    expect(select.tagName).toBe('BUTTON');
    expect(select).toHaveAttribute('type', 'button');
    expect(select).toHaveAttribute('aria-haspopup', 'listbox');
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(select).not.toHaveAttribute('aria-controls');
    expect(select).toHaveTextContent('進行中');
    expect(listbox).not.toBeInTheDocument();
  });

  it('打開時清單和按鈕同名，選項帶 aria-selected 與 data-value，反白在目前的值', async () => {
    // Arrange
    const { user, select } = renderSelect();

    // Act
    const options = await openOptions(user, select);

    // Assert
    const listbox = await screen.findByRole('listbox', { name: '狀態' });
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(select).toHaveAttribute('aria-controls', listbox.id);
    expect(options.map((option) => option.dataset.value)).toEqual(['backlog', 'doing', 'review', 'done']);
    expect(within(listbox).getByRole('option', { name: '進行中' })).toHaveAttribute('aria-selected', 'true');
    expect(within(listbox).getByRole('option', { name: '待辦' })).toHaveAttribute('aria-selected', 'false');
    expect(activeOption(select)).toBe(within(listbox).getByRole('option', { name: '進行中' }));
    expect(select).toHaveFocus();
  });

  it('不能選的選項：aria-disabled、原因連過去；點了沒有反應，清單留著', async () => {
    // Arrange
    const { user, select, onChange, onPick } = renderSelect();
    await openOptions(user, select);
    const blocked = await screen.findByRole('option', { name: '審查' });

    // Act
    await user.click(blocked);

    // Assert
    expect(blocked).toHaveAttribute('aria-disabled', 'true');
    expect(blocked).toHaveAccessibleDescription('這個流程沒有這條路');
    expect(onChange).not.toHaveBeenCalled();
    expect(onPick).not.toHaveBeenCalled();
    expect(screen.getByRole('listbox')).toBeInTheDocument();
  });

  it('名稱來自 aria-labelledby 時，清單用同一個', async () => {
    // Arrange
    const user = userEvent.setup();
    render(
      <>
        <span id="state-label">狀態</span>
        <Select aria-labelledby="state-label" value="doing" options={STATES} onChange={vi.fn()} />
      </>,
    );
    const select = screen.getByRole('combobox', { name: '狀態' });

    // Act
    await openOptions(user, select);

    // Assert
    expect(await screen.findByRole('listbox', { name: '狀態' })).toHaveAttribute('aria-labelledby', 'state-label');
  });

  it('值對不到選項時顯示 placeholder；沒有選項時清單裡說沒有可以選的', async () => {
    // Arrange
    const { user, select } = renderSelect({ value: '', options: [], placeholder: '選擇人員' });

    // Act
    const options = await openOptions(user, select);

    // Assert
    expect(select).toHaveTextContent('選擇人員');
    expect(options).toEqual([]);
    expect(await screen.findByText('沒有可以選的')).toBeInTheDocument();
    // 說明是清單裡一列不能選的「選項」，反白指著它：報讀軟體打開時聽得到，不是一個空的清單（openOptions 只回真的選項）。
    const listbox = await screen.findByRole('listbox', { name: '狀態' });
    const notice = within(listbox).getByRole('option', { name: '沒有可以選的' });
    expect(notice).toHaveAttribute('aria-disabled', 'true');
    expect(activeOption(select)).toBe(notice);
  });

  it('名稱至少要給一個（aria-label 或 aria-labelledby）：沒有名稱的 combobox 過不了型別檢查', () => {
    // Arrange / Act
    // @ts-expect-error 沒有 aria-label 也沒有 aria-labelledby：combobox 的名稱不能來自內容，一定要另外給。
    const element = <Select value="" options={[]} onChange={vi.fn()} />;

    // Assert
    expect(element.props).not.toHaveProperty('aria-label');
  });

  it('width="content"：照目前的值的寬度（工具列、設定頁的列上），標在 data-width；預設撐滿', () => {
    // Arrange
    render(
      <>
        <Select aria-label="工具列的狀態" width="content" value="" options={[]} onChange={vi.fn()} />
        <Select aria-label="表單的狀態" value="" options={[]} onChange={vi.fn()} />
      </>,
    );

    // Act
    const inline = screen.getByRole('combobox', { name: '工具列的狀態' });
    const form = screen.getByRole('combobox', { name: '表單的狀態' });

    // Assert
    expect(inline).toHaveAttribute('data-width', 'content');
    expect(form).toHaveAttribute('data-width', 'fill');
  });

  it('variant="bare"：格子裡用，標在 data-variant（外框由格子畫）；預設是 default；名稱與值照舊', () => {
    // Arrange
    render(
      <>
        <Select aria-label="格子裡的狀態" variant="bare" value="doing" options={STATES} onChange={vi.fn()} />
        <Select aria-label="表單的狀態" value="doing" options={STATES} onChange={vi.fn()} />
      </>,
    );

    // Act
    const bare = screen.getByRole('combobox', { name: '格子裡的狀態' });
    const form = screen.getByRole('combobox', { name: '表單的狀態' });

    // Assert
    expect(bare).toHaveAttribute('data-variant', 'bare');
    expect(bare).toHaveTextContent('進行中');
    expect(form).toHaveAttribute('data-variant', 'default');
  });

  it('variant="bare" 的清單照常浮出來：選項、反白與選了的行為不變', async () => {
    // Arrange
    const { user, select, onChange } = renderSelect({ variant: 'bare' });

    // Act
    const options = await openOptions(user, select);
    await user.click(await screen.findByRole('option', { name: '完成' }));

    // Assert
    expect(options).toHaveLength(4);
    expect(onChange).toHaveBeenCalledExactlyOnceWith('done');
    expect(select).toHaveAttribute('data-variant', 'bare');
  });

  it('選項前面的小圖是裝飾：清單與按鈕上都畫，不算在名稱裡', async () => {
    // Arrange
    const options: SelectOption[] = [
      { value: 'a', label: '範例使用者', leading: <span data-testid="avatar-a">範</span> },
      { value: 'b', label: '範例成員', leading: <span data-testid="avatar-b">範</span> },
    ];
    const { user, select } = renderSelect({ value: 'a', options });

    // Act
    await openOptions(user, select);

    // Assert
    const option = await screen.findByRole('option', { name: '範例成員' });
    expect(within(option).getByTestId('avatar-b').parentElement).toHaveAttribute('aria-hidden', 'true');
    expect(within(select).getByTestId('avatar-a').parentElement).toHaveAttribute('aria-hidden', 'true');
  });

  it('id、className、ref 都在按鈕上；aria-invalid、aria-describedby 照給的', () => {
    // Arrange
    const ref = createRef<HTMLButtonElement>();
    render(
      <>
        <Select
          ref={ref}
          id="assignee"
          className="toolbar-select"
          aria-label="負責人"
          aria-invalid
          aria-describedby="assignee-error"
          value=""
          options={[]}
          onChange={vi.fn()}
        />
        <p id="assignee-error">先選一個人。</p>
      </>,
    );

    // Act
    const select = screen.getByRole('combobox', { name: '負責人' });

    // Assert
    expect(ref.current).toBe(select);
    expect(select).toHaveAttribute('id', 'assignee');
    expect(select).toHaveClass('toolbar-select');
    expect(select).toHaveAttribute('aria-invalid', 'true');
    expect(select).toHaveAccessibleDescription('先選一個人。');
  });
});

describe('Select 關著時的按鍵', () => {
  it.each([['{Enter}'], [' '], ['{ArrowDown}'], ['{ArrowUp}'], ['{Alt>}{ArrowDown}{/Alt}']])('%s 打開，反白在目前的值，不換值', async (keys) => {
    // Arrange
    const { user, select, onChange } = renderSelect();
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard(keys);

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(activeOption(select)).toHaveTextContent('進行中');
    expect(onChange).not.toHaveBeenCalled();
    expect(select).toHaveFocus();
  });

  it('Home、End 打開，反白在第一個、最後一個', async () => {
    // Arrange
    const { user, select } = renderSelect();
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('{Home}');
    const first = activeOption(select)?.textContent;
    await user.keyboard('{Escape}{End}');

    // Assert
    expect(first).toBe('待辦');
    expect(activeOption(select)).toHaveTextContent('完成');
  });

  it('打字打開，反白跳到開頭符合的選項，值不變', async () => {
    // Arrange
    const { user, select, onChange } = renderSelect({ value: 'apple', options: FRUITS });
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('c');

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(activeOption(select)).toHaveTextContent('Cherry');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('Esc 不處理：外層照常收到（格子要靠它取消編輯），也交給 onKeyDown', async () => {
    // Arrange
    const onKeyDown = vi.fn();
    const { user, select, onOuterKeyDown, onDismiss } = renderSelect({ onKeyDown });
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(onKeyDown).toHaveBeenCalledWith(expect.objectContaining({ key: 'Escape' }));
    expect(onOuterKeyDown).toHaveBeenCalledWith(expect.objectContaining({ key: 'Escape' }));
    expect(onDismiss).not.toHaveBeenCalled();
  });

  it('Tab 照常往下走', async () => {
    // Arrange
    const { user, select } = renderSelect();
    act(() => {
      select.focus();
    });

    // Act
    await user.tab();

    // Assert
    expect(screen.getByRole('button', { name: '下一個' })).toHaveFocus();
  });
});

describe('Select 開著時的按鍵', () => {
  it('↓、↑ 只移動反白，到頭尾就停、不繞回去；不能選的也停得到；值不變', async () => {
    // Arrange
    const { user, select, onChange } = renderSelect();
    await openOptions(user, select);

    // Act
    await user.keyboard('{ArrowDown}');
    const onBlocked = activeOption(select)?.dataset.value;
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}');
    const atEnd = activeOption(select)?.dataset.value;
    await user.keyboard('{ArrowUp}{ArrowUp}{ArrowUp}{ArrowUp}{ArrowUp}');

    // Assert
    expect(onBlocked).toBe('review');
    expect(atEnd).toBe('done');
    expect(activeOption(select)?.dataset.value).toBe('backlog');
    expect(onChange).not.toHaveBeenCalled();
    expect(select).toHaveTextContent('進行中');
  });

  it('Home、End 到第一個、最後一個', async () => {
    // Arrange
    const { user, select } = renderSelect();
    await openOptions(user, select);

    // Act
    await user.keyboard('{End}');
    const atEnd = activeOption(select)?.dataset.value;
    await user.keyboard('{Home}');

    // Assert
    expect(atEnd).toBe('done');
    expect(activeOption(select)?.dataset.value).toBe('backlog');
  });

  it('PageUp、PageDown 一次 10 個，到頭尾就停（選項多時焦點在篩選框，反白照樣用 aria-activedescendant）', async () => {
    // Arrange
    const { user, select } = renderSelect({ value: 'option-1', options: MANY });
    await openOptions(user, select);
    const filter = await screen.findByRole('combobox', { name: '篩選 狀態' });

    // Act
    await user.keyboard('{PageDown}');
    const afterPageDown = activeOption(filter)?.dataset.value;
    await user.keyboard('{PageDown}{PageDown}');
    const atEnd = activeOption(filter)?.dataset.value;
    await user.keyboard('{PageUp}');

    // Assert
    expect(filter).toHaveFocus();
    expect(afterPageDown).toBe('option-11');
    expect(atEnd).toBe('option-25');
    expect(activeOption(filter)?.dataset.value).toBe('option-15');
  });

  it('Enter 選反白的那個並關上：先 onChange 再 onPick，焦點留在按鈕', async () => {
    // Arrange
    const { user, select, calls } = renderSelect();
    await openOptions(user, select);

    // Act
    await user.keyboard('{ArrowUp}{Enter}');

    // Assert
    expect(calls).toEqual(['change:backlog', 'pick:backlog']);
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(select).toHaveTextContent('待辦');
    expect(select).toHaveFocus();
  });

  it('空白鍵也是選；選了目前的值等於沒改：不呼叫 onChange，onPick 照樣呼叫', async () => {
    // Arrange
    const { user, select, calls } = renderSelect();
    await openOptions(user, select);

    // Act
    await user.keyboard(' ');

    // Assert
    expect(calls).toEqual(['pick:doing']);
    expect(select).toHaveAttribute('aria-expanded', 'false');
  });

  it('在不能選的選項上按 Enter 不選，清單留著', async () => {
    // Arrange
    const { user, select, onPick } = renderSelect();
    await openOptions(user, select);

    // Act
    await user.keyboard('{ArrowDown}{Enter}');

    // Assert
    expect(onPick).not.toHaveBeenCalled();
    expect(select).toHaveAttribute('aria-expanded', 'true');
  });

  it('打字跳到開頭符合的選項：500ms 內連續打的字算同一串，同一個字重複打是輪流', async () => {
    // Arrange
    const now = vi.spyOn(Date, 'now').mockReturnValue(1_000);
    const { user, select } = renderSelect({ value: 'apple', options: FRUITS });
    await openOptions(user, select);

    // Act
    await user.keyboard('b');
    const first = activeOption(select)?.dataset.value;
    await user.keyboard('l');
    const continued = activeOption(select)?.dataset.value;
    now.mockReturnValue(2_000);
    await user.keyboard('b');
    const restarted = activeOption(select)?.dataset.value;
    await user.keyboard('b');

    // Assert
    expect(first).toBe('banana');
    expect(continued).toBe('blueberry');
    expect(restarted).toBe('banana');
    expect(activeOption(select)?.dataset.value).toBe('blueberry');
  });

  it('Esc 關上、值不變，不往外傳（對話框、格子、面板才不會跟著反應）', async () => {
    // Arrange
    const onKeyDown = vi.fn();
    const { user, select, onChange, onDismiss, onOuterKeyDown } = renderSelect({ onKeyDown });
    await openOptions(user, select);
    await user.keyboard('{ArrowUp}');

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(select).toHaveTextContent('進行中');
    expect(onChange).not.toHaveBeenCalled();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('escape');
    expect(onOuterKeyDown).not.toHaveBeenCalledWith(expect.objectContaining({ key: 'Escape' }));
    expect(onKeyDown).not.toHaveBeenCalled();
    expect(select).toHaveFocus();
  });

  it('Tab 關上、值不變、照常往下走；onDismiss 只叫一次（焦點離開不會再叫）', async () => {
    // Arrange
    const { user, select, onChange, onDismiss } = renderSelect();
    await openOptions(user, select);
    await user.keyboard('{ArrowUp}');

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('tab');
    expect(screen.getByRole('button', { name: '下一個' })).toHaveFocus();
  });

  it('輸入法選字中的按鍵都不算', async () => {
    // Arrange
    const { user, select, onPick } = renderSelect();
    await openOptions(user, select);

    // Act
    fireEvent.keyDown(select, { key: 'ArrowDown', isComposing: true });
    fireEvent.keyDown(select, { key: 'Enter', keyCode: 229 });
    fireEvent.keyDown(select, { key: 'Escape', isComposing: true });

    // Assert
    expect(activeOption(select)?.dataset.value).toBe('doing');
    expect(onPick).not.toHaveBeenCalled();
    expect(select).toHaveAttribute('aria-expanded', 'true');
  });

  it('輸入法選字中的按鍵在關著時也不會打開清單', () => {
    // Arrange
    const { select } = renderSelect();

    // Act
    fireEvent.keyDown(select, { key: 'Enter', keyCode: 229 });

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'false');
  });
});

describe('Select 的滑鼠與觸控', () => {
  it('點按鈕打開，再按一次關上（onDismiss 是 toggle）', async () => {
    // Arrange
    const { user, select, onDismiss } = renderSelect();

    // Act
    await user.click(select);
    const opened = select.getAttribute('aria-expanded');
    await user.click(select);

    // Assert
    expect(opened).toBe('true');
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('toggle');
  });

  it('點選項就選、關上；焦點留在按鈕', async () => {
    // Arrange
    const { user, select, calls } = renderSelect();

    // Act
    await chooseOption(user, select, '完成');

    // Assert
    expect(calls).toEqual(['change:done', 'pick:done']);
    expect(select).toHaveTextContent('完成');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(select).toHaveFocus();
  });

  it('chooseOption 也收 value', async () => {
    // Arrange
    const { user, select, onChange } = renderSelect();

    // Act
    await chooseOption(user, select, 'backlog');

    // Assert
    expect(onChange).toHaveBeenCalledWith('backlog');
  });

  it('在清單上按下滑鼠不會把焦點從按鈕帶走', async () => {
    // Arrange
    const { user, select } = renderSelect();
    await openOptions(user, select);
    const listbox = await screen.findByRole('listbox');

    // Act
    const isDefaultKept = fireEvent.pointerDown(listbox);
    const isMouseDefaultKept = fireEvent.mouseDown(listbox);

    // Assert
    expect(isDefaultKept).toBe(false);
    expect(isMouseDefaultKept).toBe(false);
    expect(select).toHaveFocus();
  });

  it('在清單的外框（Floating 的內距與外框）上按下滑鼠：焦點留在按鈕上，清單還開著', async () => {
    // Arrange
    const { user, select } = renderSelect();
    await openOptions(user, select);
    const listbox = await screen.findByRole('listbox');
    const surface = listbox.parentElement?.parentElement;
    if (!(surface instanceof HTMLElement)) throw new Error('清單外面沒有 Floating 的外殼');

    // Act
    const isMouseDefaultKept = fireEvent.mouseDown(surface);
    await user.pointer({ keys: '[MouseLeft>]', target: surface });

    // Assert
    expect(isMouseDefaultKept).toBe(false);
    expect(select).toHaveFocus();
    expect(select).toHaveAttribute('aria-expanded', 'true');
    await user.pointer({ keys: '[/MouseLeft]', target: surface });
  });

  it('滑鼠左鍵按下按鈕就把焦點放到按鈕上（Safari 點 <button> 不給焦點），點擊照常打開；觸控不攔', async () => {
    // Arrange
    const { user, select } = renderSelect();

    // Act
    const isTouchDefaultKept = fireEvent.pointerDown(select, { pointerType: 'touch', button: 0 });
    const isMouseDefaultKept = fireEvent.pointerDown(select, { pointerType: 'mouse', button: 0 });
    const focusedOnPress = document.activeElement;
    await user.click(select);

    // Assert
    expect(isTouchDefaultKept).toBe(true);
    expect(isMouseDefaultKept).toBe(false);
    expect(focusedOnPress).toBe(select);
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(select).toHaveFocus();
  });

  it('滑過選項只移動反白，值不變', async () => {
    // Arrange
    const { user, select, onChange } = renderSelect();
    await openOptions(user, select);
    const done = await screen.findByRole('option', { name: '完成' });

    // Act
    fireEvent.pointerMove(done);

    // Assert
    expect(activeOption(select)).toBe(done);
    expect(onChange).not.toHaveBeenCalled();
  });

  it('點在外面關上、值不變（onDismiss 是 outside）', async () => {
    // Arrange
    const { user, select, onChange, onDismiss } = renderSelect();
    await openOptions(user, select);

    // Act
    await user.click(screen.getByRole('button', { name: '前一個' }));

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('outside');
  });

  it('按鈕被捲出看得到的範圍就關上', async () => {
    // Arrange
    const observer = stubIntersectionObserver();
    const { user, select, onDismiss } = renderSelect();
    await openOptions(user, select);

    // Act
    observer.report(false);

    // Assert
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('scrolled');
    expect(select).toHaveFocus();
  });
});

describe('Select 的狀態', () => {
  it('disabled：原生的停用，打不開', async () => {
    // Arrange
    const { user, select } = renderSelect({ disabled: true });

    // Act
    await user.click(select);

    // Assert
    expect(select).toBeDisabled();
    expect(select).toHaveAttribute('aria-expanded', 'false');
  });

  it('aria-disabled：停得到、打不開，按鍵交給 onKeyDown', async () => {
    // Arrange
    const onKeyDown = vi.fn();
    const { user, select } = renderSelect({ 'aria-disabled': true, onKeyDown });

    // Act
    await user.click(select);
    await user.keyboard('{ArrowDown}');

    // Assert
    expect(select).toHaveFocus();
    expect(select).toHaveAttribute('aria-disabled', 'true');
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(onKeyDown).toHaveBeenCalledWith(expect.objectContaining({ key: 'ArrowDown' }));
  });

  it('aria-busy（送出中）：打得開，但選了不收新的值', async () => {
    // Arrange
    const { user, select, onChange, onPick } = renderSelect({ 'aria-busy': true });

    // Act
    await chooseOption(user, select, '完成');

    // Assert
    expect(select).toHaveAttribute('aria-busy', 'true');
    expect(onChange).not.toHaveBeenCalled();
    expect(onPick).not.toHaveBeenCalled();
    expect(select).toHaveTextContent('進行中');
  });

  it('isOpenInitially：掛上就打開，反白在目前的值', async () => {
    // Arrange
    const { select } = renderSelect({ isOpenInitially: true });

    // Act
    const listbox = await screen.findByRole('listbox', { name: '狀態' });

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(activeOption(select)).toBe(within(listbox).getByRole('option', { name: '進行中' }));
  });

  it('選項換了（例如重新讀到），反白超出範圍時停在最後一個', async () => {
    // Arrange
    const user = userEvent.setup();
    const { rerender } = render(<Select aria-label="狀態" value="done" options={STATES} onChange={vi.fn()} />);
    const select = screen.getByRole('combobox', { name: '狀態' });
    await openOptions(user, select);

    // Act
    rerender(<Select aria-label="狀態" value="done" options={STATES.slice(0, 2)} onChange={vi.fn()} />);

    // Assert
    expect(activeOption(select)?.dataset.value).toBe('doing');
  });

  it('選項帶 groupLabel 時，前面多一行組的名稱：不是選項、不能選、不算在選項裡', async () => {
    // Arrange
    const options: readonly SelectOption[] = [
      { value: 'a1', label: '需求確認', groupLabel: '交付流程' },
      { value: 'a2', label: '結案' },
      { value: 'b1', label: '待辦', groupLabel: '任務流程' },
    ];
    const { user, select } = renderSelect({ options, value: '', placeholder: '狀態' });

    // Act
    const listed = await openOptions(user, select);

    // Assert
    const labels = Array.from(document.querySelectorAll('[data-group-label]')).map((label) => label.textContent);
    expect(labels).toEqual(['交付流程', '任務流程']);
    expect(listed.map((option) => option.dataset.value)).toEqual(['a1', 'a2', 'b1']);
    expect(screen.getAllByRole('option')).toHaveLength(3);
  });
});
