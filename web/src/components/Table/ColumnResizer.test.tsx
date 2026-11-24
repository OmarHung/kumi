import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ResizableHeader } from './ColumnResizer';
import { createColumnWidthStore, type ColumnWidthStore } from './columnWidths';

const STORAGE_KEY = 'example.widths';

/** jsdom 沒有版面：格子現在的寬度假造成固定值，內容的寬度假造成「一個字 10px」。 */
function stubLayout(cellWidth: number) {
  vi.spyOn(HTMLTableCellElement.prototype, 'getBoundingClientRect').mockReturnValue(
    DOMRect.fromRect({ x: 0, y: 0, width: cellWidth, height: 36 }),
  );
  vi.spyOn(HTMLButtonElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    return DOMRect.fromRect({ x: 0, y: 0, width: this.textContent.length * 10, height: 24 });
  });
  Object.defineProperty(Range.prototype, 'getBoundingClientRect', {
    configurable: true,
    value(this: Range) {
      return DOMRect.fromRect({ x: 0, y: 0, width: this.toString().length * 10, height: 20 });
    },
  });
}

function renderTable(store: ColumnWidthStore, onSort = vi.fn()) {
  return { onSort, ...renderTableView(store, onSort) };
}

function renderTableView(store: ColumnWidthStore, onSort: () => void) {
  return render(
    <table aria-label="範例">
      <thead>
        <tr>
          <ResizableHeader label="編號" sizing={{ id: 'key', store }} />
          <ResizableHeader label="到期日" sizing={{ id: 'due', store }} ariaSort="ascending">
            <button type="button" onClick={onSort}>
              到期日
            </button>
          </ResizableHeader>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>EX-1</td>
          <td>2026/12/15 已過期很久</td>
        </tr>
      </tbody>
    </table>,
  );
}

/** 把手是 aria-hidden（不能聚焦，瀏覽模式不必每一格都唸「分隔線」）：從它所在的表頭找，要帶 hidden。 */
function handleOf(label: string): HTMLElement {
  return within(screen.getByRole('columnheader', { name: label })).getByRole('separator', { hidden: true });
}

function dueHandle(): HTMLElement {
  return handleOf('到期日');
}

/** 有 ResizeObserver 的環境：表頭畫出來的寬度（aria-valuenow、aria-valuemax 用）照格子的框。 */
function stubResizeObserver() {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {
        return undefined;
      }
      disconnect() {
        return undefined;
      }
    },
  );
}

let store: ColumnWidthStore;

beforeEach(() => {
  store = createColumnWidthStore(STORAGE_KEY);
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  Reflect.deleteProperty(Range.prototype, 'getBoundingClientRect');
});

describe('欄寬的把手（docs/DESIGN.md 7.1「欄寬」）', () => {
  it('每一欄的表頭有一個直的分隔線把手，帶著範圍；不是 Tab 停駐點，報讀軟體不唸（aria-hidden），表頭的名稱照舊是欄名', () => {
    // Arrange / Act
    renderTable(store);

    // Assert
    const handle = dueHandle();
    expect(handle).toHaveAttribute('aria-orientation', 'vertical');
    expect(handle).toHaveAttribute('aria-label', '調整「到期日」欄寬');
    expect(handle).toHaveAttribute('aria-valuemin', '48');
    expect(handle).toHaveAttribute('aria-valuemax', '640');
    expect(handle).toHaveAttribute('aria-hidden', 'true');
    expect(handle).not.toHaveAttribute('tabindex');
    expect(screen.queryByRole('separator')).not.toBeInTheDocument();
    const header = screen.getByRole('columnheader', { name: '到期日' });
    expect(header).toContainElement(handle);
    expect(header).not.toHaveAttribute('aria-label');
    expect(handleOf('編號')).toBeInTheDocument();
  });

  it('表頭的字被省略時看得到完整的欄名（title）', () => {
    // Arrange / Act
    renderTable(store);

    // Assert
    expect(within(screen.getByRole('columnheader', { name: '編號' })).getByText('編號')).toHaveAttribute('title', '編號');
  });

  it('拖曳：跟著指標換寬度，放開才記住；不會按到表頭的排序按鈕', () => {
    // Arrange
    stubLayout(100);
    const { onSort } = renderTable(store);
    const handle = dueHandle();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 360 });
    const whileDragging = { widths: store.getSnapshot().user, saved: localStorage.getItem(STORAGE_KEY) };
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 380 });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 380 });
    fireEvent.click(handle);

    // Assert
    expect(whileDragging).toEqual({ widths: { due: 160 }, saved: null });
    expect(store.getSnapshot().user).toEqual({ due: 180 });
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ due: 180 });
    expect(handle).toHaveAttribute('aria-valuenow', '180');
    expect(onSort).not.toHaveBeenCalled();
  });

  it('拖曳夾在最小與最大之間；放開之後再移動不會改寬度', () => {
    // Arrange
    stubLayout(100);
    renderTable(store);
    const handle = dueHandle();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 0 });
    const narrowest = store.getSnapshot().user.due;
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 2000 });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 2000 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 400 });

    // Assert
    expect(narrowest).toBe(48);
    expect(store.getSnapshot().user.due).toBe(640);
  });

  it('右鍵或第二根手指按下去不開始拖曳', () => {
    // Arrange
    stubLayout(100);
    renderTable(store);
    const handle = dueHandle();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 2, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 360 });

    // Assert
    expect(store.getSnapshot().user).toEqual({});
  });

  it('點兩下：照表頭與每一格內容最寬的那個調整', () => {
    // Arrange
    stubLayout(100);
    renderTable(store);

    // Act
    fireEvent.doubleClick(dueHandle());

    // Assert：「2026/12/15 已過期很久」16 個字（假造成 160px）比表頭的「到期日」寬，再加上格子左右的內距（jsdom 的 td 預設各 1px）
    expect(store.getSnapshot().user).toEqual({ due: 162 });
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ due: 162 });
  });

  it('鍵盤：焦點在表頭時 Shift+→／← 一次調 1rem，Shift+Enter 照內容調整，不會排序', async () => {
    // Arrange
    stubLayout(100);
    const { onSort } = renderTable(store);
    const user = userEvent.setup();
    const sort = within(screen.getByRole('columnheader', { name: '到期日' })).getByRole('button', { name: '到期日' });
    act(() => {
      sort.focus();
    });

    // Act
    await user.keyboard('{Shift>}{ArrowRight}{/Shift}');
    const wider = store.getSnapshot().user.due;
    await user.keyboard('{Shift>}{ArrowLeft}{ArrowLeft}{/Shift}');
    const narrower = store.getSnapshot().user.due;
    await user.keyboard('{Shift>}{Enter}{/Shift}');

    // Assert：jsdom 裡格子量到的寬度是假造的 100px，每按一次從它算
    expect([wider, narrower]).toEqual([116, 84]);
    expect(store.getSnapshot().user.due).toBe(162);
    expect(onSort).not.toHaveBeenCalled();
    expect(sort).toHaveFocus();
  });

  it('沒按 Shift 的方向鍵、帶著其他修飾鍵的不調寬度（交給表格的方向鍵）', () => {
    // Arrange
    renderTable(store);
    const header = screen.getByRole('columnheader', { name: '編號' });

    // Act
    const plain = fireEvent.keyDown(header, { key: 'ArrowRight' });
    const withAlt = fireEvent.keyDown(header, { key: 'ArrowRight', shiftKey: true, altKey: true });

    // Assert
    expect([plain, withAlt]).toEqual([true, true]);
    expect(store.getSnapshot().user).toEqual({});
  });

  it('拖曳中按 Esc 取消：回到開始拖之前的寬度、不記；Esc 不往外傳（不會關掉旁邊的面板）', () => {
    // Arrange
    stubLayout(100);
    store.setWidth('due', 120);
    renderTable(store);
    const handle = dueHandle();
    const outside = vi.fn();
    document.addEventListener('keydown', outside);

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 360 });
    const whileDragging = store.getSnapshot().user.due;
    const isNotPrevented = fireEvent.keyDown(document.body, { key: 'Escape' });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 400 });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 400 });

    // Assert
    document.removeEventListener('keydown', outside);
    expect(whileDragging).toBe(160);
    expect(store.getSnapshot().user).toEqual({ due: 120 });
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ due: 120 });
    expect(isNotPrevented).toBe(false);
    expect(outside).not.toHaveBeenCalled();
    expect(handle).not.toHaveAttribute('data-dragging');
  });

  it('沒拉過的欄拖曳中按 Esc：回到沒拉過（照內容量的寬度），什麼都不記', () => {
    // Arrange
    stubLayout(100);
    store.setMeasured({ due: 90 });
    renderTable(store);
    const handle = dueHandle();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 360 });
    fireEvent.keyDown(document.body, { key: 'Escape' });

    // Assert
    expect(store.getSnapshot().user).toEqual({});
    expect(handle).toHaveAttribute('aria-valuenow', '90');
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('沒在拖曳時 Esc 照常往外傳', () => {
    // Arrange
    renderTable(store);
    const outside = vi.fn();
    document.addEventListener('keydown', outside);

    // Act
    fireEvent.keyDown(document.body, { key: 'Escape' });

    // Assert
    document.removeEventListener('keydown', outside);
    expect(outside).toHaveBeenCalledTimes(1);
  });

  it('拖曳被瀏覽器取消（pointercancel）和 Esc 一樣：回到原本的寬度、不記，之後的移動不再改', () => {
    // Arrange
    stubLayout(100);
    store.setWidth('due', 120);
    renderTable(store);
    const handle = dueHandle();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 360 });

    // Act
    fireEvent.pointerCancel(handle, { pointerId: 1 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 400 });

    // Assert
    expect(store.getSnapshot().user).toEqual({ due: 120 });
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ due: 120 });
    expect(handle).not.toHaveAttribute('data-dragging');
  });

  it('指標被瀏覽器收回（lostpointercapture）時照放開處理（和面板的把手一樣）：記住拖到的寬度，之後的移動不再改', () => {
    // Arrange
    stubLayout(100);
    renderTable(store);
    const handle = dueHandle();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 360 });

    // Act
    fireEvent.lostPointerCapture(handle, { pointerId: 1 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 400 });
    fireEvent.lostPointerCapture(handle, { pointerId: 1 });

    // Assert
    expect(store.getSnapshot().user).toEqual({ due: 160 });
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ due: 160 });
    expect(handle).not.toHaveAttribute('data-dragging');
  });

  it('拖曳中卸下（換檢視、換頁）：回到原本的寬度，也不再攔 Esc', () => {
    // Arrange
    stubLayout(100);
    const { unmount } = renderTable(store);
    fireEvent.pointerDown(dueHandle(), { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(dueHandle(), { pointerId: 1, clientX: 360 });
    const outside = vi.fn();
    document.addEventListener('keydown', outside);

    // Act
    unmount();
    fireEvent.keyDown(document.body, { key: 'Escape' });

    // Assert
    document.removeEventListener('keydown', outside);
    expect(store.getSnapshot().user).toEqual({});
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(outside).toHaveBeenCalledTimes(1);
  });

  it('正常放開之後 Esc 照常往外傳（關得掉旁邊的面板）', () => {
    // Arrange
    stubLayout(100);
    renderTable(store);
    const handle = dueHandle();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 360 });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 360 });
    const outside = vi.fn();
    document.addEventListener('keydown', outside);

    // Act
    const isNotPrevented = fireEvent.keyDown(document.body, { key: 'Escape' });

    // Assert
    document.removeEventListener('keydown', outside);
    expect(isNotPrevented).toBe(true);
    expect(outside).toHaveBeenCalledTimes(1);
    expect(store.getSnapshot().user).toEqual({ due: 160 });
  });

  it('開始的寬度比上限寬（我的工作沒拉過的標題欄吃掉剩下的寬度）：拖曳、鍵盤都不會先跳回上限；aria-valuemax 不小於現在的寬度', async () => {
    // Arrange：上限是 40rem（640px），格子畫出來 700px
    stubLayout(700);
    stubResizeObserver();
    renderTable(store);
    const handle = dueHandle();
    const user = userEvent.setup();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 290 });
    const narrowed = store.getSnapshot().user.due;
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 340 });
    const widened = store.getSnapshot().user.due;
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 340 });
    act(() => {
      screen.getByRole('button', { name: '到期日' }).focus();
    });
    await user.keyboard('{Shift>}{ArrowRight}{/Shift}');
    const byKeyboard = store.getSnapshot().user.due;

    // Assert：這一次的上限是開始的寬度
    expect([narrowed, widened, byKeyboard]).toEqual([690, 700, 700]);
    expect(handle).toHaveAttribute('aria-valuenow', '700');
    expect(handle).toHaveAttribute('aria-valuemax', '700');
  });

  it('被夾住的欄（畫出來的比記住的窄）：調寬從記住的起算、往右拖不會把記住的改小；調窄從畫出來的起算', async () => {
    // Arrange：記住 500，畫出來被夾成 332（拉寬的標題欄在變窄的表格裡）
    stubLayout(332);
    store.setWidth('due', 500);
    renderTable(store);
    const user = userEvent.setup();
    act(() => {
      screen.getByRole('button', { name: '到期日' }).focus();
    });

    // Act
    await user.keyboard('{Shift>}{ArrowRight}{/Shift}');
    const wider = store.getSnapshot().user.due;
    store.setWidth('due', 500);
    await user.keyboard('{Shift>}{ArrowLeft}{/Shift}');
    const narrower = store.getSnapshot().user.due;
    store.setWidth('due', 500);
    const handle = dueHandle();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 320 });
    const draggedRight = store.getSnapshot().user.due;
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 250 });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 250 });

    // Assert
    expect([wider, narrower, draggedRight]).toEqual([516, 316, 500]);
    expect(store.getSnapshot().user.due).toBe(282);
  });

  it('記住的寬度超出範圍（被改壞）時畫在範圍裡', () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ due: 5000 }));

    // Act
    renderTable(createColumnWidthStore(STORAGE_KEY));

    // Assert
    expect(dueHandle()).toHaveAttribute('aria-valuenow', '640');
  });

  it('照內容調整時不算給報讀軟體的隱藏文字', () => {
    // Arrange
    stubLayout(100);
    vi.spyOn(HTMLSpanElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      return DOMRect.fromRect({ x: 0, y: 0, width: this.textContent.length * 10, height: 20 });
    });
    render(
      <table aria-label="範例">
        <thead>
          <tr>
            <ResizableHeader label="優先度" sizing={{ id: 'priority', store }} />
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <span aria-hidden="true" />
              <span className="visually-hidden">這個項目還沒有設定優先度</span>
            </td>
          </tr>
        </tbody>
      </table>,
    );

    // Act
    fireEvent.doubleClick(handleOf('優先度'));

    // Assert：只有表頭「優先度」（30px）加上內距，再夾到最小寬度
    expect(store.getSnapshot().user).toEqual({ priority: 48 });
  });
});
