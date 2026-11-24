import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

afterEach(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
});

describe('表格的長相（docs/DESIGN.md 7.1）', () => {
  it('排序的方向用圖示，按鈕的名稱還是欄位名稱', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);

    // Act
    renderApp('/projects/SMK?sort=due_date:desc');

    // Assert
    const table = await screen.findByRole('table', { name: '項目' });
    const due = within(within(table).getByRole('columnheader', { name: '到期日' })).getByRole('button', { name: '到期日' });
    expect(due.lastElementChild).toHaveAttribute('data-icon', 'sortDesc');
    expect(due).toHaveTextContent(/^到期日$/);
  });

  it('不適用的格子不畫符號，只留給報讀軟體的「不適用」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [agyItem({ number: 2, type: 'task', title: '切版', state: 'todo' })]);

    // Act
    renderApp('/projects/AGY');

    // Assert
    const table = await screen.findByRole('table', { name: '項目' });
    const row = within(table).getByRole('row', { name: /AGY-2/ });
    const notApplicable = within(row)
      .getAllByRole('cell')
      .filter((cell) => cell.textContent.includes('不適用'));
    expect(notApplicable).toHaveLength(4);
    notApplicable.forEach((cell) => {
      expect(cell).toHaveTextContent(/^不適用$/);
    });
  });

  it('標題在表格裡是一行的連結，完整的文字放在 title 與連結的名稱裡', async () => {
    // Arrange
    const title = '很長很長的標題，在表格裡只顯示一行，滑過去或進項目頁才看得到全部';
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    const table = await screen.findByRole('table', { name: '項目' });
    const link = within(table).getByRole('link', { name: title });
    expect(link).toHaveAttribute('title', title);
  });

  it('勾選欄位的點擊範圍是包住勾選框的標籤，名稱不變', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, fields: { blocked: true } })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    const table = await screen.findByRole('table', { name: '項目' });
    const checkbox = within(table).getByRole('checkbox', { name: '卡住了' });
    expect(checkbox.parentElement?.tagName).toBe('LABEL');
  });

  it('格子取得焦點時捲到看得到的地方（讓出黏住的表頭與固定的兩欄，不躲在底下）', async () => {
    // Arrange：jsdom 沒有 scrollIntoView，換成記錄呼叫的假函式
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);
    const { user } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    const row = within(table).getByRole('row', { name: /SMK-1/ });
    within(row).getByRole('link', { name: '首頁設計稿' }).focus();

    // Act
    await user.keyboard('{ArrowRight}');

    // Assert
    const state = within(row).getByRole('button', { name: '狀態：待辦' });
    expect(state).toHaveFocus();
    expect(scrollIntoView).toHaveBeenLastCalledWith({ block: 'nearest', inline: 'nearest' });
    expect(scrollIntoView.mock.contexts.at(-1)).toBe(state.closest('td'));
  });
});
