import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatDate } from '../../domain/dates';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('窄螢幕的我的工作：連結的名稱', () => {
  it('每一個欄位都說出是什麼（狀態、類型、專案、優先度、到期）；沒有設的不唸，優先度只有高與緊急才有', async () => {
    // Arrange
    stubCompactScreen();
    const due = daysFromToday(4);
    const urgent = makeItem({ number: 3, title: '首頁改版需求', priority: 'high', dueDate: due });
    const plain = makeItem({ number: 4, title: '登入流程' });
    signedInAs(exampleOwner, { projects: [webProject], myWork: [urgent, plain] });

    // Act
    renderApp('/my-work');

    // Assert
    const list = await screen.findByRole('list', { name: /^(這一週|之後)$/ });
    await within(list).findAllByText('官網改版');
    const first = within(list).getByRole('link');
    const second = within(screen.getByRole('list', { name: '沒有到期日' })).getByRole('link');
    expect(first).toHaveAccessibleName(`WEB-3 首頁改版需求 狀態：待辦 類型：任務 優先度：高 專案：官網改版 到期：${formatDate(due)}`);
    expect(second).toHaveAccessibleName('WEB-4 登入流程 狀態：待辦 類型：任務 專案：官網改版');
    expect(within(list).getAllByRole('listitem')[0]).toHaveAttribute('data-content-start');
  });
});
