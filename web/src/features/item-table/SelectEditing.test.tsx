import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { created, serveActivity } from '../../test/activity';
import { ok, signedInAs } from '../../test/api';
import { holdTransition } from '../../test/board';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { detailOf, serveItem, serveItems, serveTransition } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

/*
 * 格子裡的下拉（cells/editors.tsx 的 SelectEditor，docs/DESIGN.md 7.3「在格子與對話框裡」、HANDOFF-custom-controls 3A.3）：
 * 進入編輯清單就打開；選了（點選、Enter、空白鍵）就存；方向鍵只移動反白；Esc 一次就關上清單並取消編輯；
 * Tab、點外面、再按一次按鈕是關上、不改、結束編輯。
 */

const TODO = smkItem({ number: 1, title: '首頁設計稿' });
const DOING = { ...TODO, state: 'doing', stateCategory: 'in_progress' as const };

const NEXT_PAGE = smkItem({ number: 2, title: '聯絡表單' });

/** 有第二頁：表格後面有「載入更多」，Tab 離開表格時有地方去。 */
async function openStateEditor() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [TODO], [NEXT_PAGE]);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  const row = within(table).getByRole('row', { name: /SMK-1/ });
  await view.user.click(within(row).getByRole('button', { name: '狀態：待辦' }));
  const select = await within(row).findByRole('combobox', { name: '狀態' });
  return { ...view, table, row, select };
}

/** 反白的選項：按鈕的 aria-activedescendant 指到它。 */
function activeOption(select: HTMLElement): HTMLElement | null {
  const id = select.getAttribute('aria-activedescendant');
  return id === null ? null : document.getElementById(id);
}

describe('格子裡的下拉', () => {
  it('格子裡是 bare 的下拉：框由格子畫，只有一圈（docs/DESIGN.md 8.3「就地編輯」）', async () => {
    // Arrange & Act
    const { select } = await openStateEditor();

    // Assert
    expect(select).toHaveAttribute('data-variant', 'bare');
    expect(select.closest('[data-cell-editor]')).not.toBeNull();
  });

  it('進入編輯清單就打開，反白在目前的值，焦點在下拉上', async () => {
    // Arrange
    const { row, select } = await openStateEditor();

    // Act
    const listbox = await within(row).findByRole('listbox', { name: '狀態' });

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'true');
    expect(select).toHaveFocus();
    expect(activeOption(select)).toBe(within(listbox).getByRole('option', { name: '待辦（目前）' }));
  });

  it('方向鍵只移動反白：不送出，也不移動表格的格子；Enter 選了就存，焦點回到格子', async () => {
    // Arrange
    const { user, row, select } = await openStateEditor();
    const requests = serveTransition(TODO, DOING);

    // Act
    await user.keyboard('{ArrowDown}{ArrowRight}{ArrowLeft}');
    await flushPending();
    const sentBeforeEnter = requests.length;
    const busyBeforeEnter = select.getAttribute('aria-busy');
    const focusedBeforeEnter = document.activeElement;
    const highlighted = activeOption(select)?.textContent;
    await user.keyboard('{Enter}');

    // Assert
    expect(sentBeforeEnter).toBe(0);
    expect(busyBeforeEnter).toBeNull();
    expect(focusedBeforeEnter).toBe(select);
    expect(highlighted).toBe('進行中');
    expect(await within(row).findByRole('button', { name: '狀態：進行中' })).toHaveFocus();
    expect(requests.map((request) => request.body)).toEqual([{ to: 'doing' }]);
  });

  it('Esc 一次就關上清單並取消編輯：不送出，焦點回到格子', async () => {
    // Arrange
    const { user, row } = await openStateEditor();

    // Act
    await user.keyboard('{ArrowDown}{Escape}');

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：待辦' })).toHaveFocus();
    expect(within(row).queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('選了目前的值：結束編輯、不送出，焦點回到格子', async () => {
    // Arrange
    const { user, row, select } = await openStateEditor();

    // Act
    await chooseOption(user, select, '待辦（目前）');

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：待辦' })).toHaveFocus();
  });

  it('再按一次下拉：關上清單、結束編輯、不送出，焦點回到格子', async () => {
    // Arrange
    const { user, row, select } = await openStateEditor();

    // Act
    await user.click(select);

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：待辦' })).toHaveFocus();
    expect(within(row).queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('Tab：關上、不改、結束編輯，焦點離開表格到「載入更多」', async () => {
    // Arrange
    const { user, row } = await openStateEditor();

    // Act
    await user.tab();

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：待辦' })).toBeInTheDocument();
    expect(within(row).queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '載入更多' })).toHaveFocus();
  });

  it('點在外面：關上、不改、結束編輯', async () => {
    // Arrange
    const { user, row } = await openStateEditor();

    // Act
    await user.click(screen.getByRole('searchbox', { name: '搜尋' }));

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：待辦' })).toBeInTheDocument();
    expect(within(row).queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveFocus();
  });

  it('送出中（aria-busy）：打得開，但選了不收；送出的是第一次選的', async () => {
    // Arrange
    const { user, row, select } = await openStateEditor();
    const held = holdTransition(TODO, () => ok(DOING));
    await chooseOption(user, select, '進行中');
    const busy = await within(row).findByRole('combobox', { name: '狀態' });
    await waitFor(() => {
      expect(held.bodies).toHaveLength(1);
    });

    // Act
    await chooseOption(user, busy, '完成');

    // Assert
    expect(busy).toHaveAttribute('aria-busy', 'true');
    expect(busy).toHaveTextContent('進行中');
    act(() => {
      held.release();
    });
    expect(await within(row).findByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(held.bodies).toEqual([{ to: 'doing' }]);
  });
});

const PANEL_ITEM = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress' });

/** 表格上點編號開項目面板，在面板的欄位裡打開狀態的下拉。 */
async function openPanelStateEditor() {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveItems('AGY', [PANEL_ITEM]);
  serveItem(detailOf(PANEL_ITEM));
  serveActivity(PANEL_ITEM.key, [created()]);
  const view = renderApp('/projects/AGY');
  const table = await screen.findByRole('table', { name: '項目' });
  await view.user.click(within(table).getByRole('link', { name: '驗收中的需求' }));
  const panel = await screen.findByRole('complementary', { name: '項目 AGY-4' });
  const fields = await within(panel).findByRole('region', { name: '欄位' });
  await view.user.click(within(fields).getByRole('button', { name: '狀態：客戶驗收' }));
  await within(fields).findByRole('listbox', { name: '狀態' });
  return { ...view, fields };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('項目面板裡的下拉', () => {
  it('按鈕被捲出看得到的範圍：關上清單、結束編輯、不送出，焦點回到那一格（不掉到頁面本體）', async () => {
    // Arrange
    const observer = stubIntersectionObserver();
    const { fields } = await openPanelStateEditor();
    const requests = serveTransition(PANEL_ITEM, PANEL_ITEM);

    // Act
    observer.report(false);

    // Assert
    expect(await within(fields).findByRole('button', { name: '狀態：客戶驗收' })).toHaveFocus();
    expect(within(fields).queryByRole('listbox')).not.toBeInTheDocument();
    await flushPending();
    expect(requests).toHaveLength(0);
  });
});

describe('項目面板裡的下拉：Esc 的順序是清單 → 格子 → 面板', () => {
  it('第一次 Esc 關上清單並取消編輯，面板還開著；再按一次才關上面板', async () => {
    // Arrange
    const { user, fields } = await openPanelStateEditor();

    // Act
    await user.keyboard('{Escape}');
    const isPanelOpenAfterFirst = screen.queryByRole('complementary', { name: '項目 AGY-4' }) !== null;
    const stateCell = await within(fields).findByRole('button', { name: '狀態：客戶驗收' });
    const focusedAfterFirst = document.activeElement;
    await user.keyboard('{Escape}');

    // Assert
    expect(isPanelOpenAfterFirst).toBe(true);
    expect(focusedAfterFirst).toBe(stateCell);
    await waitFor(() => {
      expect(screen.queryByRole('complementary', { name: '項目 AGY-4' })).not.toBeInTheDocument();
    });
  });
});
