import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { serveCrossProjectItems } from '../../test/crossProjectItems';
import { daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveTransition } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const CHANGED_AT = '2026-11-16T09:00:00Z';

const first = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });
const second = smkItem({ number: 2, title: '整理素材', priority: 'high' });

function done(item: Item): Item {
  return { ...item, state: 'done', stateCategory: 'done', updatedAt: CHANGED_AT };
}

async function completeFirst(user: ReturnType<typeof renderApp>['user']) {
  const row = await screen.findByRole('row', { name: /SMK-1/ });
  await user.click(await within(row).findByRole('button', { name: '狀態：待辦' }));
  await chooseOption(user, within(row).getByRole('combobox', { name: '狀態' }), '完成');
  return row;
}

describe('我的工作的表格：載入更多', () => {
  it('一頁讀不完時最下面有「載入更多」，按了接上下一頁；載完它就不見', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [first, second] });
    const requests: URL[] = [];
    server.use(
      http.get('/v1/items', ({ request }) => {
        const url = new URL(request.url);
        requests.push(url);
        return url.searchParams.get('cursor') === 'next'
          ? ok({ items: [second], nextCursor: null })
          : ok({ items: [first], nextCursor: 'next' });
      }),
    );
    const { user } = renderApp('/my-work');
    expect(await screen.findByText('已載入 1 個項目')).toBeInTheDocument();

    // Act
    await user.click(await screen.findByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByRole('row', { name: /SMK-2/ })).toBeInTheDocument();
    expect(screen.getByRole('row', { name: /SMK-1/ })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    });
    expect(screen.getByText('已載入 2 個項目')).toBeInTheDocument();
    expect(requests[0]?.searchParams.get('limit')).toBe('200');
    expect(requests[0]?.searchParams.get('sort')).toBe('due_date:asc');
    expect(requests[1]?.searchParams.get('cursor')).toBe('next');
  });
});

describe('我的工作的表格：條件與清單', () => {
  it('把條件送給跨專案的清單：預設是我負責、還沒結束的', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveCrossProjectItems([first], exampleOwner.personId);

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('row', { name: /SMK-1/ });
    const params = requests[0]?.searchParams;
    expect(params?.get('assignee')).toBe('me');
    expect(params?.getAll('stateCategory')).toEqual(['backlog', 'not_started', 'in_progress']);
  });

  it('網址上的 assignee 不讀：一律送自己，其他條件照送', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveCrossProjectItems([first], exampleOwner.personId);

    // Act
    renderApp('/my-work?assignee=all&category=all');

    // Assert
    await screen.findByRole('row', { name: /SMK-1/ });
    expect(requests[0]?.searchParams.get('assignee')).toBe('me');
    expect(requests[0]?.searchParams.has('stateCategory')).toBe(false);
  });

  it('一定沒有結果的條件（已完成加已過期）不送請求，說沒有符合條件的項目', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveCrossProjectItems([first], exampleOwner.personId);

    // Act
    renderApp('/my-work?category=done&due=overdue');

    // Assert
    expect(await screen.findByText('沒有符合條件的項目')).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });

  it('第一次就讀不到：換成錯誤、不放工具列；重試之後工具列與清單出現', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [first] });
    server.use(
      http.get('/v1/items', () => new Response(JSON.stringify({ code: 'server.internal_error', message: 'boom' }), { status: 500 }), { once: true }),
    );
    const { user } = renderApp('/my-work');

    // Act
    const alert = await screen.findByRole('alert');
    expect(screen.queryByRole('group', { name: '篩選項目' })).not.toBeInTheDocument();
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('row', { name: /SMK-1/ })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: '篩選項目' })).toBeInTheDocument();
  });
});

describe('我的工作的表格：換條件重新開始記下的列', () => {
  it('剛完成的列留在原位；換了條件之後原本的列不再留著', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [first, second] });
    serveTransition(first, done(first));
    const { user } = renderApp('/my-work');
    const row = await completeFirst(user);
    await waitFor(() => {
      expect(row).toHaveAttribute('data-settled', 'true');
    });

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '優先度' }), '高');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('row', { name: /SMK-1/ })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('row', { name: /SMK-2/ })).toBeInTheDocument();
  });

  it('條件是「所有狀態」：改成完成的列還符合條件，不變淡也不說會離開清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [first, second] });
    serveTransition(first, done(first));
    const { user } = renderApp('/my-work?category=all');

    // Act
    const row = await completeFirst(user);

    // Assert
    expect(await within(row).findByRole('button', { name: '狀態：完成' })).toBeInTheDocument();
    expect(row).not.toHaveAttribute('data-settled');
    expect(screen.queryByText(/重新整理後會離開清單/)).not.toBeInTheDocument();
  });

  it('條件是「未開始」：改成完成的列不再符合，變淡並說會離開清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], myWork: [first, second] });
    serveTransition(first, done(first));
    const { user } = renderApp('/my-work?category=not_started');

    // Act
    const row = await completeFirst(user);

    // Assert
    expect(await screen.findByText('SMK-1 已經完成，重新整理後會離開清單。')).toHaveAttribute('role', 'status');
    expect(row).toHaveAttribute('data-settled', 'true');
  });
});
