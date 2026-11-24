import { screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { Activity } from '../../api/types';
import { created, migrated, serveActivity, showAllActivity, transitioned, typeChanged } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿', state: 'doing', stateCategory: 'in_progress' }));

async function openTimeline(history: readonly Activity[]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  serveActivity(task.key, history);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  return { ...view, section };
}

describe('時間軸：改過類型的項目', () => {
  it('改類型之前的改狀態與搬移用當時的類型的流程找狀態名稱，之後的用新類型的', async () => {
    // Arrange：現在是任務（任務流程），之前是需求（交付流程）。
    const task = detailOf(agyItem({ number: 4, type: 'task', state: 'doing', stateCategory: 'in_progress' }));
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItem(task);
    serveActivity(task.key, [
      created(),
      transitioned('confirming', 'quoting'),
      migrated(2, { fromState: 'quoting', toState: 'designing' }),
      typeChanged('requirement', 'task', [], { fromState: 'designing', toState: 'doing' }),
      transitioned('doing', 'done'),
    ]);
    renderApp('/projects/AGY/items/AGY-4');

    // Act
    const section = await screen.findByRole('region', { name: '活動與留言' });
    const entries = await within(section).findAllByRole('listitem');

    // Assert
    expect(entries[1]).toHaveTextContent('把狀態從「需求確認」改成「報價」');
    expect(entries[2]).toHaveTextContent('狀態從「報價」搬到「設計」');
    expect(entries[4]).toHaveTextContent('把狀態從「進行中」改成「完成」');
  });
});

describe('時間軸：套用設定變更時的搬移', () => {
  it('狀態搬移說出藍圖的版本與前後的狀態；被刪除的狀態已經不在藍圖裡，用它的 key', async () => {
    // Arrange & Act
    const { section } = await openTimeline([migrated(4, { fromState: 'review', toState: 'doing' })]);

    // Assert
    const [entry] = await within(section).findAllByRole('listitem');
    expect(entry).toHaveTextContent('範例使用者 套用設定變更（第 4 版）時，狀態從「review」搬到「進行中」');
  });

  it('欄位被刪除時說它被封存，沿用表格的值轉文字', async () => {
    // Arrange & Act
    const { section } = await openTimeline([
      migrated(5, { changes: [{ field: 'story_points', from: 1234.5, to: null }] }),
    ]);

    // Assert
    const [entry] = await within(section).findAllByRole('listitem');
    expect(entry).toHaveTextContent('套用設定變更（第 5 版）時，「點數」被封存（原本是 1,234.5）');
  });

  it('欄位換了型別時說從什麼轉成什麼', async () => {
    // Arrange & Act
    const { section } = await openTimeline([
      migrated(6, { changes: [{ field: 'summary', from: '42', to: 42 }] }),
    ]);

    // Assert
    const [entry] = await within(section).findAllByRole('listitem');
    expect(entry).toHaveTextContent('套用設定變更（第 6 版）時，「摘要」從 42 轉成 42');
  });

  it('同時搬了狀態又封存了欄位時，每一件事一行', async () => {
    // Arrange & Act
    const { section } = await openTimeline([
      migrated(7, {
        fromState: 'review',
        toState: 'done',
        changes: [
          { field: 'notes', from: null, to: null },
          { field: 'category', from: 'bug', to: null },
        ],
      }),
    ]);

    // Assert
    const [entry] = await within(section).findAllByRole('listitem', { name: undefined });
    expect(entry).toHaveTextContent('套用設定變更（第 7 版）時：');
    const lines = within(entry as HTMLElement).getByRole('list', { name: '搬移的內容' });
    expect(within(lines).getAllByRole('listitem').map((line) => line.textContent)).toEqual([
      '狀態從「review」搬到「完成」',
      '「備註」被封存',
      '「分類」被封存（原本是 錯誤）',
    ]);
  });
});
