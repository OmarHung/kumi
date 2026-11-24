import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { beforeEach, describe, expect, it } from 'vitest';
import type { Activity } from '../../api/types';
import { formatInstant } from '../../domain/time';
import { commented, created, manyComments, memberRef, serveActivity, showAllActivity, transitioned, updated } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner, memberAsEditor, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿', state: 'doing', stateCategory: 'in_progress' }));

async function openTimeline(history: readonly Activity[]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  const activity = serveActivity(task.key, history);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  return { ...view, section, activity };
}

function entries(section: HTMLElement): Element[] {
  return Array.from(within(section).getByRole('list', { name: '活動與留言' }).children);
}

describe('活動與留言的時間軸', () => {
  it('由舊到新列出每一筆：人、做了什麼、時間（滑過看完整時間）', async () => {
    // Arrange
    const history = [
      created({ at: '2026-11-10T01:00:00Z' }),
      updated([{ field: 'due_date', from: '2026-11-20', to: '2026-11-30' }], { actor: memberRef }),
      transitioned('todo', 'doing'),
      commented('第一行\n第二行', { actor: memberRef }),
    ];

    // Act
    const { section } = await openTimeline(history);

    // Assert
    const [first, second, third, fourth] = await within(section).findAllByRole('listitem');
    expect(first).toHaveTextContent('範例使用者 建立了這個項目');
    expect(second).toHaveTextContent('範例成員 把「到期日」從 2026/11/20 改成 2026/11/30');
    expect(third).toHaveTextContent('範例使用者 把狀態從「待辦」改成「進行中」');
    expect(fourth).toHaveTextContent('範例成員 留言');
    const time = first?.querySelector('time');
    expect(time).toHaveAttribute('dateTime', '2026-11-10T01:00:00Z');
    expect(time).toHaveAttribute('title', formatInstant('2026-11-10T01:00:00Z'));
  });

  it('留言保留換行，內容是純文字', async () => {
    // Arrange & Act
    const { section } = await openTimeline([commented('第一行\n<b>第二行</b>')]);

    // Assert
    const body = await within(section).findByText('第一行 <b>第二行</b>');
    expect(body.textContent).toBe('第一行\n<b>第二行</b>');
  });

  it('一次改了好幾個欄位時，每個欄位一行；值用名稱，長文字只說有改，查不到的人用替代文字', async () => {
    // Arrange
    const change = updated([
      { field: 'assignee', from: 'someone-gone', to: memberAsEditor.personId },
      { field: 'category', from: null, to: 'bug' },
      { field: 'notes', from: null, to: null },
      { field: 'description', from: null, to: null },
    ]);

    // Act
    const { section } = await openTimeline([change]);

    // Assert
    const [entry] = await within(section).findAllByRole('list', { name: '修改的欄位' });
    const lines = within(entry as HTMLElement).getAllByRole('listitem');
    expect(lines.map((line) => line.textContent)).toEqual([
      '把「負責人」從 查不到的人 改成 範例成員',
      '把「分類」設為 錯誤',
      '修改了「備註」',
      '修改了「說明」',
    ]);
    expect(within(section).getByText(/修改了 4 個欄位/)).toBeInTheDocument();
  });

  it('還有更新的活動時可以載入更多，接在後面', async () => {
    // Arrange
    const firstPageLast = commented('第一頁的留言');
    const { user, section, activity } = await openTimeline([
      created(),
      ...manyComments(48),
      firstPageLast,
      commented('第二頁的留言'),
    ]);
    expect(await within(section).findByText('第一頁的留言')).toBeInTheDocument();
    expect(entries(section)).toHaveLength(50);

    // Act
    await user.click(within(section).getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await within(section).findByText('第二頁的留言')).toBeInTheDocument();
    expect(entries(section)).toHaveLength(51);
    expect(entries(section).at(-1)).toHaveTextContent('第二頁的留言');
    expect(within(section).queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    expect(activity.requests.map((url) => url.search)).toEqual(['?limit=50', `?limit=50&cursor=${firstPageLast.id}`]);
  });

  it('載入中按鈕停得到（aria-disabled），焦點不掉；載完最後一頁按鈕不見時，焦點到新載入的第一筆', async () => {
    // Arrange
    const firstPage = [created(), ...manyComments(49)];
    const { user, section } = await openTimeline([...firstPage, commented('第二頁的留言')]);
    await within(section).findByText('留言 49');
    let release = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const later = commented('晚到的第二頁');
    server.use(
      http.get('/v1/items/SMK-7/activity', async ({ request }) => {
        if (new URL(request.url).searchParams.get('cursor') === null) return ok({ items: firstPage, nextCursor: firstPage.at(-1)?.id ?? null });
        await gate;
        return ok({ items: [later], nextCursor: null });
      }),
    );

    // Act
    await user.click(within(section).getByRole('button', { name: '載入更多' }));

    // Assert
    const loading = await within(section).findByRole('button', { name: '載入中…' });
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).toBeEnabled();
    expect(loading).toHaveFocus();
    await user.click(loading);
    release();
    const added = (await within(section).findByText('晚到的第二頁')).closest('li');
    await waitFor(() => {
      expect(added).toHaveFocus();
    });
    expect(within(section).queryByRole('button', { name: /載入/ })).not.toBeInTheDocument();
    expect(entries(section)).toHaveLength(51);
  });

  it('載入更多失敗時說明原因，已經載入的留著', async () => {
    // Arrange
    const { user, section } = await openTimeline([created(), ...manyComments(49), commented('第二頁')]);
    await within(section).findByText(/建立了這個項目/);
    server.use(http.get('/v1/items/SMK-7/activity', () => failure(503, 'server.not_ready', 'starting')));

    // Act
    await user.click(within(section).getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('載入更多失敗');
    expect(entries(section)).toHaveLength(50);
  });

  it('讀不到活動時顯示訊息並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(task);
    serveActivity(task.key, [created()]);
    server.use(
      http.get('/v1/items/SMK-7/activity', () => failure(500, 'server.error', 'boom'), { once: true }),
    );
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    const section = await screen.findByRole('region', { name: '活動與留言' });

    // Act
    const alert = await within(section).findByRole('alert');
    expect(alert).toHaveTextContent('活動載入失敗');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(await within(section).findByText(/建立了這個項目/)).toBeInTheDocument();
  });

  it('重抓時多了別人的留言，報讀簡短的摘要（留言只唸開頭）', async () => {
    // Arrange
    const history = [created()];
    const { section, activity, queryClient } = await openTimeline(history);
    await within(section).findByText(/建立了這個項目/);
    const longComment = `${'請'.repeat(45)}看一下`;
    activity.set([...history, commented(longComment, { actor: memberRef })]);

    // Act
    await queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7', 'activity'] });

    // Assert
    expect(await within(section).findByText(longComment)).toBeInTheDocument();
    expect(within(section).getAllByRole('status').at(-1)).toHaveTextContent(`範例成員 留言：${'請'.repeat(40)}…`);
  });

  it('沒有任何活動時說明', async () => {
    // Arrange & Act
    const { section } = await openTimeline([]);

    // Assert
    expect(await within(section).findByText('還沒有任何活動。')).toBeInTheDocument();
  });
});
