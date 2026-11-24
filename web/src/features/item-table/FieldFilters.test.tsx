import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { stubResizeObserver } from '../../test/resizeObserver';
import { chooseOption, openOptions } from '../../test/select';
import { simpleTasksBlueprint } from '../../test/blueprints';

afterEach(() => {
  vi.unstubAllGlobals();
});

const ITEMS = [
  smkItem({ number: 1, title: '修登入', fields: { category: 'bug', tags: ['backend'], blocked: true, story_points: 8, reviewer: exampleMember.personId } }),
  smkItem({ number: 2, title: '做報表', fields: { category: 'feature', tags: ['frontend', 'docs'], story_points: 3 } }),
  smkItem({ number: 3, title: '整理文件' }),
];

async function openSmk(path = '/projects/SMK') {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const server = serveItemsByQuery('SMK', ITEMS, { blueprint: simpleTasksBlueprint });
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  return { ...view, server, filters: screen.getByRole('group', { name: '篩選項目' }) };
}

async function openMore(user: ReturnType<typeof renderApp>['user'], filters: HTMLElement): Promise<HTMLElement> {
  await user.click(await within(filters).findByRole('button', { name: '更多條件' }));
  return screen.findByRole('dialog', { name: '更多條件' });
}

const lastParams = (server: { readonly requests: readonly URL[] }) => server.requests.at(-1)?.searchParams ?? new URLSearchParams();

describe('表格依自訂欄位篩選', () => {
  it('「更多條件」裡每個能篩的欄位一個下拉，照藍圖的順序；不能篩的型別沒有', async () => {
    // Arrange
    const { user, filters } = await openSmk();

    // Act
    const more = await openMore(user, filters);

    // Assert
    const names = within(more).getAllByRole('combobox').map((box) => box.textContent);
    expect(names).toEqual(['所有分類', '所有標籤', '所有卡住了', '所有審查者']);
    expect(within(more).queryByRole('combobox', { name: '摘要' })).not.toBeInTheDocument();
    expect(within(more).queryByRole('combobox', { name: '點數' })).not.toBeInTheDocument();
  });

  it('單選與多選的選項：所有<欄位名>、沒有填，再列各個選項', async () => {
    // Arrange
    const { user, filters } = await openSmk();
    const more = await openMore(user, filters);

    // Act
    const options = await openOptions(user, within(more).getByRole('combobox', { name: '分類' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['所有分類', '沒有填', '新功能', '錯誤', '雜務']);
  });

  it('人員的選項：所有<欄位名>、我、沒有填，再列其他成員；勾選是已勾選、沒勾選', async () => {
    // Arrange
    const { user, filters } = await openSmk();
    const more = await openMore(user, filters);

    // Act
    const people = await openOptions(user, within(more).getByRole('combobox', { name: '審查者' }));
    await user.keyboard('{Escape}');
    const checks = await openOptions(user, within(more).getByRole('combobox', { name: '卡住了' }));

    // Assert
    expect(people.map((option) => option.dataset.value)).toEqual(['', 'me', '_none', exampleMember.personId]);
    expect(checks.map((option) => option.textContent)).toEqual(['所有卡住了', '已勾選', '沒勾選']);
  });

  it('選了就篩：網址帶 field.<key>、請求帶同一個參數、只剩符合的項目；按鈕上的數字加一', async () => {
    // Arrange
    const { user, router, server, filters } = await openSmk();
    const more = await openMore(user, filters);

    // Act
    await chooseOption(user, within(more).getByRole('combobox', { name: '分類' }), '錯誤');

    // Assert
    await waitFor(() => { expect(lastParams(server).getAll('field.category')).toEqual(['bug']); });
    expect(new URLSearchParams(router.state.location.search).get('field.category')).toBe('bug');
    const table = await screen.findByRole('table', { name: '項目' });
    await waitFor(() => { expect(within(table).queryByText('做報表')).not.toBeInTheDocument(); });
    expect(within(table).getByText('修登入')).toBeInTheDocument();
    expect(within(filters).getByRole('button', { name: '更多條件' })).toHaveTextContent('1');
  });

  it('不同欄位一起篩是「而且」，每個欄位算一個條件', async () => {
    // Arrange
    const { user, server, filters } = await openSmk();
    const more = await openMore(user, filters);

    // Act
    await chooseOption(user, within(more).getByRole('combobox', { name: '標籤' }), '文件');
    await chooseOption(user, within(more).getByRole('combobox', { name: '卡住了' }), '沒勾選');

    // Assert
    await waitFor(() => { expect(lastParams(server).get('field.blocked')).toBe('false'); });
    expect(lastParams(server).get('field.tags')).toBe('docs');
    expect(within(filters).getByRole('button', { name: '更多條件' })).toHaveTextContent('2');
    expect(await screen.findByText('做報表')).toBeInTheDocument();
  });

  it('「沒有填」與「我」照原樣送出', async () => {
    // Arrange
    const { user, server, filters } = await openSmk();
    const more = await openMore(user, filters);

    // Act
    await chooseOption(user, within(more).getByRole('combobox', { name: '審查者' }), '沒有填');
    await chooseOption(user, within(more).getByRole('combobox', { name: '分類' }), '沒有填');

    // Assert
    await waitFor(() => { expect(lastParams(server).get('field.category')).toBe('_none'); });
    expect(lastParams(server).get('field.reviewer')).toBe('_none');
  });

  it('選回「所有<欄位名>」就拿掉條件', async () => {
    // Arrange
    const { user, router, server, filters } = await openSmk('/projects/SMK?field.category=bug');
    const more = await openMore(user, filters);

    // Act
    await chooseOption(user, within(more).getByRole('combobox', { name: '分類' }), '所有分類');

    // Assert
    await waitFor(() => { expect(lastParams(server).has('field.category')).toBe(false); });
    expect(router.state.location.search).toBe('');
  });

  it('「清除條件」把欄位條件一起清掉', async () => {
    // Arrange
    const { user, router, server, filters } = await openSmk('/projects/SMK?field.category=bug&field.blocked=true');

    // Act
    await user.click(await within(filters).findByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => { expect(lastParams(server).has('field.category')).toBe(false); });
    expect(router.state.location.search).toBe('');
  });

  it('網址上已經不在藍圖裡的欄位與不能排的排序，送出前拿掉：不然整張表被 422 擋掉', async () => {
    // Arrange / Act
    const { server } = await openSmk('/projects/SMK?field.gone=x&field.summary=y&field.category=bug&sort=field.reviewer:asc');

    // Assert
    await waitFor(() => { expect(server.requests.length).toBeGreaterThan(0); });
    const params = lastParams(server);
    expect(params.has('field.gone')).toBe(false);
    expect(params.has('field.summary')).toBe(false);
    expect(params.get('field.category')).toBe('bug');
    expect(params.has('sort')).toBe(false);
  });

  it('窄螢幕：欄位條件在「篩選」浮層裡，數字算進去', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === COMPACT_QUERY, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const server = serveItemsByQuery('SMK', ITEMS, { blueprint: simpleTasksBlueprint });
    const { user } = renderApp('/projects/SMK');

    // Act
    await user.click(await screen.findByRole('button', { name: '篩選' }));
    const dialog = await screen.findByRole('dialog', { name: '篩選' });
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '卡住了' }), '已勾選');

    // Assert
    await waitFor(() => { expect(lastParams(server).get('field.blocked')).toBe('true'); });
    expect(screen.getByRole('button', { name: '篩選' })).toHaveTextContent('1');
  });
});

describe('自訂欄位的位置', () => {
  it('列再寬也在「更多條件」裡，不擠進列上：欄位數不定，擠掉的是常用的條件', async () => {
    // Arrange
    const resize = stubResizeObserver();
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', ITEMS, { blueprint: simpleTasksBlueprint });
    renderApp('/projects/SMK');
    const bar = await screen.findByRole('group', { name: '篩選項目' });

    // Act
    resize.report('[role=group][aria-label="篩選項目"]', 1400);

    // Assert
    expect(within(bar).getByRole('button', { name: '更多條件' })).toBeInTheDocument();
    expect(within(bar).queryByRole('combobox', { name: '分類' })).not.toBeInTheDocument();
    expect(within(bar).getByRole('combobox', { name: '優先度' })).toBeInTheDocument();
  });
});

describe('表格依自訂欄位排序', () => {
  it('能排的欄位標題是按鈕：點一下遞增、再點遞減、再點回到預設', async () => {
    // Arrange
    const { user, server, router } = await openSmk();
    const sortButton = () => within(within(screen.getByRole('table', { name: '項目' })).getByRole('columnheader', { name: '點數' })).getByRole('button', { name: '點數' });

    // Act / Assert
    await user.click(await waitFor(() => sortButton()));
    await waitFor(() => { expect(lastParams(server).get('sort')).toBe('field.story_points:asc'); });
    expect(new URLSearchParams(router.state.location.search).get('sort')).toBe('field.story_points:asc');
    await user.click(sortButton());
    await waitFor(() => { expect(lastParams(server).get('sort')).toBe('field.story_points:desc'); });
    await user.click(sortButton());
    // 預設的清單已經讀過、還新鮮，不必再送請求：看網址就知道排序拿掉了。
    await waitFor(() => { expect(router.state.location.search).toBe(''); });
  });

  it('依自訂欄位排好的順序顯示，沒有值的在最後', async () => {
    // Arrange
    const { user } = await openSmk();

    // Act
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(within(table).getByRole('columnheader', { name: '點數' })).getByRole('button', { name: '點數' }));

    // Assert
    await waitFor(() => {
      const rows = within(screen.getByRole('table', { name: '項目' })).getAllByRole('row').map((row) => row.textContent);
      expect(rows.findIndex((text) => text.includes('做報表'))).toBeLessThan(rows.findIndex((text) => text.includes('修登入')));
    });
  });

  it('人員、多選、長文字、網址的標題不能點，和不能排的核心欄一樣', async () => {
    // Arrange
    await openSmk();

    // Act
    const table = await screen.findByRole('table', { name: '項目' });

    // Assert
    const header = (name: string) => within(table).getByRole('columnheader', { name });
    expect(within(header('審查者')).queryByRole('button', { name: '審查者' })).not.toBeInTheDocument();
    expect(within(header('標籤')).queryByRole('button', { name: '標籤' })).not.toBeInTheDocument();
    expect(within(header('備註')).queryByRole('button', { name: '備註' })).not.toBeInTheDocument();
    expect(within(header('點數')).getByRole('button', { name: '點數' })).toBeInTheDocument();
  });

  it('網址指到已不在專案裡的人員：選單顯示「已不在的人員」，不是一整串編號', async () => {
    // Arrange
    const gone = '0190a000-0000-7000-8000-0000000000aa';

    // Act
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', ITEMS, { blueprint: simpleTasksBlueprint });
    const { user } = renderApp(`/projects/SMK?field.reviewer=${gone}`);
    const more = await openMore(user, await screen.findByRole('group', { name: '篩選項目' }));

    // Assert
    expect(within(more).getByRole('combobox', { name: '審查者' })).toHaveTextContent('已不在的人員');
    expect(within(more).getByRole('combobox', { name: '審查者' })).not.toHaveTextContent(gone);
  });
});
