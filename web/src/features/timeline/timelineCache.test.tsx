import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, smkItem } from '../../test/fixtures';
import { detailOf } from '../../test/items';
import { server } from '../../test/server';
import { renderTimeline } from '../../test/timeline';
import { dateViewFiltersFromSearchParams } from '../../domain/projectView';
import { replaceItemInCache } from '../item-table/itemCache';

/** 假的清單：沒有到期日的給「沒有日期」的請求，其餘給畫面這一段的請求。伺服器上的項目放在 all 裡，測試可以直接換掉。 */
function serveFrom(all: Item[]) {
  server.use(
    http.get('/v1/projects/SMK/items', ({ request }) => {
      const isUndated = new URL(request.url).searchParams.get('noDue') === 'true';
      const items = all.filter((item) => (isUndated ? item.dueDate === null : item.dueDate !== null || item.startDate !== null));
      return ok({ items, nextCursor: null });
    }),
  );
}

describe('面板裡改了日期之後的時間軸', () => {
  it('補上開始日與到期日：項目出現在畫面裡，「沒有日期」的數字跟著少一個', async () => {
    // Arrange
    const all: Item[] = [smkItem({ number: 1, title: '補日期的' }), smkItem({ number: 2, title: '還是沒有' })];
    signedInAs(exampleOwner);
    serveFrom(all);
    const { queryClient } = renderTimeline();
    await screen.findByRole('link', { name: '2 個項目沒有開始日也沒有到期日' });

    // Act
    const edited = { ...(all[0] as Item), startDate: '2026-11-20', dueDate: '2026-11-25' };
    all[0] = edited;
    replaceItemInCache(queryClient, detailOf(edited));

    // Assert
    const list = await screen.findByRole('list', { name: '項目' });
    expect(await within(list).findByRole('link', { name: /補日期的/ })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: '1 個項目沒有開始日也沒有到期日' })).toBeInTheDocument();
  });
});

describe('「沒有日期」連到表格', () => {
  it('帶著類型與狀態', async () => {
    // Arrange
    const all: Item[] = [smkItem({ number: 1 })];
    signedInAs(exampleOwner);
    serveFrom(all);
    const filters = dateViewFiltersFromSearchParams(new URLSearchParams('view=timeline&type=task&state=doing'));

    // Act
    renderTimeline({ path: '/projects/SMK?view=timeline&type=task&state=doing', filters });

    // Assert
    const link = await screen.findByRole('link', { name: '1 個項目沒有開始日也沒有到期日' });
    expect(link).toHaveAttribute('href', '/projects/SMK?type=task&state=doing&due=none');
  });
});
