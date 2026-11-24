import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useRef, useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useRovingGrid } from './useRovingGrid';

interface TestRow {
  readonly key: string;
  /** 狀態那一格多一個「開啟」連結（一格裡有兩個可以操作的元素）。 */
  readonly hasLink?: boolean;
  /** 狀態那一格在編輯中：按鈕換成編輯器。 */
  readonly isEditing?: boolean;
  /** 狀態那一格的按鈕開著自己的清單（aria-expanded="true"，例如打開的下拉）。 */
  readonly isExpanded?: boolean;
  /** 狀態那一格的按鈕不是編輯用的（data-grid-action，例如成員的「移除」）。 */
  readonly isAction?: boolean;
}

function makeRows(count: number): TestRow[] {
  return Array.from({ length: count }, (_, index) => ({ key: `R${String(index + 1)}` }));
}

interface GridProps {
  readonly rows: readonly TestRow[];
  readonly dir?: 'rtl';
}

/** 測試用的表格：編號（連結）、名稱（唯讀的文字）、狀態（按鈕）；表頭的「名稱」沒有排序按鈕。 */
function Grid({ rows, dir }: GridProps) {
  const ref = useRef<HTMLTableElement>(null);
  const { gridProps, handle } = useRovingGrid(ref);
  const [pressed, setPressed] = useState('');
  return (
    <div dir={dir}>
      <button type="button">前面</button>
      <table ref={ref} aria-label="測試表格" {...gridProps}>
        <thead>
          <tr>
            <th scope="col">
              <button type="button">編號</button>
            </th>
            <th scope="col">名稱</th>
            <th scope="col">
              <button type="button">狀態</button>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} data-row-key={row.key}>
              <td>
                <a href={`#${row.key}`}>{row.key}</a>
              </td>
              <td>名稱 {row.key}</td>
              <td>
                {row.isEditing === true ? (
                  <div data-cell-editor="">
                    <input aria-label={`編輯 ${row.key}`} />
                  </div>
                ) : (
                  <button
                    type="button"
                    aria-expanded={row.isExpanded}
                    data-grid-action={row.isAction === true ? '' : undefined}
                    onClick={() => { setPressed(row.key); }}
                  >
                    狀態 {row.key}
                  </button>
                )}
                {row.hasLink === true && <a href="#open">開啟 {row.key}</a>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <button type="button" onClick={() => { handle.focusRow('R3'); }}>
        到第三列
      </button>
      <button type="button">後面</button>
      <p>按了 {pressed}</p>
    </div>
  );
}

function setup(rows: readonly TestRow[] = makeRows(3), dir?: 'rtl') {
  const user = userEvent.setup();
  const view = render(<Grid rows={rows} dir={dir} />);
  const table = screen.getByRole('table', { name: '測試表格' });
  return { ...view, user, table };
}

/** 表格裡每一個用方向鍵停得到的元素：連結、按鈕、格子、表頭。 */
function stopsIn(table: HTMLElement): HTMLElement[] {
  const grid = within(table);
  return [
    ...grid.getAllByRole('link'),
    ...grid.getAllByRole('button'),
    ...grid.getAllByRole('cell'),
    ...grid.getAllByRole('columnheader'),
  ];
}

function tabStopsIn(table: HTMLElement): HTMLElement[] {
  return stopsIn(table).filter((element) => element.tabIndex === 0);
}

async function enterTable(user: ReturnType<typeof userEvent.setup>) {
  screen.getByRole('button', { name: '前面' }).focus();
  await user.tab();
}

afterEach(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
});

describe('useRovingGrid：一個 Tab 停駐點', () => {
  it('從前面 Tab 進來落在第一個資料格，再按 Tab 就離開表格；Shift+Tab 回到同一格', async () => {
    // Arrange
    const { user } = setup();

    // Act
    await enterTable(user);
    const entered = document.activeElement;
    await user.tab();
    const left = document.activeElement;
    await user.tab({ shift: true });

    // Assert
    expect(entered).toBe(screen.getByRole('link', { name: 'R1' }));
    expect(left).toBe(screen.getByRole('button', { name: '到第三列' }));
    expect(screen.getByRole('link', { name: 'R1' })).toHaveFocus();
  });

  it('同一時間只有一格的 tabIndex 是 0；唯讀的格子與沒有按鈕的表頭自己停得到', () => {
    // Arrange / Act
    const { table } = setup();

    // Assert
    expect(tabStopsIn(table)).toEqual([screen.getByRole('link', { name: 'R1' })]);
    expect(screen.getByRole('cell', { name: '名稱 R2' })).toHaveAttribute('tabindex', '-1');
    expect(screen.getByRole('columnheader', { name: '名稱' })).toHaveAttribute('tabindex', '-1');
    expect(screen.getByRole('button', { name: '狀態 R2' })).toHaveAttribute('tabindex', '-1');
    // 格子裡有可以操作的元素時，停在那個元素上，格子自己不是停駐點。
    expect(screen.getByRole('cell', { name: 'R1' })).not.toHaveAttribute('tabindex');
  });

  it('任何格子拿到焦點（點擊或程式）就成為唯一的停駐點，下一次 Tab 從那裡進來', async () => {
    // Arrange
    const { user, table } = setup();
    const status = screen.getByRole('button', { name: '狀態 R3' });

    // Act
    act(() => {
      status.focus();
    });
    const stopsAfterFocus = tabStopsIn(table);
    await user.click(screen.getByRole('button', { name: '後面' }));
    await user.tab({ shift: true });
    await user.tab({ shift: true });

    // Assert
    expect(stopsAfterFocus).toEqual([status]);
    expect(status).toHaveFocus();
  });
});

describe('useRovingGrid：方向鍵', () => {
  it('一次一格：左右在同一列、上下在同一欄；唯讀的格子停得到，第一列往上是表頭', async () => {
    // Arrange
    const { user } = setup();
    await enterTable(user);

    // Act / Assert
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('cell', { name: '名稱 R1' })).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(screen.getByRole('cell', { name: '名稱 R2' })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('button', { name: '狀態 R2' })).toHaveFocus();
    await user.keyboard('{ArrowUp}{ArrowUp}{ArrowUp}{ArrowUp}');
    expect(screen.getByRole('button', { name: '狀態' })).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(screen.getByRole('columnheader', { name: '名稱' })).toHaveFocus();
    await user.keyboard('{ArrowLeft}{ArrowLeft}');
    expect(screen.getByRole('button', { name: '編號' })).toHaveFocus();
  });

  it('Home／End 到這一列的頭尾；Ctrl+Home／Ctrl+End 到第一格與最後一格', async () => {
    // Arrange
    const { user } = setup();
    await enterTable(user);

    // Act / Assert
    await user.keyboard('{ArrowDown}{End}');
    expect(screen.getByRole('button', { name: '狀態 R2' })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(screen.getByRole('link', { name: 'R2' })).toHaveFocus();
    await user.keyboard('{Control>}{End}{/Control}');
    expect(screen.getByRole('button', { name: '狀態 R3' })).toHaveFocus();
    await user.keyboard('{Control>}{Home}{/Control}');
    expect(screen.getByRole('button', { name: '編號' })).toHaveFocus();
  });

  it('PageUp／PageDown 一次十列，到頭到尾就停在那裡', async () => {
    // Arrange
    const { user } = setup(makeRows(25));
    await enterTable(user);

    // Act / Assert
    await user.keyboard('{PageDown}');
    expect(screen.getByRole('link', { name: 'R11' })).toHaveFocus();
    await user.keyboard('{PageDown}{PageDown}');
    expect(screen.getByRole('link', { name: 'R25' })).toHaveFocus();
    await user.keyboard('{PageUp}');
    expect(screen.getByRole('link', { name: 'R15' })).toHaveFocus();
    await user.keyboard('{PageUp}{PageUp}');
    expect(screen.getByRole('button', { name: '編號' })).toHaveFocus();
  });

  it('一格裡有兩個可以操作的元素：左右鍵先走過格子裡的每一個', async () => {
    // Arrange
    const { user } = setup([{ key: 'R1' }, { key: 'R2', hasLink: true }, { key: 'R3' }]);
    await enterTable(user);

    // Act / Assert
    await user.keyboard('{ArrowDown}{End}');
    expect(screen.getByRole('link', { name: '開啟 R2' })).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(screen.getByRole('button', { name: '狀態 R2' })).toHaveFocus();
    await user.keyboard('{ArrowRight}{ArrowRight}');
    expect(screen.getByRole('link', { name: '開啟 R2' })).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(screen.getByRole('button', { name: '狀態 R3' })).toHaveFocus();
  });

  it('文字方向從右到左時，左右鍵照畫面的方向', async () => {
    // Arrange
    const { user } = setup(makeRows(2), 'rtl');
    await enterTable(user);

    // Act
    await user.keyboard('{ArrowLeft}');

    // Assert
    expect(screen.getByRole('cell', { name: '名稱 R1' })).toHaveFocus();
  });

  it('取得焦點的格子捲到看得到的地方（nearest，讓出黏住的表頭與固定欄）', async () => {
    // Arrange：jsdom 沒有 scrollIntoView，換成記錄呼叫的假函式
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
    const { user } = setup();
    await enterTable(user);

    // Act
    await user.keyboard('{ArrowDown}{ArrowRight}{ArrowRight}');

    // Assert
    expect(scrollIntoView).toHaveBeenLastCalledWith({ block: 'nearest', inline: 'nearest' });
    expect(scrollIntoView.mock.contexts.at(-1)).toBe(screen.getByRole('button', { name: '狀態 R2' }).closest('td'));
  });

  it('Ctrl+Home 走到黏住的表頭時，把第一列捲進來（畫面回到最上面）', async () => {
    // Arrange
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
    const { user } = setup(makeRows(20));
    await enterTable(user);
    await user.keyboard('{Control>}{End}{/Control}');

    // Act
    await user.keyboard('{Control>}{Home}{/Control}');

    // Assert
    expect(screen.getByRole('button', { name: '編號' })).toHaveFocus();
    expect(scrollIntoView.mock.contexts.at(-1)).toBe(screen.getByRole('cell', { name: 'R1' }));
  });
});

describe('useRovingGrid：不搶別人的按鍵', () => {
  it('編輯器裡的方向鍵是編輯器自己的', async () => {
    // Arrange
    const { user } = setup([{ key: 'R1' }, { key: 'R2', isEditing: true }, { key: 'R3' }]);
    const input = screen.getByRole('textbox', { name: '編輯 R2' });
    act(() => {
      input.focus();
    });

    // Act
    await user.keyboard('{ArrowDown}{ArrowLeft}{Home}');

    // Assert
    expect(input).toHaveFocus();
  });

  it('已經被處理過的、輸入法選字中的、按著 Alt、Shift 或 ⌘ 的方向鍵都不處理', async () => {
    // Arrange
    const { user } = setup();
    await enterTable(user);
    const first = screen.getByRole('link', { name: 'R1' });
    const preventOnce = (event: KeyboardEvent) => {
      event.preventDefault();
    };
    first.addEventListener('keydown', preventOnce, { once: true });

    // Act
    await user.keyboard('{ArrowRight}');
    fireEvent.keyDown(first, { key: 'ArrowRight', keyCode: 229 });
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await user.keyboard('{Shift>}{ArrowRight}{/Shift}');
    await user.keyboard('{Meta>}{ArrowRight}{/Meta}');

    // Assert
    expect(first).toHaveFocus();
  });

  it('F2 不按不是編輯用的按鈕（data-grid-action，例如成員的「移除」）', async () => {
    // Arrange
    const { user } = setup([{ key: 'R1', isAction: true }]);
    await enterTable(user);

    // Act
    await user.keyboard('{End}{F2}');

    // Assert
    expect(screen.getByRole('button', { name: '狀態 R1' })).toHaveFocus();
    expect(screen.getByText(/^按了/)).toHaveTextContent(/^按了$/);
  });

  it('開著自己的清單的元素（aria-expanded="true"）上，方向鍵、Home、End、F2 都是清單的：表格不移動、不按它', async () => {
    // Arrange
    const { user } = setup([{ key: 'R1', isExpanded: true }, { key: 'R2' }]);
    await enterTable(user);
    await user.keyboard('{End}');
    const opened = screen.getByRole('button', { name: '狀態 R1' });

    // Act
    await user.keyboard('{ArrowLeft}{ArrowDown}{Home}{PageDown}{F2}');

    // Assert
    expect(opened).toHaveFocus();
    expect(screen.getByText(/^按了/)).toHaveTextContent(/^按了$/);
  });

  it('F2 在可以編輯的格子（按鈕）上等於按下它；在連結上不做事', async () => {
    // Arrange
    const { user } = setup();
    await enterTable(user);

    // Act
    await user.keyboard('{F2}');
    const afterLink = screen.getByText(/^按了/).textContent;
    await user.keyboard('{End}{F2}');

    // Assert
    expect(afterLink).toBe('按了 ');
    expect(screen.getByText(/^按了/)).toHaveTextContent('按了 R1');
  });
});

describe('useRovingGrid：列變了', () => {
  it('焦點所在的那一列被拿掉：焦點移到下一列同一欄，沒有下一列就到上一列', async () => {
    // Arrange
    const { user, rerender } = setup();
    await enterTable(user);
    await user.keyboard('{ArrowDown}{ArrowRight}');

    // Act
    rerender(<Grid rows={[{ key: 'R1' }, { key: 'R3' }]} />);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('cell', { name: '名稱 R3' })).toHaveFocus();
    });

    // Act
    rerender(<Grid rows={[{ key: 'R1' }]} />);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('cell', { name: '名稱 R1' })).toHaveFocus();
    });
    expect(tabStopsIn(screen.getByRole('table'))).toEqual([screen.getByRole('cell', { name: '名稱 R1' })]);
  });

  it('列被拿掉時焦點不在表格裡：只換停駐點，不搶焦點', async () => {
    // Arrange
    const { user, rerender } = setup();
    await enterTable(user);
    await user.keyboard('{ArrowDown}{ArrowRight}');
    const outside = screen.getByRole('button', { name: '後面' });
    await user.click(outside);

    // Act
    rerender(<Grid rows={[{ key: 'R1' }, { key: 'R3' }]} />);
    await waitFor(() => {
      expect(screen.getByRole('cell', { name: '名稱 R3' })).toHaveAttribute('tabindex', '0');
    });

    // Assert
    expect(outside).toHaveFocus();
  });

  it('加了新的列：停駐點與焦點留在原本那一格，新的格子都不是停駐點', async () => {
    // Arrange
    const { user, rerender, table } = setup(makeRows(2));
    await enterTable(user);
    await user.keyboard('{ArrowDown}{End}');
    const status = screen.getByRole('button', { name: '狀態 R2' });

    // Act
    rerender(<Grid rows={makeRows(5)} />);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '狀態 R5' })).toHaveAttribute('tabindex', '-1');
    });
    expect(status).toHaveFocus();
    expect(tabStopsIn(table)).toEqual([status]);
  });

  it('格子換了內容（編輯結束換回按鈕）：新的按鈕是停駐點，焦點原本在編輯器裡就回到它', async () => {
    // Arrange
    const { rerender, table } = setup([{ key: 'R1' }, { key: 'R2', isEditing: true }]);
    act(() => {
      screen.getByRole('textbox', { name: '編輯 R2' }).focus();
    });

    // Act
    rerender(<Grid rows={makeRows(2)} />);

    // Assert
    const status = screen.getByRole('button', { name: '狀態 R2' });
    await waitFor(() => {
      expect(status).toHaveFocus();
    });
    expect(tabStopsIn(table)).toEqual([status]);
  });

  it('focusRow：焦點放到那一列、停駐點那一欄的格子', async () => {
    // Arrange
    const { user } = setup();
    await enterTable(user);
    await user.keyboard('{ArrowRight}');

    // Act
    await user.click(screen.getByRole('button', { name: '到第三列' }));

    // Assert
    expect(screen.getByRole('cell', { name: '名稱 R3' })).toHaveFocus();
  });
});

interface ColumnsGridProps {
  readonly rows: readonly string[];
  readonly columns: readonly string[];
  /** 換了就是另一張表格元素（例如表格整個重新掛上）。 */
  readonly tableKey?: string;
}

/** 欄可以增減的表格，外面包著一層捲動區塊（表格的 TableScroll）。 */
function ColumnsGrid({ rows, columns, tableKey = 'table' }: ColumnsGridProps) {
  const ref = useRef<HTMLTableElement>(null);
  const { gridProps } = useRovingGrid(ref);
  return (
    <div>
      <button type="button">前面</button>
      <div role="region" aria-label="捲動區塊" style={{ overflowX: 'auto', overflowY: 'auto' }}>
        <table key={tableKey} ref={ref} aria-label="欄位表格" {...gridProps}>
          <thead>
            <tr>
              {columns.map((column) => (
                <th key={column} scope="col">
                  {column}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row} data-row-key={row}>
                {columns.map((column) => (
                  <td key={column}>
                    <button type="button">{`${row}-${column}`}</button>
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <button type="button">後面</button>
    </div>
  );
}

function zeroStops(): HTMLElement[] {
  const grid = within(screen.getByRole('table', { name: '欄位表格' }));
  return [...grid.queryAllByRole('button'), ...grid.queryAllByRole('cell'), ...grid.queryAllByRole('columnheader')].filter(
    (element) => element.tabIndex === 0,
  );
}

describe('useRovingGrid：顯示的欄位變了', () => {
  it('隱藏停駐點所在的最後一欄：停駐點到同一列的前一欄，剛好一個 tabIndex 0，Tab 照樣進得來', async () => {
    // Arrange
    const user = userEvent.setup();
    const { rerender } = render(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'B', 'C']} />);
    act(() => {
      screen.getByRole('button', { name: 'R2-C' }).focus();
    });
    await user.click(screen.getByRole('button', { name: '後面' }));

    // Act
    rerender(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'B']} />);

    // Assert
    await waitFor(() => {
      expect(zeroStops()).toEqual([screen.getByRole('button', { name: 'R2-B' })]);
    });
    screen.getByRole('button', { name: '前面' }).focus();
    await user.tab();
    expect(screen.getByRole('button', { name: 'R2-B' })).toHaveFocus();
  });

  it('在停駐點前面多顯示一欄：停駐點還是原本那一格', async () => {
    // Arrange
    const user = userEvent.setup();
    const { rerender } = render(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'C']} />);
    act(() => {
      screen.getByRole('button', { name: 'R2-C' }).focus();
    });
    await user.click(screen.getByRole('button', { name: '後面' }));

    // Act
    rerender(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'B', 'C']} />);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'R2-B' })).toHaveAttribute('tabindex', '-1');
    });
    expect(zeroStops()).toEqual([screen.getByRole('button', { name: 'R2-C' })]);
  });

  it('焦點在格子上時前面多一欄：焦點所在的元素還是唯一的停駐點', async () => {
    // Arrange
    const { rerender } = render(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'C']} />);
    const focused = screen.getByRole('button', { name: 'R2-C' });
    act(() => {
      focused.focus();
    });

    // Act
    rerender(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'B', 'C']} />);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'R1-B' })).toHaveAttribute('tabindex', '-1');
    });
    expect(focused).toHaveFocus();
    expect(zeroStops()).toEqual([focused]);
  });

  it('表格整個換成另一個元素：新的表格照樣只有一個停駐點', async () => {
    // Arrange
    const { rerender } = render(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'B']} />);

    // Act
    rerender(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'B']} tableKey="another" />);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'R2-B' })).toHaveAttribute('tabindex', '-1');
    });
    expect(zeroStops()).toEqual([screen.getByRole('button', { name: 'R1-A' })]);
  });
});

describe('useRovingGrid：捲動與空白鍵', () => {
  it('焦點在格子自己（唯讀的格子、表頭）上按空白鍵不捲動容器；在按鈕上照舊是按下它', async () => {
    // Arrange
    const { user } = setup();
    await enterTable(user);
    await user.keyboard('{ArrowRight}');
    const cell = screen.getByRole('cell', { name: '名稱 R1' });

    // Act
    const isCellDefault = fireEvent.keyDown(cell, { key: ' ' });
    await user.keyboard('{ArrowRight} ');

    // Assert
    expect(isCellDefault).toBe(false);
    expect(screen.getByText(/^按了/)).toHaveTextContent('按了 R1');
  });

  it('按在表格外面那一層捲動區塊（拖捲動條）不算離開：焦點所在的列被拿掉時照樣交給相鄰的一列', async () => {
    // Arrange
    const { rerender } = render(<ColumnsGrid rows={['R1', 'R2', 'R3']} columns={['A', 'B']} />);
    act(() => {
      screen.getByRole('button', { name: 'R2-B' }).focus();
    });

    // Act
    fireEvent.pointerDown(screen.getByRole('region', { name: '捲動區塊' }));
    rerender(<ColumnsGrid rows={['R1', 'R3']} columns={['A', 'B']} />);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'R3-B' })).toHaveFocus();
    });
  });

  it('Home 把表格橫向捲回最前面（第一格是固定欄，捲它本身不會讓容器回到最左邊）', async () => {
    // Arrange：jsdom 沒有版面，讓捲動區塊看起來比表格窄
    vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(2000);
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(500);
    const setScrollLeft = vi.spyOn(Element.prototype, 'scrollLeft', 'set');
    const user = userEvent.setup();
    render(<ColumnsGrid rows={['R1', 'R2']} columns={['A', 'B', 'C']} />);
    act(() => {
      screen.getByRole('button', { name: 'R2-C' }).focus();
    });

    // Act
    await user.keyboard('{Home}');

    // Assert
    expect(screen.getByRole('button', { name: 'R2-A' })).toHaveFocus();
    expect(setScrollLeft).toHaveBeenLastCalledWith(0);
    expect(setScrollLeft.mock.contexts.at(-1)).toBe(screen.getByRole('region', { name: '捲動區塊' }));
  });
});
