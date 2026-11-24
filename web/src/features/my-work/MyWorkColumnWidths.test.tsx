import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

const overdue = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });
const undated = smkItem({ number: 2, title: '整理素材與這一季的參考資料' });

const STORAGE_KEY = 'my-work.widths';

async function openMyWork() {
  signedInAs(exampleOwner, { details: [smkDetail()], myWork: [overdue, undated] });
  const view = renderApp('/my-work');
  const region = await screen.findByRole('region', { name: '我的工作' });
  await within(region).findAllByRole('button', { name: '狀態：待辦' });
  return { ...view, region };
}

/** 每一組的表頭都有同一欄的把手（aria-hidden：從表頭找）；第一個是第一組（已過期）的。 */
function handlesOf(label: string): HTMLElement[] {
  return screen
    .getAllByRole('columnheader', { name: label })
    .map((header) => within(header).getByRole('separator', { hidden: true }));
}

function firstHandle(label: string): HTMLElement {
  const [handle] = handlesOf(label);
  if (handle === undefined) throw new Error(`找不到「${label}」的把手`);
  return handle;
}

function drag(handle: HTMLElement, from: number, to: number) {
  fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: from });
  fireEvent.pointerMove(handle, { pointerId: 1, clientX: to });
  fireEvent.pointerUp(handle, { pointerId: 1, clientX: to });
}

function stubHeaderWidth(width: number) {
  vi.spyOn(HTMLTableCellElement.prototype, 'getBoundingClientRect').mockReturnValue(
    DOMRect.fromRect({ x: 0, y: 0, width, height: 36 }),
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  Reflect.deleteProperty(Range.prototype, 'getBoundingClientRect');
});

describe('我的工作：欄寬（docs/DESIGN.md 7.1「欄寬」、8.3「我的工作」）', () => {
  it('每一組的每一欄都有把手；表頭的名稱照舊；說明提到調寬的按鍵', async () => {
    // Arrange / Act
    await openMyWork();

    // Assert
    for (const label of ['編號', '標題', '狀態', '優先度', '到期日', '專案']) {
      expect(handlesOf(label)).toHaveLength(2);
    }
    const table = screen.getByRole('table', { name: '已過期' });
    expect(within(table).getByRole('columnheader', { name: '到期日' })).toContainElement(firstHandle('到期日'));
    await waitFor(() => {
      expect(table).toHaveAccessibleDescription(
        '用方向鍵在格子之間移動，Enter 或 F2 編輯，Tab 離開表格。在表頭按 Shift 加左右方向鍵調整欄寬，Shift 加 Enter 照內容調整。',
      );
    });
  });

  it('拉一組的欄，所有組一起變（同一組變數），記在這個瀏覽器，下次打開還在', async () => {
    // Arrange
    stubHeaderWidth(100);
    const first = await openMyWork();

    // Act
    drag(firstHandle('到期日'), 400, 480);
    first.unmount();
    const { region } = await openMyWork();

    // Assert
    expect(region.style.getPropertyValue('--my-work-due-width')).toBe('180px');
    expect(handlesOf('到期日').map((handle) => handle.getAttribute('aria-valuenow'))).toEqual(['180', '180']);
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ due: 180 });
  });

  it('拉過的標題欄換掉它的最小寬度（固定欄的位置、表格的最小寬度、捲動讓出的寬度都跟著），夾在表格寬度之內', async () => {
    // Arrange
    stubHeaderWidth(200);
    const { region } = await openMyWork();

    // Act
    drag(firstHandle('標題'), 300, 420);

    // Assert：表格寬度（包住捲動區塊的容器的 100cqi）減掉它左邊固定的欄（預設是編號與專案）與一段留給其他欄的寬度：
    // 開著面板時固定欄不會蓋滿畫面
    expect(region.style.getPropertyValue('--my-work-title-min')).toBe(
      'min(320px, 100cqi - var(--my-work-pinned-width) - var(--my-work-scroll-reserve))',
    );
    expect(region.style.getPropertyValue('--my-work-pinned-width')).toBe('calc(var(--my-work-key-width) + var(--my-work-project-width))');
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ title: 320 });
  });

  it('點兩下照所有組裡最寬的內容調整（第二組的長標題也算）', async () => {
    // Arrange：jsdom 沒有版面，元素與文字的寬度假造成「一個字 10px」
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      return DOMRect.fromRect({ x: 0, y: 0, width: this.textContent.length * 10, height: 20 });
    });
    Object.defineProperty(Range.prototype, 'getBoundingClientRect', {
      configurable: true,
      value(this: Range) {
        return DOMRect.fromRect({ x: 0, y: 0, width: this.toString().length * 10, height: 20 });
      },
    });
    await openMyWork();

    // Act
    fireEvent.doubleClick(firstHandle('標題'));

    // Assert：「整理素材與這一季的參考資料」13 個字，加上格子左右的內距（jsdom 的 td 預設各 1px）
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ title: 132 });
  });

  it('鍵盤：第二組表頭的 Shift+← 也是調同一欄（焦點在表頭的排序按鈕上）', async () => {
    // Arrange
    stubHeaderWidth(100);
    const { user } = await openMyWork();
    const column = within(screen.getByRole('table', { name: '沒有到期日' })).getByRole('columnheader', { name: '優先度' });
    const header = within(column).getByRole('button', { name: '優先度' });
    act(() => {
      header.focus();
    });

    // Act
    await user.keyboard('{Shift>}{ArrowLeft}{/Shift}');

    // Assert
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ priority: 84 });
    expect(header).toHaveFocus();
  });

  it('拉過才有「重設欄寬」：按了回到預設、按鈕不見，焦點到頁面的標題', async () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ due: 200 }));
    const { user, region } = await openMyWork();

    // Act
    await user.click(screen.getByRole('button', { name: '重設欄寬' }));

    // Assert
    expect(screen.queryByRole('button', { name: '重設欄寬' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '我的工作' })).toHaveFocus();
    expect(region.style.getPropertyValue('--my-work-due-width')).toBe('');
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('「重設欄寬」只看已經記住的：拖曳中不出現，放開才出現', async () => {
    // Arrange
    stubHeaderWidth(100);
    await openMyWork();
    const handle = firstHandle('到期日');

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 400 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 450 });
    const whileDragging = screen.queryByRole('button', { name: '重設欄寬' });
    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 450 });

    // Assert
    expect(whileDragging).not.toBeInTheDocument();
    expect(await screen.findByRole('button', { name: '重設欄寬' })).toBeInTheDocument();
  });

  it('記住的寬度裡沒有這一頁的欄（別的版本留下的、被改壞的）時不算調過', async () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ removed_column: 200 }));

    // Act
    await openMyWork();

    // Assert
    expect(screen.queryByRole('button', { name: '重設欄寬' })).not.toBeInTheDocument();
  });

  it('記住的寬度超出範圍時畫在範圍裡；標題欄的上限交給容器（夾在表格寬度之內）', async () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ key: 5000, due: 1, title: 10 }));

    // Act
    const { region } = await openMyWork();

    // Assert
    expect(region.style.getPropertyValue('--my-work-key-width')).toBe('256px');
    expect(region.style.getPropertyValue('--my-work-due-width')).toBe('48px');
    expect(region.style.getPropertyValue('--my-work-title-min')).toBe(
      'min(96px, 100cqi - var(--my-work-pinned-width) - var(--my-work-scroll-reserve))',
    );
  });

  it('沒拉過的標題欄比 32rem 寬（吃掉剩下的寬度）：往窄拉一點就是那個寬度，不會先跳回 32rem', async () => {
    // Arrange：表頭畫出來 700px（上限 32rem 是 512px）
    stubHeaderWidth(700);
    const { region } = await openMyWork();

    // Act
    drag(firstHandle('標題'), 400, 390);

    // Assert
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toEqual({ title: 690 });
    expect(region.style.getPropertyValue('--my-work-title-min')).toBe(
      'min(690px, 100cqi - var(--my-work-pinned-width) - var(--my-work-scroll-reserve))',
    );
  });

  it('沒拉過時沒有「重設欄寬」', async () => {
    // Arrange / Act
    await openMyWork();

    // Assert
    expect(screen.queryByRole('button', { name: '重設欄寬' })).not.toBeInTheDocument();
  });
});
