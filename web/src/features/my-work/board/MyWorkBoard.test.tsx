import { act, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ItemPage } from '../../../api/types';
import { DEFAULT_MY_WORK_FILTERS, MY_WORK_PAGE_SIZE } from '../../../domain/myWorkView';
import { ok, signedInAs } from '../../../test/api';
import { serveCrossProjectItems } from '../../../test/crossProjectItems';
import { agyDetail, agyItem, daysFromToday, exampleOwner, smkDetail, smkItem } from '../../../test/fixtures';
import { server } from '../../../test/server';
import { removeItemFromCache } from '../../item-table/itemCache';
import { renderMyWorkBoard } from './renderMyWorkBoard';

const mine = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };

const quoting = agyItem({ number: 1, title: '報價單', state: 'quoting', stateCategory: 'not_started', assignee: mine, dueDate: daysFromToday(5) });
const clientQa = agyItem({ number: 4, title: '驗收中的需求', state: 'client_qa', stateCategory: 'in_progress', assignee: mine });
const todo = smkItem({ number: 3, title: '首頁設計稿', assignee: mine, dueDate: daysFromToday(2) });
const doing = smkItem({ number: 2, title: '串接金流', state: 'doing', stateCategory: 'in_progress', assignee: mine });
const finished = smkItem({ number: 9, title: '已經做完的', state: 'done', stateCategory: 'done', assignee: mine });
const backlogged = smkItem({ number: 7, title: '之後再說', state: 'backlog', stateCategory: 'backlog', assignee: mine });

function column(name: string): HTMLElement {
  return screen.getByRole('region', { name });
}

function cardKeysIn(name: string): (string | null)[] {
  return within(column(name))
    .queryAllByRole('article')
    .map((card) => card.getAttribute('data-card'));
}

async function boardLoaded() {
  await screen.findByRole('article', { name: '報價單' });
}

describe('欄', () => {
  it('預設的條件：待規劃、未開始、進行中，加上放下去的目標「完成」；完成那一欄一開始是空的', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [quoting, clientQa, todo, doing] });

    // Act
    renderMyWorkBoard();
    await boardLoaded();

    // Assert
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['待規劃', '未開始', '進行中', '完成']);
    expect(within(column('完成')).getByText('把卡片放到這裡，就是完成了。')).toBeInTheDocument();
  });

  it('選了專案：那個專案的流程沒有的類別不畫空欄（簡單任務沒有「待規劃」），「完成」照舊是放下去的目標', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [todo, doing] });

    // Act
    renderMyWorkBoard({ ...DEFAULT_MY_WORK_FILTERS, project: 'SMK' });
    await screen.findByRole('article', { name: '首頁設計稿' });

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('region', { name: '待規劃' })).not.toBeInTheDocument();
    });
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['未開始', '進行中', '完成']);
  });

  it('跨專案的卡片依狀態類別分欄；欄內照到期日、沒有到期日的最後', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [quoting, clientQa, todo, doing, backlogged] });

    // Act
    renderMyWorkBoard();
    await boardLoaded();

    // Assert
    expect(cardKeysIn('待規劃')).toEqual(['SMK-7']);
    expect(cardKeysIn('未開始')).toEqual(['SMK-3', 'AGY-1']);
    expect(cardKeysIn('進行中')).toEqual(['AGY-4', 'SMK-2']);
    expect(cardKeysIn('完成')).toEqual([]);
  });

  it('卡片寫專案與狀態的名稱（那張卡片自己專案的藍圖）；卡片上不寫負責人（這一頁都是我負責的）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [clientQa, doing] });
    renderMyWorkBoard();

    // Act
    const agyCard = await screen.findByRole('article', { name: '驗收中的需求' });
    const smkCard = await screen.findByRole('article', { name: '串接金流' });

    // Assert
    await waitFor(() => {
      expect(within(agyCard).getByText('客戶驗收')).toBeInTheDocument();
    });
    expect(within(agyCard).getByText('接案測試')).toBeInTheDocument();
    expect(within(smkCard).getByText('冒煙測試')).toBeInTheDocument();
    expect(within(smkCard).getByText('進行中')).toBeInTheDocument();
    expect(within(agyCard).queryByText(exampleOwner.displayName)).not.toBeInTheDocument();
    expect(within(smkCard).queryByText(exampleOwner.displayName)).not.toBeInTheDocument();
  });

  it('所有狀態是五欄；一個類別是那一欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [quoting, finished] });
    const { setFilters } = renderMyWorkBoard({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'all' } });
    await boardLoaded();

    // Act / Assert
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual([
      '待規劃',
      '未開始',
      '進行中',
      '完成',
      '已取消',
    ]);
    expect(cardKeysIn('完成')).toEqual(['SMK-9']);
    act(() => {
      setFilters({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'category', category: 'done' } });
    });
    await screen.findByRole('article', { name: '已經做完的' });
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['完成']);
  });

  it('選了專案與一個狀態：那個狀態的類別那一欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [quoting, clientQa] });

    // Act
    renderMyWorkBoard({ ...DEFAULT_MY_WORK_FILTERS, project: 'AGY', status: { kind: 'state', state: 'client_qa' } });
    await screen.findByRole('article', { name: '驗收中的需求' });

    // Assert
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['進行中']);
    expect(cardKeysIn('進行中')).toEqual(['AGY-4']);
  });

  it('有條件而沒有符合的項目時，每一欄寫「沒有符合條件的項目」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], items: [] });

    // Act
    renderMyWorkBoard({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'category', category: 'in_progress' }, q: '不存在' });

    // Assert
    expect(await within(column('進行中')).findByText('沒有符合條件的項目')).toBeInTheDocument();
  });
});

describe('送出的條件', () => {
  it('一次查一份跨專案的清單：條件、到期日遞增、一頁 200 個', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [quoting] });
    const requests = serveCrossProjectItems([quoting], exampleOwner.personId);

    // Act
    renderMyWorkBoard();
    await boardLoaded();

    // Assert
    expect(requests).toHaveLength(1);
    const params = requests[0]?.searchParams;
    expect(params?.getAll('stateCategory')).toEqual(['backlog', 'not_started', 'in_progress']);
    expect(params?.get('assignee')).toBe('me');
    expect(params?.get('sort')).toBe('due_date:asc');
    expect(params?.get('limit')).toBe(String(MY_WORK_PAGE_SIZE));
  });

  it('條件換了就重新讀，欄跟著換', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [quoting, finished] });
    const requests = serveCrossProjectItems([quoting, finished], exampleOwner.personId);
    const { setFilters } = renderMyWorkBoard();
    await boardLoaded();

    // Act
    act(() => {
      setFilters({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'category', category: 'done' } });
    });

    // Assert
    await screen.findByRole('article', { name: '已經做完的' });
    expect(requests.at(-1)?.searchParams.getAll('stateCategory')).toEqual(['done']);
    expect(requests.at(-1)?.searchParams.get('assignee')).toBe('me');
  });

  it('條件一定是空的（完成的已過期）時不送請求，欄是空的', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], items: [quoting] });
    const requests = serveCrossProjectItems([quoting], exampleOwner.personId);

    // Act
    renderMyWorkBoard({ ...DEFAULT_MY_WORK_FILTERS, status: { kind: 'category', category: 'done' }, due: 'overdue' });

    // Assert
    expect(await within(column('完成')).findByText('沒有符合條件的項目')).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });

  it('第一次就讀不到：說明加「重試」，重試之後有卡片', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], items: [quoting] });
    let calls = 0;
    server.use(
      http.get('/v1/items', () => {
        calls += 1;
        return calls === 1
          ? new Response(JSON.stringify({ code: 'server.error', message: '伺服器出了問題。', details: [] }), { status: 500 })
          : ok({ items: [quoting], nextCursor: null });
      }),
    );
    const { user } = renderMyWorkBoard();

    // Act
    await user.click(await screen.findByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('article', { name: '報價單' })).toBeInTheDocument();
  });
});

describe('載入更多', () => {
  it('還有更多時欄的數量寫「n+」，看板最下面有「載入更多」；載完重新分欄、按鈕不見', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [] });
    const pages: Record<string, ItemPage> = {
      first: { items: [quoting, todo], nextCursor: 'next' },
      next: { items: [doing], nextCursor: null },
    };
    server.use(
      http.get('/v1/items', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor');
        return ok(cursor === null ? pages.first : pages.next);
      }),
    );
    const { user } = renderMyWorkBoard();
    await boardLoaded();
    expect(within(column('未開始')).getByText('2+')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByRole('article', { name: '串接金流' })).toBeInTheDocument();
    expect(cardKeysIn('進行中')).toEqual(['SMK-2']);
    expect(within(column('未開始')).getByText('2')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
  });

  it('用鍵盤按「載入更多」：載入中按鈕用 aria-disabled、焦點留著；載完最後一頁、按鈕不見，焦點到最後一頁的第一張卡片', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [] });
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/items', async ({ request }) => {
        if (new URL(request.url).searchParams.get('cursor') === null) return ok({ items: [quoting, todo], nextCursor: 'next' });
        await gate;
        return ok({ items: [doing], nextCursor: null });
      }),
    );
    const { user } = renderMyWorkBoard();
    await boardLoaded();
    const button = screen.getByRole('button', { name: '載入更多' });
    button.focus();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    const loading = await screen.findByRole('button', { name: '載入中…' });
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).not.toBeDisabled();
    expect(loading).toHaveFocus();
    release();
    await screen.findByRole('article', { name: '串接金流' });
    await waitFor(() => {
      expect(screen.getByRole('link', { name: '串接金流' })).toHaveFocus();
    });
  });

  it('載入更多失敗：說明，已經載入的卡片還在', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], items: [] });
    server.use(
      http.get('/v1/items', ({ request }) =>
        new URL(request.url).searchParams.get('cursor') === null
          ? ok({ items: [quoting], nextCursor: 'next' })
          : new Response(JSON.stringify({ code: 'server.error', message: '伺服器出了問題。', details: [] }), { status: 500 }),
      ),
    );
    const { user } = renderMyWorkBoard();
    await boardLoaded();

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByText(/載入更多失敗/)).toBeInTheDocument();
    expect(screen.getByRole('article', { name: '報價單' })).toBeInTheDocument();
  });
});

describe('卡片', () => {
  it('標題是連到項目頁的連結（寬螢幕一般的點擊由頁面的面板接手）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], items: [quoting] });
    renderMyWorkBoard();

    // Act
    const card = await screen.findByRole('article', { name: '報價單' });

    // Assert
    expect(within(card).getByRole('link', { name: '報價單' })).toHaveAttribute('href', expect.stringContaining('AGY-1'));
  });
});

describe('在項目面板裡刪除項目之後', () => {
  it('卡片從看板上不見，別的卡片還在', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [quoting, todo] });
    const { queryClient } = renderMyWorkBoard();
    await boardLoaded();

    // Act
    act(() => {
      removeItemFromCache(queryClient, 'AGY', 'AGY-1');
    });

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('article', { name: '報價單' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
  });
});
