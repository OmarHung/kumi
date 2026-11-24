import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetailWithoutFieldFilters, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { stubResizeObserver } from '../../test/resizeObserver';

afterEach(() => {
  vi.unstubAllGlobals();
});

const BAR = '[role=group][aria-label="篩選項目"]';
const BOARD_BAR = '[role=group][aria-label="篩選看板"]';

async function openTable(path = '/projects/AGY') {
  signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
  serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const resize = stubResizeObserver();
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  return { ...view, resize, bar: () => screen.getByRole('group', { name: '篩選項目' }) };
}

describe('工具列放不下時，條件依序收進「更多條件」（表格）', () => {
  it('量不到寬度（沒有 ResizeObserver）時照舊排在列上', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    renderApp('/projects/AGY');
    await screen.findByRole('table', { name: '項目' });

    // Act
    const bar = screen.getByRole('group', { name: '篩選項目' });

    // Assert
    expect(within(bar).getByRole('combobox', { name: '到期' })).toBeInTheDocument();
    expect(within(bar).getByRole('combobox', { name: '優先度' })).toBeInTheDocument();
    expect(within(bar).queryByRole('button', { name: '更多條件' })).not.toBeInTheDocument();
  });

  it('列夠寬時到期與優先度直接排在列上', async () => {
    // Arrange
    const { resize, bar } = await openTable();

    // Act
    resize.report(BAR, 1100);

    // Assert
    expect(within(bar()).getByRole('combobox', { name: '到期' })).toBeInTheDocument();
    expect(within(bar()).queryByRole('button', { name: '更多條件' })).not.toBeInTheDocument();
  });

  it('列剛好放得下全部欄位與預留的「清除條件」時，不預留「更多條件」的位置：它沒有出現', async () => {
    // Arrange：AGY 有好幾種類型，列上是六個欄位加「清除條件」= 896px；950px 放得下，沒有東西要收。
    const { resize, bar } = await openTable();

    // Act
    resize.report(BAR, 950);

    // Assert
    expect(within(bar()).getByRole('combobox', { name: '優先度' })).toBeInTheDocument();
    expect(within(bar()).queryByRole('button', { name: '更多條件' })).not.toBeInTheDocument();
  });

  it('差一點放不下時先收優先度，再放不下才收到期；類型、狀態、負責人留在列上', async () => {
    // Arrange：收掉優先度之後，五個欄位加「清除條件」與「更多條件」要 872px。
    const { resize, bar } = await openTable();

    // Act / Assert
    resize.report(BAR, 880);
    expect(within(bar()).queryByRole('combobox', { name: '優先度' })).not.toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '到期' })).toBeInTheDocument();
    expect(within(bar()).getByRole('button', { name: '更多條件' })).toBeInTheDocument();
    resize.report(BAR, 800);
    expect(within(bar()).queryByRole('combobox', { name: '到期' })).not.toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '類型' })).toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '狀態' })).toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '負責人' })).toBeInTheDocument();
  });

  it('欄位剛好排得下、但再出現「清除條件」就排不下時先收起來：套用第一個條件時整列不會擠成兩列', async () => {
    // Arrange：AGY 有好幾種類型，列上是六個欄位（6 × 8rem + 5 × 0.5rem = 808px），再加「清除條件」要 896px。
    const { resize, bar } = await openTable();

    // Act
    resize.report(BAR, 850);

    // Assert
    expect(within(bar()).getByRole('button', { name: '更多條件' })).toBeInTheDocument();
    expect(within(bar()).queryByRole('combobox', { name: '到期' })).not.toBeInTheDocument();
  });

  it('列放不下時，收起來的條件在「更多條件」的浮層裡，照原本列上的順序；搜尋留在列上', async () => {
    // Arrange：500px 只放得下搜尋與負責人（加「清除條件」與「更多條件」要 464px）。
    const { user, resize, bar } = await openTable();

    // Act
    resize.report(BAR, 500);

    // Assert
    expect(within(bar()).getByRole('searchbox', { name: '搜尋' })).toBeInTheDocument();
    expect(within(bar()).getByRole('combobox', { name: '負責人' })).toBeInTheDocument();
    expect(within(bar()).queryByRole('combobox', { name: '到期' })).not.toBeInTheDocument();
    await user.click(within(bar()).getByRole('button', { name: '更多條件' }));
    const popover = await screen.findByRole('dialog', { name: '更多條件' });
    expect(within(popover).getAllByRole('combobox')).toHaveLength(4);
    expect(within(popover).getByRole('combobox', { name: '類型' })).toBeInTheDocument();
    expect(within(popover).getByRole('combobox', { name: '狀態' })).toBeInTheDocument();
    expect(within(popover).getByRole('combobox', { name: '到期' })).toBeInTheDocument();
    expect(within(popover).getByRole('combobox', { name: '優先度' })).toBeInTheDocument();
    expect(within(popover).queryByRole('combobox', { name: '負責人' })).not.toBeInTheDocument();
  });

  it('窄到連負責人也放不下時，只剩搜尋在列上，其餘全在浮層裡，順序是類型、狀態、負責人、到期、優先度', async () => {
    // Arrange
    const { user, resize, bar } = await openTable();

    // Act
    resize.report(BAR, 250);

    // Assert
    expect(within(bar()).queryByRole('combobox')).not.toBeInTheDocument();
    await user.click(within(bar()).getByRole('button', { name: '更多條件' }));
    const popover = await screen.findByRole('dialog', { name: '更多條件' });
    const names = within(popover)
      .getAllByRole('combobox')
      .map((box) => box.getAttribute('aria-labelledby'))
      .map((id) => (id === null ? '' : (document.getElementById(id)?.textContent ?? '')));
    expect(names).toEqual(['類型', '狀態', '負責人', '到期', '優先度']);
  });

  it('用了其中幾個條件，按鈕上顯示數字，完整的說法在 aria-describedby', async () => {
    // Arrange
    const { resize, bar } = await openTable('/projects/AGY?due=week&priority=high');

    // Act
    resize.report(BAR, 500);

    // Assert
    const button = within(bar()).getByRole('button', { name: '更多條件' });
    expect(button).toHaveTextContent('2');
    expect(button).toHaveAccessibleDescription('目前有 2 個條件');
  });

  it('按鈕上的數字只算浮層裡有值的條件：留在列上的負責人不算', async () => {
    // Arrange
    const { resize, bar } = await openTable('/projects/AGY?due=week&assignee=me');

    // Act
    resize.report(BAR, 500);

    // Assert
    const button = within(bar()).getByRole('button', { name: '更多條件' });
    expect(button).toHaveAccessibleDescription('目前有 1 個條件');
  });

  it('寬度變回夠寬，兩個下拉回到列上（條件不變）', async () => {
    // Arrange
    const { resize, bar } = await openTable('/projects/AGY?due=week');
    resize.report(BAR, 500);
    expect(within(bar()).queryByRole('combobox', { name: '到期' })).not.toBeInTheDocument();

    // Act
    resize.report(BAR, 1200);

    // Assert
    expect(within(bar()).getByRole('combobox', { name: '到期' })).toHaveTextContent('本週到期');
  });
});

describe('工具列放不下時（看板）', () => {
  it('同樣收進「更多條件」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetailWithoutFieldFilters()] });
    serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);
    const resize = stubResizeObserver();
    const { user } = renderApp('/projects/AGY?view=board&priority=high');
    await screen.findByRole('region', { name: '看板' });
    const bar = () => screen.getByRole('group', { name: '篩選看板' });

    // Act
    resize.report(BOARD_BAR, 300);

    // Assert
    expect(within(bar()).queryByRole('combobox', { name: '優先度' })).not.toBeInTheDocument();
    const button = within(bar()).getByRole('button', { name: '更多條件' });
    expect(button).toHaveAccessibleDescription('目前有 1 個條件');
    await user.click(button);
    expect(within(await screen.findByRole('dialog', { name: '更多條件' })).getByRole('combobox', { name: '優先度' })).toHaveTextContent('高');
    resize.report(BOARD_BAR, 1200);
    expect(within(bar()).getByRole('combobox', { name: '優先度' })).toBeInTheDocument();
  });
});
