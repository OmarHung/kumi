import { act, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BELOW_WIDE_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

/** 48rem 到 64rem 之間：不是窄螢幕（還是表格），但不到寬螢幕。 */
function stubBelowWideScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === BELOW_WIDE_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('快速新增併進表格的第一列（64rem 以上）', () => {
  it('畫在表頭下面、第一筆項目上面，但不是表格的一列（不算一筆項目、方向鍵與量欄寬都不理它）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '既有的項目' })]);

    // Act
    renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    const form = await screen.findByRole('form', { name: '新增項目' });

    // Assert
    // 不是 tr（tr 是方向鍵與量欄寬的一列）：在表格前面的一層裡，畫面上用絕對定位放在表頭下面。
    expect(form.closest('[data-quick-add="row"]')).not.toBeNull();
    expect(table.contains(form)).toBe(false);
    expect(form.compareDocumentPosition(table)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(table).toHaveAttribute('data-has-quick-add');
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    expect(within(table).getByRole('row', { name: /SMK-1/ })).toBeInTheDocument();
  });

  it('方向鍵從表頭往下走到第一筆項目，不停在快速新增那一列；裡面的輸入框仍在 Tab 順序裡', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '既有的項目' })]);
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    const selectAll = within(table).getByRole('checkbox', { name: '選取全部已載入的項目' });
    act(() => {
      selectAll.focus();
    });

    // Act
    await user.keyboard('{ArrowDown}');

    // Assert
    expect(within(table).getByRole('row', { name: /SMK-1/ })).toContainElement(document.activeElement as HTMLElement);
    expect(within(table).getAllByRole('row')[1]).toHaveAccessibleName(/SMK-1/);
    const title = screen.getByRole('textbox', { name: '標題' });
    expect(title).not.toHaveAttribute('tabindex', '-1');
  });

  it('實心的「新增」平常看不見，輸入框有字才出現；按鈕一直在（滑鼠與報讀軟體都按得到）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);
    const { user } = renderApp('/projects/SMK');
    const form = await screen.findByRole('form', { name: '新增項目' });
    const submit = within(form).getByRole('button', { name: '新增' });
    expect(submit).toHaveAttribute('data-shown', 'false');

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '修');

    // Assert
    expect(submit).toHaveAttribute('data-shown', 'true');
  });

  it('還沒有項目時照樣在表格的位置（表頭下面）出現，空白說明在它下面', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);

    // Act
    renderApp('/projects/SMK');
    const empty = await screen.findByText('這個專案還沒有項目');

    // Assert
    const form = screen.getByRole('form', { name: '新增項目' });
    expect(form.compareDocumentPosition(empty)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });
});

describe('64rem 以下（不到寬螢幕）', () => {
  it('不顯示快速新增（右下角的按鈕取代它），表格照舊，捲到底多留按鈕的高度', async () => {
    // Arrange
    stubBelowWideScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);

    // Act
    renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });

    // Assert
    expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
    expect(table.closest('section')).toHaveAttribute('data-has-fab');
  });

  it('空白說明改指向右下角的按鈕', async () => {
    // Arrange
    stubBelowWideScreen();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByText('按「新增項目」建立第一個項目。')).toBeInTheDocument();
  });
});
