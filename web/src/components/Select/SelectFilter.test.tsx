import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { chooseOption, openOptions } from '../../test/select';
import { Dialog } from '../Dialog/Dialog';
import { Select, type SelectOption, type SelectProps } from './Select';

/*
 * 選項多的時候（10 個以上）清單最上面有篩選框（docs/DESIGN.md 7.3「選項很多的時候」）：打開時焦點在篩選框裡，
 * 打字就篩選（比對選項的文字與 keywords，不分大小寫，中文照樣找得到），方向鍵、Enter、Esc、Tab 和沒有篩選框時一樣。
 * 輸入法選字中不篩、按鍵不攔。結果數在停下來之後才唸（aria-live="polite" 的區域：一頁只有一個全域的 status）。
 */

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const PEOPLE: readonly SelectOption[] = [
  { value: '', label: '沒有負責人' },
  { value: 'designer', label: '範例設計師' },
  { value: 'engineer', label: '範例工程師' },
  { value: 'owner', label: '範例使用者', keywords: ['owner@corp.test'] },
  { value: 'member', label: '範例成員', keywords: ['member@corp.test'] },
  { value: 'alice', label: 'Alice Example' },
  { value: 'bob', label: 'Bob Example' },
  { value: 'carol', label: 'Carol Example' },
  { value: 'dave', label: 'Dave Example', disabledReason: '已經停用' },
  { value: 'erin', label: 'Erin Example' },
];

function many(count: number): SelectOption[] {
  return Array.from({ length: count }, (_, index) => ({ value: `AGY-${String(index + 1)}`, label: `AGY-${String(index + 1)} 範例需求 ${String(index + 1)}` }));
}

type Overrides = Partial<Omit<SelectProps, 'onChange' | 'aria-label' | 'aria-labelledby'>>;

type User = ReturnType<typeof userEvent.setup>;

/**
 * 打字停下來之後才換的計時器是 setTimeout：只假它。Testing Library 的等待（asyncWrapper、waitFor）只認得 jest 的假計時器，
 * 給它一個 jest.advanceTimersByTime，等待時才會把假的時間往前推，不會卡住。
 */
function useFakeSettleTimers(): User {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  vi.stubGlobal('jest', {
    advanceTimersByTime: (ms: number) => {
      vi.advanceTimersByTime(ms);
    },
  });
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
}

function renderFilterable(overrides: Overrides = {}, user: User = userEvent.setup()) {
  const onChange = vi.fn();
  const onPick = vi.fn();
  const onDismiss = vi.fn();
  const onBlur = vi.fn();
  const onOuterKeyDown = vi.fn();
  function Harness() {
    const [value, setValue] = useState(overrides.value ?? 'member');
    return (
      <Select
        aria-label="負責人"
        options={PEOPLE}
        onPick={onPick}
        onDismiss={onDismiss}
        onBlur={onBlur}
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
  const select = screen.getByRole('combobox', { name: '負責人' });
  return { user, select, onChange, onPick, onDismiss, onBlur, onOuterKeyDown };
}

async function openFilter(user: ReturnType<typeof userEvent.setup>, select: HTMLElement) {
  await openOptions(user, select);
  const filter = await screen.findByRole('combobox', { name: '篩選 負責人' });
  await waitFor(() => {
    expect(filter).toHaveFocus();
  });
  return filter;
}

/** 結果數的區域（aria-live="polite"，只給報讀軟體）：在清單旁邊。 */
function liveRegion(): HTMLElement {
  const region = screen.getByRole('listbox').parentElement?.querySelector<HTMLElement>('[aria-live="polite"]');
  if (region === null || region === undefined) throw new Error('沒有結果數的區域');
  return region;
}

function activeOf(filter: HTMLElement): HTMLElement | null {
  const id = filter.getAttribute('aria-activedescendant');
  return id === null ? null : document.getElementById(id);
}

function visibleLabels(): string[] {
  const listbox = screen.getByRole('listbox', { name: '負責人' });
  return within(listbox)
    .queryAllByRole('option')
    .filter((option) => option.dataset.value !== undefined)
    .map((option) => option.querySelector('[id$="-name"]')?.textContent ?? '');
}

describe('什麼時候有篩選框', () => {
  it('9 個選項沒有篩選框，焦點留在按鈕；10 個以上自動出現，不用逐處開關', async () => {
    // Arrange
    const { user, select } = renderFilterable({ options: PEOPLE.slice(0, 9) });

    // Act
    await openOptions(user, select);

    // Assert
    expect(screen.queryByRole('combobox', { name: '篩選 負責人' })).not.toBeInTheDocument();
    expect(select).toHaveFocus();
  });

  it('打開時焦點在篩選框裡：名稱是「篩選」加清單的名稱，控制清單、反白用 aria-activedescendant；按鈕不再帶反白', async () => {
    // Arrange
    const { user, select } = renderFilterable();

    // Act
    const filter = await openFilter(user, select);

    // Assert
    expect(filter).toHaveAttribute('aria-controls', screen.getByRole('listbox', { name: '負責人' }).id);
    expect(filter).toHaveAttribute('aria-expanded', 'true');
    expect(filter).toHaveAttribute('aria-autocomplete', 'list');
    expect(activeOf(filter)).toHaveTextContent('範例成員');
    expect(select).not.toHaveAttribute('aria-activedescendant');
    expect(select).toHaveAttribute('aria-expanded', 'true');
  });

  it('用鍵盤打開（Enter）也是：焦點到篩選框', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await screen.findByRole('combobox', { name: '篩選 負責人' })).toHaveFocus();
  });

  it('關著時打字：打開並把字放進篩選框（不是跳到開頭符合的選項）', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('v');

    // Assert
    const filter = await screen.findByRole('combobox', { name: '篩選 負責人' });
    expect(filter).toHaveValue('v');
    await waitFor(() => {
      expect(filter).toHaveFocus();
    });
    expect(visibleLabels()).toEqual(['Dave Example']);
  });
});

describe('篩選', () => {
  it('比對選項的文字（包含，不只開頭）：中文名字找得到', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    await openFilter(user, select);

    // Act
    await user.keyboard('設計');

    // Assert
    expect(visibleLabels()).toEqual(['範例設計師']);
  });

  it('英文不分大小寫；keywords（人員的 Email）也比對', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    const filter = await openFilter(user, select);

    // Act
    await user.keyboard('CORP.TEST');
    const byEmail = visibleLabels();
    await user.clear(filter);
    await user.keyboard('ALICE');

    // Assert
    expect(byEmail).toEqual(['範例使用者', '範例成員']);
    expect(visibleLabels()).toEqual(['Alice Example']);
  });

  it('打字之後反白到第一個符合的，↓／↑ 在篩選後的選項之間移動；Enter 選了：關上、焦點回到按鈕', async () => {
    // Arrange
    const { user, select, onChange, onPick } = renderFilterable();
    const filter = await openFilter(user, select);

    // Act
    await user.keyboard('example');
    const first = activeOf(filter)?.dataset.value;
    await user.keyboard('{ArrowDown}');
    const second = activeOf(filter)?.dataset.value;
    await user.keyboard('{Enter}');

    // Assert
    expect(first).toBe('alice');
    expect(second).toBe('bob');
    expect(onChange).toHaveBeenCalledExactlyOnceWith('bob');
    expect(onPick).toHaveBeenCalledExactlyOnceWith('bob');
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(select).toHaveFocus();
    expect(select).toHaveTextContent('Bob Example');
  });

  it('沒有符合的：清單裡寫「沒有符合的選項」，Enter 不選、清單留著', async () => {
    // Arrange
    const { user, select, onChange } = renderFilterable();
    await openFilter(user, select);

    // Act
    await user.keyboard('zzz{Enter}');

    // Assert
    const listbox = screen.getByRole('listbox', { name: '負責人' });
    expect(within(listbox).getByText('沒有符合的選項')).toBeInTheDocument();
    expect(visibleLabels()).toEqual([]);
    expect(onChange).not.toHaveBeenCalled();
    expect(select).toHaveAttribute('aria-expanded', 'true');
  });

  it('結果數用 aria-live="polite" 的區域唸（不是 role="status"），停下來 300ms 才換（打字中不逐字唸）', async () => {
    // Arrange
    const { user, select } = renderFilterable({}, useFakeSettleTimers());
    await openFilter(user, select);
    const live = liveRegion();

    // Act
    await user.keyboard('example');
    const whileTyping = live.textContent;
    act(() => {
      vi.advanceTimersByTime(299);
    });
    const beforeSettled = live.textContent;
    act(() => {
      vi.advanceTimersByTime(1);
    });
    const settled = live.textContent;
    await user.keyboard('zzz');
    act(() => {
      vi.advanceTimersByTime(300);
    });

    // Assert
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(whileTyping).toBe('');
    expect(beforeSettled).toBe('');
    expect(settled).toBe('5 個符合');
    expect(live).toHaveTextContent('沒有符合的選項');
  });

  it('關著時打字打開：第一次的結果數也唸（打開時是空的，停下來才換成結果數）', async () => {
    // Arrange
    const { user, select } = renderFilterable({}, useFakeSettleTimers());
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('v');
    const live = liveRegion();
    const atOpen = live.textContent;
    act(() => {
      vi.advanceTimersByTime(300);
    });

    // Assert
    expect(atOpen).toBe('');
    expect(live).toHaveTextContent('1 個符合');
  });

  it('關著時連打兩個字：篩選框裡是兩個字、游標在後面', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('ex');

    // Assert
    const filter = await screen.findByRole('combobox', { name: '篩選 負責人' });
    expect(filter).toHaveValue('ex');
    await waitFor(() => {
      expect(filter).toHaveFocus();
    });
    expect((filter as HTMLInputElement).selectionStart).toBe(2);
  });

  it('全形、半形都找得到（兩邊先 NFKC）：「ＡＧＹ－１２」找到 AGY-12', async () => {
    // Arrange
    const { user, select } = renderFilterable({ value: 'AGY-1', options: many(20) });
    const filter = await openFilter(user, select);

    // Act
    fireEvent.change(filter, { target: { value: 'ＡＧＹ－１２' } });

    // Assert
    expect(visibleLabels()).toEqual(['AGY-12 範例需求 12']);
  });

  it('選項的 id 照它在完整清單裡的位置：篩選之後同一個選項 id 不變；反白換了，aria-activedescendant 的值就變', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    const filter = await openFilter(user, select);
    const listbox = screen.getByRole('listbox', { name: '負責人' });
    const bobId = within(listbox).getByRole('option', { name: 'Bob Example' }).id;

    // Act
    await user.keyboard('alice');
    const onAlice = filter.getAttribute('aria-activedescendant');
    await user.clear(filter);
    await user.keyboard('bob');
    const onBob = filter.getAttribute('aria-activedescendant');

    // Assert
    expect(within(listbox).getByRole('option', { name: 'Bob Example' }).id).toBe(bobId);
    expect(onBob).toBe(bobId);
    expect(onAlice).not.toBe(onBob);
    expect(activeOf(filter)).toHaveTextContent('Bob Example');
  });

  it('篩選字換了但反白還是同一個（數量也一樣）：照樣把反白捲到看得到', async () => {
    // Arrange
    const { user, select } = renderFilterable({ value: 'AGY-1', options: many(12) });
    await openFilter(user, select);
    await user.keyboard('AGY');
    const listbox = screen.getByRole('listbox', { name: '負責人' });
    // jsdom 沒有版面：清單在 100～200，反白的選項在它上面（被捲走了），要往上捲才看得到。
    vi.spyOn(listbox, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 100, 200, 100));
    vi.spyOn(HTMLLIElement.prototype, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 0, 200, 20));
    let scrollTop = 500;
    Object.defineProperty(listbox, 'scrollTop', {
      configurable: true,
      get: () => scrollTop,
      set: (next: number) => {
        scrollTop = next;
      },
    });

    // Act：多打一個「-」，符合的還是那 12 個、反白還是第一個。
    await user.keyboard('-');

    // Assert
    expect(visibleLabels()).toHaveLength(12);
    expect(scrollTop).toBe(400);
  });

  it('篩選框帶著按鈕的說明與錯誤（aria-describedby、aria-invalid）', async () => {
    // Arrange
    render(
      <>
        <Select aria-label="負責人" aria-describedby="assignee-error" aria-invalid value="" options={PEOPLE} onChange={vi.fn()} />
        <p id="assignee-error">先選一個人。</p>
      </>,
    );
    const user = userEvent.setup();

    // Act
    const filter = await openFilter(user, screen.getByRole('combobox', { name: '負責人' }));

    // Assert
    expect(filter).toHaveAccessibleDescription('先選一個人。');
    expect(filter).toHaveAttribute('aria-invalid', 'true');
  });

  it('輸入法選字中不篩、按鍵（Enter、方向鍵、Esc）都交給輸入法；選好字才篩', async () => {
    // Arrange
    const { user, select, onChange, onOuterKeyDown, onDismiss } = renderFilterable();
    const filter = await openFilter(user, select);

    // Act
    fireEvent.compositionStart(filter);
    fireEvent.change(filter, { target: { value: 'ㄕㄜˋ' } });
    const duringComposition = visibleLabels().length;
    fireEvent.keyDown(filter, { key: 'Enter', isComposing: true });
    fireEvent.keyDown(filter, { key: 'Escape', isComposing: true });
    fireEvent.keyDown(filter, { key: 'ArrowDown', keyCode: 229 });
    fireEvent.change(filter, { target: { value: '設' } });
    fireEvent.compositionEnd(filter);

    // Assert
    expect(duringComposition).toBe(PEOPLE.length);
    expect(onChange).not.toHaveBeenCalled();
    expect(onDismiss).not.toHaveBeenCalled();
    expect(onOuterKeyDown).not.toHaveBeenCalledWith(expect.objectContaining({ key: 'Escape' }));
    expect(select).toHaveAttribute('aria-expanded', 'true');
    await waitFor(() => {
      expect(visibleLabels()).toEqual(['範例設計師']);
    });
  });

  it('每次打開都從空的篩選開始', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    await openFilter(user, select);
    await user.keyboard('設計{Escape}');

    // Act
    const filter = await openFilter(user, select);

    // Assert
    expect(filter).toHaveValue('');
    expect(visibleLabels()).toHaveLength(PEOPLE.length);
  });
});

describe('篩選框的關上與焦點', () => {
  it('Esc：關上、值不變、焦點回到按鈕，不往外傳', async () => {
    // Arrange
    const { user, select, onChange, onDismiss, onOuterKeyDown } = renderFilterable();
    await openFilter(user, select);

    // Act
    await user.keyboard('設{Escape}');

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(select).toHaveFocus();
    expect(onChange).not.toHaveBeenCalled();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('escape');
    expect(onOuterKeyDown).not.toHaveBeenCalledWith(expect.objectContaining({ key: 'Escape' }));
  });

  it('Tab：關上、值不變，照常往下走（到下拉後面的元素）；這時才算離開（onBlur）', async () => {
    // Arrange
    const { user, select, onChange, onDismiss, onBlur } = renderFilterable();
    await openFilter(user, select);

    // Act
    await user.tab();

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('button', { name: '下一個' })).toHaveFocus();
    expect(onChange).not.toHaveBeenCalled();
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('tab');
    expect(onBlur).toHaveBeenCalledTimes(1);
  });

  it('焦點從按鈕移到篩選框不算離開：onBlur 不叫', async () => {
    // Arrange
    const { user, select, onBlur } = renderFilterable();

    // Act
    await openFilter(user, select);

    // Assert
    expect(onBlur).not.toHaveBeenCalled();
  });

  it('點選項就選：焦點回到按鈕；點篩選框本身不被擋（要能點著放游標）', async () => {
    // Arrange
    const { user, select, onChange } = renderFilterable();
    const filter = await openFilter(user, select);

    // Act
    const isFilterPressKept = fireEvent.mouseDown(filter);
    await chooseOption(user, select, 'Carol Example');

    // Assert
    expect(isFilterPressKept).toBe(true);
    expect(onChange).toHaveBeenCalledExactlyOnceWith('carol');
    expect(select).toHaveFocus();
  });

  it('清單開著、焦點卻在按鈕上（按下滑鼠拖出去才放開）：打的字與方向鍵轉給篩選框，照篩選後的清單算', async () => {
    // Arrange
    const { user, select } = renderFilterable();
    const filter = await openFilter(user, select);
    act(() => {
      select.focus();
    });

    // Act
    await user.keyboard('e');
    const typed = (filter as HTMLInputElement).value;
    const focusedAfterTyping = document.activeElement;
    act(() => {
      select.focus();
    });
    await user.keyboard('{ArrowDown}');

    // Assert
    expect(typed).toBe('e');
    expect(focusedAfterTyping).toBe(filter);
    expect(filter).toHaveFocus();
    expect(visibleLabels()[1]).toBe(activeOf(filter)?.querySelector('[id$="-name"]')?.textContent);
    expect(select).toHaveAttribute('aria-expanded', 'true');
  });

  it('對話框裡最後一個可停的是有篩選框的下拉：從篩選框按 Tab 繞回對話框的第一個，不跑出對話框', async () => {
    // Arrange
    const user = userEvent.setup();
    render(
      <>
        <button type="button">對話框外面</button>
        <Dialog title="指派" onClose={vi.fn()}>
          <button type="button">第一個</button>
          <Select aria-label="負責人" value="" options={PEOPLE} onChange={vi.fn()} />
        </Dialog>
      </>,
    );
    const dialog = screen.getByRole('dialog', { name: '指派' });
    await openFilter(user, within(dialog).getByRole('combobox', { name: '負責人' }));

    // Act
    await user.tab();

    // Assert
    expect(dialog).toContainElement(document.activeElement as HTMLElement | null);
    expect(within(dialog).getByRole('button', { name: '第一個' })).toHaveFocus();
  });

  it('再按一次按鈕：關上（toggle），焦點在按鈕', async () => {
    // Arrange
    const { user, select, onDismiss } = renderFilterable();
    await openFilter(user, select);

    // Act
    await user.click(select);

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(onDismiss).toHaveBeenCalledExactlyOnceWith('toggle');
    expect(select).toHaveFocus();
  });
});

describe('200 個選項（上層項目最多 200 個）', () => {
  it('篩選時只畫符合的；還符合的選項是同一個元素（不是每次按鍵整個清單重畫）', async () => {
    // Arrange
    const { user, select } = renderFilterable({ value: 'AGY-1', options: many(200) });
    await openFilter(user, select);
    const listbox = screen.getByRole('listbox', { name: '負責人' });
    const target = within(listbox).getByRole('option', { name: 'AGY-123 範例需求 123' });

    // Act
    await user.keyboard('12');
    const afterTwo = within(listbox).getByRole('option', { name: 'AGY-123 範例需求 123' });
    await user.keyboard('3');

    // Assert
    expect(afterTwo).toBe(target);
    expect(within(listbox).getByRole('option', { name: 'AGY-123 範例需求 123' })).toBe(target);
    expect(visibleLabels()).toEqual(['AGY-123 範例需求 123']);
  });
});
