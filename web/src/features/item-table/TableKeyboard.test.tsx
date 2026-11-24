import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item, ItemPage } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const ONE = smkItem({ number: 1, title: '首頁設計稿' });
const TWO = smkItem({ number: 2, title: '聯絡表單' });
const THREE = smkItem({ number: 3, title: '上線檢查' });
const FOUR = smkItem({ number: 4, title: '效能調校' });

async function openTable(...pages: readonly (readonly Item[])[]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', ...pages);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  return { ...view, table };
}

function rowOf(table: HTMLElement, item: Item): HTMLElement {
  return within(table).getByRole('row', { name: new RegExp(item.key) });
}

function stateButton(table: HTMLElement, item: Item): HTMLElement {
  return within(rowOf(table, item)).getByRole('button', { name: /^狀態：/ });
}

/** 表格裡每一個用方向鍵停得到的元素。 */
function tabStopsIn(table: HTMLElement): HTMLElement[] {
  const grid = within(table);
  return [
    ...grid.getAllByRole('link'),
    ...grid.getAllByRole('button'),
    ...grid.getAllByRole('checkbox'),
    ...grid.getAllByRole('cell'),
    ...grid.getAllByRole('rowheader'),
    ...grid.getAllByRole('columnheader'),
  ].filter((element) => element.tabIndex === 0);
}

function focus(element: HTMLElement) {
  act(() => {
    element.focus();
  });
}

describe('專案的表格：一個 Tab 停駐點（docs/DESIGN.md 8.3「清單的鍵盤操作」）', () => {
  it('從快速新增按 Tab 一次進到表格（第一個資料格），再一次就到「載入更多」', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO, THREE], [FOUR]);
    focus(screen.getByRole('button', { name: '新增' }));

    // Act
    await user.tab();
    const entered = document.activeElement;
    await user.tab();

    // Assert
    // 第一格是多選的勾選框（編號前面）。
    expect(entered).toBe(within(table).getByRole('checkbox', { name: '選取 SMK-1' }));
    expect(screen.getByRole('button', { name: '載入更多' })).toHaveFocus();
    // 表格外面的捲動容器不再是停駐點：格子都停得到、走到哪捲到哪。
    expect(screen.getByRole('region', { name: '項目表格' })).not.toHaveAttribute('tabindex');
  });

  it('同一時間只有一格的 tabIndex 是 0；表格告訴報讀軟體怎麼操作', async () => {
    // Arrange / Act
    const { table } = await openTable([ONE, TWO]);

    // Assert
    await waitFor(() => {
      expect(tabStopsIn(table)).toEqual([within(table).getByRole('checkbox', { name: '選取 SMK-1' })]);
    });
    expect(table).toHaveAccessibleDescription('用方向鍵在格子之間移動，Enter 或 F2 編輯，Tab 離開表格。在表頭按 Shift 加左右方向鍵調整欄寬，Shift 加 Enter 照內容調整。');
  });

  it('唯讀與不適用的格子也停得到：上下移動不跳欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [
      agyItem({ number: 1, title: '官網需求' }),
      agyItem({ number: 2, type: 'task', title: '切版', state: 'todo' }),
    ]);
    const { user } = renderApp('/projects/AGY');
    const table = await screen.findByRole('table', { name: '項目' });
    const quote = within(table).getByRole('button', { name: '報價金額：未填' });
    focus(quote);

    // Act
    await user.keyboard('{ArrowDown}');
    const below = document.activeElement;
    await user.keyboard('{ArrowUp}');

    // Assert
    expect(below).toHaveRole('cell');
    expect(below).toHaveTextContent(/^不適用$/);
    expect(quote).toHaveFocus();
  });

  it('從第一列往上是表頭的排序按鈕，Enter 切換排序、焦點留在按鈕上', async () => {
    // Arrange
    const { user, router, table } = await openTable([ONE, TWO]);
    focus(within(table).getByRole('cell', { name: 'SMK-1' }));

    // Act
    await user.keyboard('{ArrowUp}');
    const header = document.activeElement;
    await user.keyboard('{ArrowRight}{Enter}');

    // Assert
    expect(header).toBe(within(table).getByRole('button', { name: '編號' }));
    expect(router.state.location.search).toBe('?sort=title%3Aasc');
    await waitFor(() => {
      expect(within(table).getByRole('columnheader', { name: '標題' })).toHaveAttribute('aria-sort', 'ascending');
    });
    expect(within(table).getByRole('button', { name: '標題' })).toHaveFocus();
  });

  it('F2 進入編輯，Esc 回到那一格，那一格還是停駐點', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO]);
    focus(within(rowOf(table, TWO)).getByRole('link', { name: '聯絡表單' }));

    // Act
    await user.keyboard('{ArrowRight}{F2}');
    const editor = within(rowOf(table, TWO)).getByRole('combobox', { name: '狀態' });
    const isEditorFocused = editor === document.activeElement;
    await user.keyboard('{Escape}');

    // Assert
    expect(isEditorFocused).toBe(true);
    const state = await within(rowOf(table, TWO)).findByRole('button', { name: '狀態：待辦' });
    expect(state).toHaveFocus();
    await waitFor(() => {
      expect(tabStopsIn(table)).toEqual([state]);
    });
  });

  it('勾選欄位照舊是按了就存：方向鍵走到勾選框，按空白鍵', async () => {
    // Arrange
    const { user, table } = await openTable([ONE]);
    const patches = servePatch(ONE);
    focus(within(table).getByRole('button', { name: /^預算：/ }));

    // Act
    await user.keyboard('{ArrowRight}');
    const checkbox = within(table).getByRole('checkbox', { name: '卡住了' });
    const isCheckboxFocused = checkbox === document.activeElement;
    await user.keyboard(' ');

    // Assert
    expect(isCheckboxFocused).toBe(true);
    await waitFor(() => {
      expect(checkbox).toBeChecked();
    });
    expect(patches.map((request) => request.body)).toEqual([{ fields: { blocked: true } }]);
  });
});

describe('專案的表格：面板與快捷鍵', () => {
  it('標題格按 Enter 在旁邊開項目面板；焦點回到表格的格子時 Alt+↓ 照樣換到下一個', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO]);
    [ONE, TWO].forEach((item) => {
      serveItem(detailOf(item, `${item.title}的說明`));
      serveActivity(item.key, [created()]);
    });
    focus(within(table).getByRole('link', { name: '首頁設計稿' }));

    // Act
    await user.keyboard('{Enter}');
    await screen.findByRole('complementary', { name: '項目 SMK-1' });
    const state = stateButton(table, ONE);
    focus(state);
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');

    // Assert
    expect(await screen.findByRole('complementary', { name: '項目 SMK-2' })).toBeInTheDocument();
    expect(state).toHaveFocus();
  });
});

describe('專案的表格：編號格', () => {
  it('編號是純文字、不是連結，也是和其他唯讀格一樣的停駐點；打開項目的連結在標題', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO]);
    const row = rowOf(table, ONE);

    // Act
    const keyCell = within(row).getByRole('cell', { name: 'SMK-1' });
    focus(keyCell);
    await user.keyboard('{ArrowRight}');

    // Assert
    expect(within(keyCell).queryByRole('link')).not.toBeInTheDocument();
    expect(within(row).getAllByRole('link')).toHaveLength(1);
    expect(within(row).getByRole('link', { name: '首頁設計稿' })).toHaveFocus();
  });

  it('面板開著那一筆時，aria-current 標在標題的連結上，編號格沒有', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO]);
    serveItem(detailOf(ONE, '說明'));
    serveActivity(ONE.key, [created()]);

    // Act
    await user.click(within(table).getByRole('link', { name: '首頁設計稿' }));
    await screen.findByRole('complementary', { name: '項目 SMK-1' });

    // Assert
    expect(within(table).getByRole('link', { name: '首頁設計稿' })).toHaveAttribute('aria-current', 'true');
    expect(within(table).getByRole('link', { name: '聯絡表單' })).not.toHaveAttribute('aria-current');
    expect(within(rowOf(table, ONE)).getByRole('cell', { name: 'SMK-1' })).not.toHaveAttribute('aria-current');
  });
});

describe('專案的表格：列變了之後的焦點', () => {
  it('「載入更多」之後停駐點留在原本那一格；最後一頁載完、按鈕不見時焦點到新的第一列的同一欄', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO], [THREE], [FOUR]);
    focus(stateButton(table, ONE));
    await user.tab();
    const loadMore = screen.getByRole('button', { name: '載入更多' });

    // Act：還有下一頁，按鈕留著、焦點也留在上面
    await user.keyboard('{Enter}');
    await within(table).findByRole('link', { name: '上線檢查' });

    // Assert
    expect(loadMore).toHaveFocus();
    await user.tab({ shift: true });
    expect(stateButton(table, ONE)).toHaveFocus();

    // Act：最後一頁
    await user.tab();
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(stateButton(table, FOUR)).toHaveFocus();
    });
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    expect(tabStopsIn(table)).toEqual([stateButton(table, FOUR)]);
  });

  it('「載入更多」載入中標 aria-disabled（不是 disabled），焦點留在按鈕上，再按也不重送', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    let release: () => void = () => undefined;
    const cursors: (string | null)[] = [];
    server.use(
      http.get('/v1/projects/SMK/items', async ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor');
        cursors.push(cursor);
        if (cursor !== null) {
          await new Promise<void>((resolve) => {
            release = resolve;
          });
        }
        const page: ItemPage = cursor === null ? { items: [ONE, TWO], nextCursor: 'page-1' } : { items: [THREE], nextCursor: 'page-2' };
        return ok(page);
      }),
    );
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    focus(screen.getByRole('button', { name: '載入更多' }));

    // Act
    await user.keyboard('{Enter}');
    const loading = await screen.findByRole('button', { name: '載入中…' });
    await user.keyboard('{Enter}');

    // Assert
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).not.toBeDisabled();
    expect(loading).toHaveFocus();
    await waitFor(() => {
      expect(cursors).toEqual([null, 'page-1']);
    });
    release();
    expect(await within(table).findByRole('link', { name: '上線檢查' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '載入更多' })).toHaveFocus();
  });

  it('重抓之後焦點所在的列不見了：焦點到下一列同一欄，沒有下一列就到上一列', async () => {
    // Arrange
    const { queryClient, table } = await openTable([ONE, TWO, THREE]);
    focus(stateButton(table, TWO));

    // Act
    serveItems('SMK', [ONE, THREE]);
    await act(async () => {
      await queryClient.refetchQueries({ type: 'active' });
    });

    // Assert
    await waitFor(() => {
      expect(stateButton(table, THREE)).toHaveFocus();
    });

    // Act
    serveItems('SMK', [ONE]);
    await act(async () => {
      await queryClient.refetchQueries({ type: 'active' });
    });

    // Assert
    await waitFor(() => {
      expect(stateButton(table, ONE)).toHaveFocus();
    });
  });

  it('換了搜尋條件、焦點在搜尋框：停駐點移到相鄰的一格，Tab 進表格落在那裡', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(
      http.get('/v1/projects/SMK/items', ({ request }) => {
        const query = new URL(request.url).searchParams.get('q');
        const page: ItemPage = { items: query === null ? [ONE, TWO, THREE] : [ONE, THREE], nextCursor: null };
        return ok(page);
      }),
    );
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    focus(stateButton(table, TWO));

    // Act
    await user.type(screen.getByRole('searchbox', { name: '搜尋' }), '檢查{Enter}');
    await waitFor(() => {
      expect(within(table).queryByRole('link', { name: '聯絡表單' })).not.toBeInTheDocument();
    });
    focus(screen.getByRole('button', { name: '新增' }));
    await user.tab();

    // Assert
    expect(stateButton(table, THREE)).toHaveFocus();
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveValue('檢查');
  });
});

function serveCreate(created: Item) {
  server.use(http.post('/v1/projects/SMK/items', () => ok(created, 201)));
}

describe('專案的表格：審查後補的情境', () => {
  it('停駐點所在的欄被隱藏之後，Tab 照樣進得了表格，剛好一個停駐點（同一列的前一欄）', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO]);
    focus(within(rowOf(table, TWO)).getByRole('button', { name: /^備註：/ }));

    // Act
    await user.click(screen.getByRole('button', { name: '顯示的欄位' }));
    await user.click(within(screen.getByRole('group', { name: '顯示的欄位' })).getByRole('checkbox', { name: '備註' }));
    await user.keyboard('{Escape}');
    focus(screen.getByRole('button', { name: '新增' }));
    await user.tab();

    // Assert
    const summary = within(rowOf(table, TWO)).getByRole('button', { name: /^摘要：/ });
    expect(summary).toHaveFocus();
    expect(tabStopsIn(table)).toEqual([summary]);
  });

  it('編輯中按 Tab：存好、離開表格到「載入更多」，那一格還是停駐點', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO], [THREE]);
    const patches = servePatch(ONE);
    focus(within(rowOf(table, ONE)).getByRole('button', { name: '估計：未填' }));

    // Act
    await user.keyboard('{Enter}');
    await user.type(within(rowOf(table, ONE)).getByRole('textbox', { name: '估計' }), '5');
    await user.tab();

    // Assert
    expect(screen.getByRole('button', { name: '載入更多' })).toHaveFocus();
    const title = await within(rowOf(table, ONE)).findByRole('button', { name: '估計：5' });
    await waitFor(() => {
      expect(tabStopsIn(table)).toEqual([title]);
    });
    expect(patches.map((request) => request.body)).toEqual([{ estimate: 5 }]);
    expect(screen.getByRole('button', { name: '載入更多' })).toHaveFocus();
  });

  it('焦點在不適用的格子上按空白鍵不捲動表格', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [
      agyItem({ number: 1, title: '官網需求' }),
      agyItem({ number: 2, type: 'task', title: '切版', state: 'todo' }),
    ]);
    const { user } = renderApp('/projects/AGY');
    const table = await screen.findByRole('table', { name: '項目' });
    focus(within(table).getByRole('button', { name: '報價金額：未填' }));
    await user.keyboard('{ArrowDown}');
    const notApplicable = document.activeElement;

    // Act
    const isDefault = notApplicable === null ? true : fireEvent.keyDown(notApplicable, { key: ' ' });

    // Assert
    expect(notApplicable).toHaveTextContent(/^不適用$/);
    expect(isDefault).toBe(false);
  });

  it('不能改欄位的人：表格的說明不提編輯', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });
    serveItems('SMK', [ONE]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByRole('table', { name: '項目' })).toHaveAccessibleDescription('用方向鍵在格子之間移動，Tab 離開表格。在表頭按 Shift 加左右方向鍵調整欄寬，Shift 加 Enter 照內容調整。');
  });

  it('最後一頁是空的：「載入更多」不見時焦點到最後一列的同一欄', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO], []);
    focus(stateButton(table, ONE));
    await user.tab();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(stateButton(table, TWO)).toHaveFocus();
    });
  });

  it('快速新增的項目在後面那頁又出現（去重換了位置）：焦點到最後一頁的第一列，不少算一列', async () => {
    // Arrange
    const { user, table } = await openTable([ONE, TWO], [THREE, FOUR]);
    serveCreate(THREE);
    await user.type(within(screen.getByRole('form', { name: '新增項目' })).getByRole('textbox', { name: '標題' }), '上線檢查{Enter}');
    await within(table).findByRole('link', { name: '上線檢查' });
    focus(stateButton(table, ONE));
    await user.tab();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    await within(table).findByRole('link', { name: '效能調校' });
    await waitFor(() => {
      expect(stateButton(table, THREE)).toHaveFocus();
    });
  });

  it('沒有用鍵盤按「載入更多」（點了按鈕卻沒有取得焦點，例如 Safari）：載完最後一頁不把焦點拉進表格', async () => {
    // Arrange
    const { table } = await openTable([ONE, TWO], [THREE]);
    const loadMore = screen.getByRole('button', { name: '載入更多' });

    // Act
    fireEvent.click(loadMore);

    // Assert
    await within(table).findByRole('link', { name: '上線檢查' });
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    });
    expect(document.activeElement).toBe(document.body);
  });
});
