import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import { agyDetail, agyItem, daysFromToday, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const mine = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const partner = { personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例夥伴' };

const smkTodo = smkItem({ number: 1, title: '首頁設計稿', dueDate: daysFromToday(-2) });
const smkDoing = smkItem({ number: 2, title: '整理素材', state: 'doing', stateCategory: 'in_progress', priority: 'high' });
const smkDone = smkItem({ number: 3, title: '舊的稿', state: 'done', stateCategory: 'done' });
const agyRequirement = agyItem({ number: 4, title: '官網需求', assignee: mine });
const partnerItem = smkItem({ number: 5, title: '夥伴的工作', assignee: partner });
const everything = [smkTodo, smkDoing, smkDone, agyRequirement, partnerItem];

function arrange(items = everything) {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()], myWork: items });
}

type BarRole = 'combobox' | 'button' | 'searchbox';

/** 寬螢幕工具列上的那一組篩選（箭頭函式包起來：testing-library 的查詢不能直接解構出來用）。 */
async function openToolbar() {
  const bar = await screen.findByRole('group', { name: '篩選項目' });
  return {
    findByRole: (role: BarRole, options: { readonly name: string }) => within(bar).findByRole(role, options),
    getByRole: (role: BarRole, options: { readonly name: string }) => within(bar).getByRole(role, options),
    queryByRole: (role: BarRole, options: { readonly name: string }) => within(bar).queryByRole(role, options),
  };
}

function rowKeys(): (string | null | undefined)[] {
  return screen
    .queryAllByRole('table')
    .flatMap((table) => within(table).getAllByRole('row').slice(1))
    .map((row) => within(row).getAllByRole('cell')[0]?.textContent);
}

describe('我的工作的工具列：預設', () => {
  it('版面分頁（表格是目前的）、搜尋、類型、狀態、到期、優先度、更多條件（沒有負責人）；預設的條件沒有「清除條件」', async () => {
    // Arrange
    arrange();

    // Act
    renderApp('/my-work');

    // Assert
    const tabs = await screen.findByRole('navigation', { name: '版面' });
    expect(within(tabs).getByRole('link', { name: '表格' })).toHaveAttribute('aria-current', 'page');
    expect(within(tabs).getAllByRole('link').map((link) => link.textContent)).toEqual(['表格', '看板', '行事曆', '時間軸']);
    const bar = await openToolbar();
    expect(bar.getByRole('searchbox', { name: '搜尋' })).toBeInTheDocument();
    expect(await bar.findByRole('combobox', { name: '類型' })).toHaveTextContent('所有類型');
    expect(bar.getByRole('combobox', { name: '狀態' })).toHaveTextContent('未結束的狀態');
    expect(bar.queryByRole('combobox', { name: '負責人' })).not.toBeInTheDocument();
    expect(bar.getByRole('combobox', { name: '到期' })).toHaveTextContent('所有到期日');
    expect(bar.getByRole('combobox', { name: '優先度' })).toHaveTextContent('所有優先度');
    expect(bar.getByRole('button', { name: '更多條件' })).toBeInTheDocument();
    expect(bar.queryByRole('button', { name: '清除條件' })).not.toBeInTheDocument();
  });

  it('預設只列我負責、還沒結束的', async () => {
    // Arrange
    arrange();

    // Act
    renderApp('/my-work');

    // Assert
    await screen.findByRole('table', { name: '已過期' });
    expect(rowKeys().toSorted()).toEqual(['AGY-4', 'SMK-1', 'SMK-2']);
  });
});

describe('我的工作的工具列：選項', () => {
  it('類型：所有類型，再列各專案合併的類型；只有一種時不放', async () => {
    // Arrange
    arrange();
    const { user } = renderApp('/my-work');
    const { findByRole } = await openToolbar();

    // Act
    const options = await openOptions(user, await findByRole('combobox', { name: '類型' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['所有類型', '需求變更單', '需求', '任務']);
  });

  it('狀態：未結束的狀態、所有狀態，再是五個類別；沒選專案時沒有個別的狀態', async () => {
    // Arrange
    arrange();
    const { user } = renderApp('/my-work');
    const { findByRole } = await openToolbar();

    // Act
    const options = await openOptions(user, await findByRole('combobox', { name: '狀態' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['未結束的狀態', '所有狀態', '待規劃', '未開始', '進行中', '完成', '已取消']);
  });

  it('選了專案：狀態多列那個專案的狀態', async () => {
    // Arrange
    arrange();
    const { user } = renderApp('/my-work?project=SMK');
    const { findByRole } = await openToolbar();

    // Act
    const states = await waitFor(async () => {
      const options = await openOptions(user, await findByRole('combobox', { name: '狀態' }));
      expect(options.length).toBeGreaterThan(7);
      return options.map((option) => option.textContent);
    });

    // Assert
    expect(states.slice(0, 7)).toEqual(['未結束的狀態', '所有狀態', '待規劃', '未開始', '進行中', '完成', '已取消']);
    expect(states.slice(7)).toEqual(['待辦', '進行中', '完成']);
  });
});

describe('我的工作的工具列：換條件', () => {
  it('狀態選「進行中」：網址記下 category，只列那一類，多一顆「清除條件」', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work');
    const { findByRole } = await openToolbar();
    await screen.findByRole('table', { name: '已過期' });

    // Act
    await chooseOption(user, await findByRole('combobox', { name: '狀態' }), '進行中');

    // Assert
    await waitFor(() => {
      expect(rowKeys()).toEqual(['SMK-2']);
    });
    expect(currentPath(router)).toBe('/my-work?category=in_progress');
    expect(await findByRole('button', { name: '清除條件' })).toBeInTheDocument();
  });

  it('舊網址上的 assignee=all 不讀：只列我負責的，沒有負責人欄', async () => {
    // Arrange
    arrange();

    // Act
    renderApp('/my-work?assignee=all');
    await screen.findByRole('table', { name: '已過期' });

    // Assert
    expect(screen.queryByRole('row', { name: /SMK-5/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: '負責人' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '清除條件' })).not.toBeInTheDocument();
  });

  it('「更多條件」裡選專案：網址記下 project，只列那個專案的', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work');
    const { findByRole } = await openToolbar();
    await screen.findByRole('table', { name: '已過期' });

    // Act
    await user.click(await findByRole('button', { name: '更多條件' }));
    const popover = await screen.findByRole('group', { name: '更多條件' });
    await chooseOption(user, within(popover).getByRole('combobox', { name: '專案' }), '接案測試');

    // Assert
    await waitFor(() => {
      expect(rowKeys()).toEqual(['AGY-4']);
    });
    expect(currentPath(router)).toBe('/my-work?project=AGY');
  });

  it('搜尋：停下來之後記進網址 q，只列符合的', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work');
    const { findByRole } = await openToolbar();
    await screen.findByRole('table', { name: '已過期' });

    // Act
    await user.type(await findByRole('searchbox', { name: '搜尋' }), '素材');

    // Assert
    await waitFor(() => {
      expect(rowKeys()).toEqual(['SMK-2']);
    });
    expect(currentPath(router)).toBe('/my-work?q=%E7%B4%A0%E6%9D%90');
  });

  it('網址上的狀態不在選的專案裡：拿掉，換成預設的狀態', async () => {
    // Arrange
    arrange();

    // Act
    const { router } = renderApp('/my-work?project=SMK&state=nothing');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?project=SMK');
    });
  });

  it('「清除條件」回到預設（版面與分組留著），它跟著不見，焦點到搜尋框', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work?view=board&group=project&category=done&assignee=all&priority=high');
    const { findByRole } = await openToolbar();

    // Act
    await user.click(await findByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?view=board&group=project');
    });
    expect(screen.queryByRole('button', { name: '清除條件' })).not.toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveFocus();
  });
});

describe('我的工作的版面切換', () => {
  it('分頁帶著條件與分組換版面：看板依類別分欄，工具列還在', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work?priority=high&group=project&month=2026-11');
    const tabs = await screen.findByRole('navigation', { name: '版面' });
    const board = within(tabs).getByRole('link', { name: '看板' });
    expect(board).toHaveAttribute('href', '/my-work?view=board&priority=high&group=project');

    // Act
    await user.click(board);

    // Assert
    expect(await screen.findByRole('navigation', { name: '版面' })).toBe(tabs);
    expect(within(tabs).getByRole('link', { name: '看板' })).toHaveAttribute('aria-current', 'page');
    expect(currentPath(router)).toBe('/my-work?view=board&priority=high&group=project');
    expect(await screen.findByRole('region', { name: /未開始/ })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: '分組' })).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '優先度' })).toHaveTextContent('高');
  });

  it('開著的面板跟著帶到另一個版面', async () => {
    // Arrange
    arrange();
    renderApp('/my-work?item=SMK-1');

    // Act
    const tabs = await screen.findByRole('navigation', { name: '版面' });

    // Assert
    expect(within(tabs).getByRole('link', { name: '時間軸' })).toHaveAttribute('href', '/my-work?view=timeline&item=SMK-1');
  });
});

describe('我的工作的空白', () => {
  it('有條件而沒有符合的：說沒有符合條件的項目，「清除條件」回到預設', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work?priority=urgent');
    expect(await screen.findByText('沒有符合條件的項目')).toBeInTheDocument();
    expect(screen.queryByText('目前沒有你負責的工作')).not.toBeInTheDocument();

    // Act
    // 工具列與空白狀態各一顆「清除條件」：按空白狀態裡的（在內容裡，排在後面）。
    await user.click(screen.getAllByRole('button', { name: '清除條件' }).at(-1) as HTMLElement);

    // Assert
    await screen.findByRole('table', { name: '已過期' });
    expect(currentPath(router)).toBe('/my-work');
  });

  it('預設的條件沒有項目：照舊說這裡會出現什麼，工具列還在', async () => {
    // Arrange
    arrange([]);

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByText('目前沒有你負責的工作')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: '篩選項目' })).toBeInTheDocument();
  });
});

describe('我的工作的讀取失敗', () => {
  it('第一次就讀不到：不放工具列，說載入失敗', async () => {
    // Arrange
    arrange();
    server.use(http.get('/v1/items', () => failure(500, 'server.error', '伺服器出錯了。')));

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('button', { name: '重試' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: '篩選項目' })).not.toBeInTheDocument();
  });

  it('讀過之後換條件、新條件的第一次讀取失敗：工具列還在，「清除條件」按得到，清單換成錯誤', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work');
    await screen.findByRole('table', { name: '已過期' });
    server.use(
      http.get('/v1/items', ({ request }) =>
        new URL(request.url).searchParams.get('priority') === 'urgent' ? failure(500, 'server.error', '伺服器出錯了。') : undefined,
      ),
    );

    // Act
    await router.navigate('/my-work?priority=urgent');

    // Assert
    expect(await screen.findByRole('button', { name: '重試' })).toBeInTheDocument();
    const bar = await openToolbar();
    await user.click(await bar.findByRole('button', { name: '清除條件' }));
    await screen.findByRole('table', { name: '已過期' });
    expect(currentPath(router)).toBe('/my-work');
  });
});
