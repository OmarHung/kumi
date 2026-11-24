import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const ONE = smkItem({ number: 1, title: '首頁設計稿', dueDate: '2026-12-01' });
const TWO = smkItem({ number: 2, title: '聯絡表單' });

const STORAGE_KEY = 'item-table.widths.SMK';

async function openTable() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [ONE, TWO]);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  return { ...view, table };
}

/** jsdom 沒有版面：表頭的格子量起來一律是 width 寬。 */
function stubHeaderWidth(width: number) {
  vi.spyOn(HTMLTableCellElement.prototype, 'getBoundingClientRect').mockReturnValue(
    DOMRect.fromRect({ x: 0, y: 0, width, height: 36 }),
  );
}

function drag(handle: HTMLElement, from: number, to: number) {
  fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: from });
  fireEvent.pointerMove(handle, { pointerId: 1, clientX: to });
  fireEvent.pointerUp(handle, { pointerId: 1, clientX: to });
}

/** 把手是 aria-hidden：從它所在的表頭找。 */
function handleOf(label: string): HTMLElement {
  return within(screen.getByRole('columnheader', { name: label })).getByRole('separator', { hidden: true });
}

function savedWidths(): unknown {
  return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}');
}

/** jsdom 沒有版面：元素與文字的寬度假造成「一個字 10px」（照內容量欄寬時用）。 */
function stubContentWidths() {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    return DOMRect.fromRect({ x: 0, y: 0, width: this.textContent.length * 10, height: 20 });
  });
  Object.defineProperty(Range.prototype, 'getBoundingClientRect', {
    configurable: true,
    value(this: Range) {
      return DOMRect.fromRect({ x: 0, y: 0, width: this.toString().length * 10, height: 20 });
    },
  });
}

afterEach(() => {
  vi.restoreAllMocks();
  Reflect.deleteProperty(Range.prototype, 'getBoundingClientRect');
});

describe('專案的表格：欄寬（docs/DESIGN.md 7.1「欄寬」）', () => {
  it('每一欄（包含固定的編號與標題）的表頭右緣都有把手；表頭的名稱照舊，表格仍然只有一個停駐點', async () => {
    // Arrange / Act
    const { table } = await openTable();

    // Assert
    for (const label of ['編號', '標題', '狀態', '負責人', '到期日', '摘要']) {
      expect(within(table).getByRole('columnheader', { name: label })).toContainElement(handleOf(label));
    }
    expect(handleOf('標題')).not.toHaveAttribute('tabindex');
    await waitFor(() => {
      expect(within(table).getByRole('checkbox', { name: '選取 SMK-1' })).toHaveAttribute('tabindex', '0');
    });
  });

  it('拖曳到期日的把手：寬度記在這個專案，重新打開還在', async () => {
    // Arrange
    stubHeaderWidth(100);
    const first = await openTable();

    // Act
    drag(handleOf('到期日'), 500, 580);
    first.unmount();
    await openTable();

    // Assert
    expect(savedWidths()).toEqual({ due_date: 180 });
    expect(handleOf('到期日')).toHaveAttribute('aria-valuenow', '180');
  });

  it('固定的編號與標題：拉的寬度換掉固定欄的變數（欄寬、標題的省略號、捲動讓出的寬度都跟著）', async () => {
    // Arrange
    stubHeaderWidth(200);
    await openTable();
    const region = screen.getByRole('region', { name: '項目表格' });

    // Act
    drag(handleOf('編號'), 100, 60);
    drag(handleOf('標題'), 300, 420);

    // Assert：標題欄最寬不超過表格的寬度減掉編號欄與一段留給其他欄的寬度（放不下時固定欄不會蓋滿畫面）
    expect(region.style.getPropertyValue('--item-key-width')).toBe('160px');
    expect(region.style.getPropertyValue('--item-title-width')).toBe(
      'min(320px, 100cqi - var(--item-key-width) - var(--item-scroll-reserve))',
    );
    expect(savedWidths()).toEqual({ key: 160, title: 320 });
  });

  it('「顯示的欄位」裡的「重設欄寬」回到預設', async () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ due_date: 300, title: 400 }));
    const { user } = await openTable();
    expect(handleOf('到期日')).toHaveAttribute('aria-valuenow', '300');

    // Act
    await user.click(screen.getByRole('button', { name: '顯示的欄位' }));
    await user.click(await screen.findByRole('button', { name: '重設欄寬' }));

    // Assert
    expect(handleOf('到期日')).not.toHaveAttribute('aria-valuenow');
    expect(screen.getByRole('region', { name: '項目表格' }).style.getPropertyValue('--item-title-width')).toBe('');
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('鍵盤：焦點在表頭時 Shift+→ 調寬，沒按 Shift 的方向鍵照樣在格子之間移動；表格的說明有寫', async () => {
    // Arrange
    stubHeaderWidth(100);
    const { user, table } = await openTable();
    const due = within(within(table).getByRole('columnheader', { name: '到期日' })).getByRole('button', { name: '到期日' });
    act(() => {
      due.focus();
    });

    // Act
    await user.keyboard('{Shift>}{ArrowRight}{/Shift}');
    const afterResize = document.activeElement;
    await user.keyboard('{ArrowRight}');

    // Assert
    expect(savedWidths()).toEqual({ due_date: 116 });
    expect(afterResize).toBe(due);
    expect(within(within(table).getByRole('columnheader', { name: '估計' })).getByRole('button', { name: '估計' })).toHaveFocus();
    expect(table).toHaveAccessibleDescription(
      '用方向鍵在格子之間移動，Enter 或 F2 編輯，Tab 離開表格。在表頭按 Shift 加左右方向鍵調整欄寬，Shift 加 Enter 照內容調整。',
    );
  });

  it('記住的寬度超出範圍（被改壞）時畫在範圍裡：編號欄不會蓋滿畫面', async () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ key: 5000, title: 10, due_date: 9000 }));

    // Act
    await openTable();

    // Assert
    const region = screen.getByRole('region', { name: '項目表格' });
    expect(region.style.getPropertyValue('--item-key-width')).toBe('256px');
    expect(region.style.getPropertyValue('--item-title-width')).toBe(
      'min(96px, 100cqi - var(--item-key-width) - var(--item-scroll-reserve))',
    );
    expect(handleOf('到期日')).toHaveAttribute('aria-valuenow', '640');
  });

  it('表頭的字被省略時看得到完整的欄名（title）', async () => {
    // Arrange / Act
    const { table } = await openTable();

    // Assert
    const header = within(table).getByRole('columnheader', { name: '到期日' });
    expect(within(header).getByText('到期日')).toHaveAttribute('title', '到期日');
  });

  it('記住的欄寬壞掉時當成沒有設定', async () => {
    // Arrange
    localStorage.setItem(STORAGE_KEY, '{not json');

    // Act
    await openTable();

    // Assert
    expect(handleOf('到期日')).not.toHaveAttribute('aria-valuenow');
  });

  describe('沒拉過的欄照內容量一次（預設寬度）', () => {
    const SHORT = smkItem({ number: 1, title: '首頁設計稿', fields: { summary: '首頁的新版型說明' } });
    const EMPTY = smkItem({ number: 2, title: '聯絡表單' });
    const LONG = smkItem({ number: 3, title: '上線檢查', fields: { summary: '很長很長很長很長的摘要文字說明' } });

    async function openPaged() {
      stubContentWidths();
      signedInAs(exampleOwner, { details: [smkDetail()] });
      serveItems('SMK', [SHORT, EMPTY], [LONG]);
      const view = renderApp('/projects/SMK');
      await screen.findByRole('table', { name: '項目' });
      return view;
    }

    it('第一次有資料時量一次：表頭與每一格裡最寬的那個加上內距，不記在這個瀏覽器', async () => {
      // Arrange / Act
      await openPaged();

      // Assert：「首頁的新版型說明」8 個字（假造成 80px）加上格子左右的內距（jsdom 的 td 預設各 1px）
      expect(handleOf('摘要')).toHaveAttribute('aria-valuenow', '82');
      // 比最小寬度（3rem）窄的夾到最小：表頭「備註」與「未填」都只有兩個字
      expect(handleOf('備註')).toHaveAttribute('aria-valuenow', '48');
      expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    });

    it('載入更多不重新量（版面不跳）；換了顯示的欄位才重新量', async () => {
      // Arrange
      const { user } = await openPaged();

      // Act
      await user.click(screen.getByRole('button', { name: '載入更多' }));
      await screen.findByRole('row', { name: /SMK-3/ });
      const afterLoadMore = handleOf('摘要').getAttribute('aria-valuenow');
      await user.click(screen.getByRole('button', { name: '顯示的欄位' }));
      await user.click(within(screen.getByRole('group', { name: '顯示的欄位' })).getByRole('checkbox', { name: '備註' }));

      // Assert
      expect(afterLoadMore).toBe('82');
      await waitFor(() => {
        expect(handleOf('摘要')).toHaveAttribute('aria-valuenow', '152');
      });
      expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    });

    it('照內容量的預設最寬 17.5rem（原本長文字在表格裡最多 16rem 加上內距）；點兩下才照全部的內容（最寬 40rem）', async () => {
      // Arrange
      stubContentWidths();
      const essay = smkItem({ number: 4, title: '長說明', fields: { summary: '這是一段很長的摘要'.repeat(4) } });
      signedInAs(exampleOwner, { details: [smkDetail()] });
      serveItems('SMK', [essay]);
      renderApp('/projects/SMK');
      await screen.findByRole('table', { name: '項目' });
      const asDefault = handleOf('摘要').getAttribute('aria-valuenow');

      // Act
      fireEvent.doubleClick(handleOf('摘要'));

      // Assert：36 個字（假造成 360px）加上內距
      expect(asDefault).toBe('280');
      expect(savedWidths()).toEqual({ 'field:summary': 362 });
    });

    it('「重設欄寬」：拉過的欄回到沒拉過，照現在的內容重新量', async () => {
      // Arrange
      const { user } = await openPaged();
      await user.click(screen.getByRole('button', { name: '載入更多' }));
      await screen.findByRole('row', { name: /SMK-3/ });
      drag(handleOf('摘要'), 300, 400);
      expect(savedWidths()).toEqual({ 'field:summary': 120 });

      // Act
      await user.click(screen.getByRole('button', { name: '顯示的欄位' }));
      await user.click(await screen.findByRole('button', { name: '重設欄寬' }));

      // Assert
      expect(handleOf('摘要')).toHaveAttribute('aria-valuenow', '152');
      expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    });
  });
});
