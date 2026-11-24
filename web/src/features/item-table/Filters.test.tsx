import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

function lastParams(requests: readonly URL[]): URLSearchParams {
  return requests.at(-1)?.searchParams ?? new URLSearchParams();
}

function urlParams(router: ReturnType<typeof renderApp>['router']): URLSearchParams {
  return new URLSearchParams(router.state.location.search);
}

async function openAgy(path = '/projects/AGY') {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  const filters = screen.getByRole('group', { name: '篩選項目' });
  return { ...view, requests, filters };
}

describe('篩選', () => {
  it('類型、狀態、負責人帶出對的查詢參數，也寫進網址', async () => {
    // Arrange
    const { user, requests, filters, router } = await openAgy();
    expect(lastParams(requests).toString()).toBe('limit=50');

    // Act
    await chooseOption(user, within(filters).getByRole('combobox', { name: '類型' }), '需求');
    await chooseOption(user, within(filters).getByRole('combobox', { name: '狀態' }), '開發');
    await chooseOption(user, within(filters).getByRole('combobox', { name: '負責人' }), '我');

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('assignee')).toBe('me');
    });
    const sent = lastParams(requests);
    expect(sent.getAll('type')).toEqual(['requirement']);
    expect(sent.getAll('state')).toEqual(['developing']);
    expect(sent.get('limit')).toBe('50');
    expect(urlParams(router).toString()).toBe('type=requirement&state=developing&assignee=me');
  });

  it('狀態的選項跟著類型換；原本選的狀態不在新類型的流程裡就清掉', async () => {
    // Arrange
    const { user, filters, router } = await openAgy('/projects/AGY?type=requirement&state=developing');
    const stateSelect = within(filters).getByRole('combobox', { name: '狀態' });
    expect(stateSelect).toHaveTextContent('開發');

    // Act
    await chooseOption(user, within(filters).getByRole('combobox', { name: '類型' }), '任務');

    // Assert
    const names = (await openOptions(user, stateSelect)).map((option) => option.textContent);
    expect(names).toEqual(['所有狀態', '待辦', '進行中', '完成']);
    expect(urlParams(router).toString()).toBe('type=task');
  });

  it('沒選類型：狀態依流程分組、組名是用它的類型；打字篩選時組名跟著留下來的選項', async () => {
    // Arrange
    const { user, filters, router } = await openAgy();
    const stateSelect = within(filters).getByRole('combobox', { name: '狀態' });
    const groupNames = () => {
      const listbox = document.getElementById(stateSelect.getAttribute('aria-controls') ?? '');
      return Array.from(listbox?.querySelectorAll('[data-group-label]') ?? [], (label) => label.textContent);
    };

    // Act
    await openOptions(user, stateSelect);
    const before = groupNames();
    await user.type(await screen.findByRole('combobox', { name: '篩選 狀態' }), '完成');

    // Assert
    expect(before).toEqual(['需求、需求變更單', '任務']);
    await waitFor(() => {
      expect(groupNames()).toEqual(['任務']);
    });
    await chooseOption(user, stateSelect, '完成');
    expect(urlParams(router).toString()).toBe('state=done');
  });

  it('負責人可以選沒有人或某個成員', async () => {
    // Arrange
    const { user, requests, filters } = await openAgy();

    // Act
    await chooseOption(user, within(filters).getByRole('combobox', { name: '負責人' }), '沒有負責人');
    await waitFor(() => {
      expect(lastParams(requests).get('assignee')).toBe('unassigned');
    });
    await chooseOption(user, within(filters).getByRole('combobox', { name: '負責人' }), '範例成員');

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('assignee')).toBe(exampleMember.personId);
    });
  });

  it('搜尋文字打完停一下就查，按 Enter 馬上查', async () => {
    // Arrange
    const { user, requests, router } = await openAgy();
    const search = within(screen.getByRole('search')).getByRole('searchbox', { name: '搜尋' });

    // Act
    await user.type(search, '官網');

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('q')).toBe('官網');
    });
    expect(urlParams(router).get('q')).toBe('官網');

    // Act
    await user.clear(search);
    await user.type(search, 'AGY-1{Enter}');

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('q')).toBe('AGY-1');
    });
  });

  it('網址帶著條件打開時直接用那些條件查，重新整理不會消失', async () => {
    // Arrange / Act
    const { requests, filters } = await openAgy('/projects/AGY?type=task&assignee=unassigned&q=切版&sort=due_date:desc');

    // Assert
    const sent = lastParams(requests);
    expect(sent.getAll('type')).toEqual(['task']);
    expect(sent.get('assignee')).toBe('unassigned');
    expect(sent.get('q')).toBe('切版');
    expect(sent.get('sort')).toBe('due_date:desc');
    expect(within(filters).getByRole('combobox', { name: '類型' })).toHaveTextContent('任務');
    expect(within(screen.getByRole('search')).getByRole('searchbox', { name: '搜尋' })).toHaveValue('切版');
  });

  it('清除條件留著排序', async () => {
    // Arrange
    const { user, filters, router } = await openAgy('/projects/AGY?q=切版&sort=title:asc');

    // Act
    await user.click(within(filters).getByRole('button', { name: '清除條件' }));

    // Assert
    expect(urlParams(router).toString()).toBe('sort=title%3Aasc');
    expect(within(screen.getByRole('search')).getByRole('searchbox', { name: '搜尋' })).toHaveValue('');
  });
});

describe('排序', () => {
  it('點欄位標題依序是遞增、遞減、回到預設；預設是依編號遞增', async () => {
    // Arrange
    const { user, requests, router } = await openAgy();
    const table = screen.getByRole('table', { name: '項目' });
    const header = (name: string) => within(table).getByRole('columnheader', { name });
    expect(header('編號')).toHaveAttribute('aria-sort', 'ascending');

    // Act
    await user.click(within(header('到期日')).getByRole('button', { name: '到期日' }));

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('sort')).toBe('due_date:asc');
    });
    expect(header('到期日')).toHaveAttribute('aria-sort', 'ascending');
    expect(header('編號')).not.toHaveAttribute('aria-sort');

    // Act
    await user.click(within(header('到期日')).getByRole('button', { name: '到期日' }));
    await waitFor(() => {
      expect(lastParams(requests).get('sort')).toBe('due_date:desc');
    });
    await user.click(within(header('到期日')).getByRole('button', { name: '到期日' }));

    // Assert
    expect(urlParams(router).get('sort')).toBeNull();
    expect(header('編號')).toHaveAttribute('aria-sort', 'ascending');

    // Act
    await user.click(within(header('編號')).getByRole('button', { name: '編號' }));

    // Assert
    await waitFor(() => {
      expect(lastParams(requests).get('sort')).toBe('number:desc');
    });
  });

  it('後端不支援排序的欄位不能點', async () => {
    // Arrange
    await openAgy();
    const table = screen.getByRole('table', { name: '項目' });

    // Assert
    for (const name of ['類型', '負責人', '上層項目', '變更原因']) {
      expect(within(within(table).getByRole('columnheader', { name })).queryByRole('button')).not.toBeInTheDocument();
    }
  });

  it('自訂欄位：金額、日期、單選的標題可以點（依 field.<key> 排）', async () => {
    // Arrange
    const { user, requests } = await openAgy();
    const table = screen.getByRole('table', { name: '項目' });

    // Act
    await user.click(within(within(table).getByRole('columnheader', { name: '報價金額' })).getByRole('button'));

    // Assert
    expect(within(within(table).getByRole('columnheader', { name: '影響程度' })).getByRole('button')).toBeInTheDocument();
    await waitFor(() => { expect(lastParams(requests).get('sort')).toBe('field.quote_amount:asc'); });
  });
});

describe('載入更多', () => {
  it('還有下一頁時用游標載入，接在後面；沒有了就不再顯示', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveItems(
      'SMK',
      [smkItem({ number: 1, title: '第一頁' })],
      [smkItem({ number: 2, title: '第二頁' })],
    );
    const { user } = renderApp('/projects/SMK?sort=title:asc');
    await screen.findByText('第一頁');

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByText('第二頁')).toBeInTheDocument();
    expect(screen.getByText('第一頁')).toBeInTheDocument();
    expect(lastParams(requests).get('cursor')).toBe('page-1');
    expect(lastParams(requests).get('sort')).toBe('title:asc');
    expect(screen.queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    expect(screen.getByText('已載入 2 個項目')).toBeInTheDocument();
  });

  it('載入更多失敗時說明，可以再按一次', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '第一頁' })], [smkItem({ number: 2, title: '第二頁' })]);
    const { user } = renderApp('/projects/SMK');
    await screen.findByText('第一頁');
    server.use(
      http.get('/v1/projects/SMK/items', () => failure(503, 'server.not_ready', 'starting'), { once: true }),
    );

    // Act
    await user.click(screen.getByRole('button', { name: '載入更多' }));
    expect(await screen.findByText(/載入更多失敗/)).toHaveTextContent('伺服器暫時無法處理');
    await user.click(screen.getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await screen.findByText('第二頁')).toBeInTheDocument();
  });
});
