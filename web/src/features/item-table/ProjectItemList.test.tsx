import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatDate } from '../../domain/dates';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
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

describe('窄螢幕的專案清單：連結的名稱', () => {
  it('每一個欄位都說出是什麼（狀態、類型、負責人、到期），不是只唸一串值', async () => {
    // Arrange
    stubCompactScreen();
    const due = daysFromToday(5);
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '首頁設計稿', dueDate: due }), smkItem({ number: 2, title: '切版', assignee: null })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    const list = await screen.findByRole('list', { name: '項目' });
    const [first, second] = within(list).getAllByRole('link');
    expect(first).toHaveAccessibleName(`SMK-1 首頁設計稿 狀態：待辦 類型：任務 負責人：範例使用者 到期：${formatDate(due)}`);
    expect(second).toHaveAccessibleName('SMK-2 切版 狀態：待辦 類型：任務 沒有負責人');
    expect(within(list).getAllByRole('listitem')[0]).toHaveAttribute('data-content-start');
  });
});
