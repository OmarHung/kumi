import { screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { Activity } from '../../api/types';
import { created, deleted, restored, serveActivity, showAllActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const requirement = detailOf(agyItem({ number: 4, title: '官網需求' }));

async function openTimeline(history: readonly Activity[]) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveItem(requirement);
  serveActivity(requirement.key, history);
  renderApp('/projects/AGY/items/AGY-4');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  return within(section).findAllByRole('listitem');
}

describe('時間軸：刪除與復原', () => {
  it('復原過的項目，時間軸上有「刪除了這個項目」與「復原了這個項目」兩筆', async () => {
    // Arrange & Act
    const entries = await openTimeline([created(), deleted(), restored()]);

    // Assert
    expect(entries[1]).toHaveTextContent('範例使用者 刪除了這個項目');
    expect(entries[2]).toHaveTextContent('範例使用者 復原了這個項目');
  });

  it('復原時有搬移：狀態、封存的欄位值與拿掉的上層都說出來', async () => {
    // Arrange & Act
    const entries = await openTimeline([
      created(),
      restored(
        [
          { field: 'quote_amount', from: 1200, to: null },
          { field: 'parent', from: 'AGY-3', to: null },
        ],
        { fromState: 'review', toState: 'confirming' },
      ),
    ]);

    // Assert
    expect(entries[1]).toHaveTextContent(
      '復原了這個項目，狀態從「review」搬到「需求確認」；「報價金額」（原本是 $1,200）的值封存起來；拿掉上層 AGY-3',
    );
  });
});
