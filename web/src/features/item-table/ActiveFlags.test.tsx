import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

const OPEN = ['backlog', 'not_started', 'in_progress'];

async function openAgy(path: string) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  return { ...view, requests };
}

describe('總覽連過來的條件：還沒結束、14 天沒有更新', () => {
  it('open=1：送三個還沒結束的狀態類別，表格上方看得到「只看還沒結束的」', async () => {
    // Arrange / Act
    const { requests } = await openAgy('/projects/AGY?open=1');

    // Assert
    expect(screen.getByRole('list', { name: '其他條件' })).toHaveTextContent('只看還沒結束的');
    expect(requests.at(-1)?.searchParams.getAll('stateCategory')).toEqual(OPEN);
    expect(requests.at(-1)?.searchParams.has('updatedBefore')).toBe(false);
  });

  it('stale=1：送 updatedBefore（現在減 14 天）與還沒結束的類別，標記只說「14 天沒有更新（只算還沒結束的）」，不另外列「只看還沒結束的」', async () => {
    // Arrange
    const before = Date.now();

    // Act
    const { requests } = await openAgy('/projects/AGY?stale=1&open=1');

    // Assert
    const params = requests.at(-1)?.searchParams;
    const cutoff = Date.parse(params?.get('updatedBefore') ?? '');
    const fourteenDays = 14 * 24 * 60 * 60 * 1000;
    const flags = screen.getByRole('list', { name: '其他條件' });
    expect(within(flags).getAllByRole('listitem')).toHaveLength(1);
    expect(flags).toHaveTextContent('14 天沒有更新（只算還沒結束的）');
    expect(flags).not.toHaveTextContent('只看還沒結束的');
    expect(params?.getAll('stateCategory')).toEqual(OPEN);
    expect(cutoff).toBeGreaterThan(before - fourteenDays - 120_000);
    expect(cutoff).toBeLessThan(Date.now() - fourteenDays + 1);
  });

  it('只有 stale（存的檢視）也只顯示一個標記', async () => {
    // Arrange / Act
    await openAgy('/projects/AGY?stale=1');

    // Assert
    const flags = screen.getByRole('list', { name: '其他條件' });
    expect(within(flags).getAllByRole('listitem')).toHaveLength(1);
    expect(flags).toHaveTextContent('14 天沒有更新（只算還沒結束的）');
  });

  it('stale 與 open 同時開著時，拿掉標記兩個一起拿掉', async () => {
    // Arrange
    const { user, router, requests } = await openAgy('/projects/AGY?stale=1&open=1');

    // Act
    await user.click(screen.getByRole('button', { name: '拿掉「14 天沒有更新（只算還沒結束的）」' }));

    // Assert
    await waitFor(() => { expect(currentPath(router)).toBe('/projects/AGY'); });
    await waitFor(() => { expect(requests.at(-1)?.searchParams.has('stateCategory')).toBe(false); });
    expect(screen.queryByRole('list', { name: '其他條件' })).not.toBeInTheDocument();
  });

  it('按標記的「拿掉」就把條件從網址與請求拿掉，焦點落在搜尋框', async () => {
    // Arrange
    const { user, router, requests } = await openAgy('/projects/AGY?stale=1');

    // Act
    await user.click(screen.getByRole('button', { name: '拿掉「14 天沒有更新（只算還沒結束的）」' }));

    // Assert
    await waitFor(() => { expect(currentPath(router)).toBe('/projects/AGY'); });
    await waitFor(() => { expect(requests.at(-1)?.searchParams.has('updatedBefore')).toBe(false); });
    expect(screen.queryByRole('list', { name: '其他條件' })).not.toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: '搜尋' })).toHaveFocus();
  });

  it('「清除條件」連它們一起清掉', async () => {
    // Arrange
    const { user, router } = await openAgy('/projects/AGY?open=1&stale=1&assignee=me');

    // Act
    await user.click(screen.getByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => { expect(currentPath(router)).toBe('/projects/AGY'); });
  });

  it('到期選「7 天內到期」：送今天到 6 天後與還沒結束的類別', async () => {
    // Arrange
    const { user, requests, router } = await openAgy('/projects/AGY');

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '到期' }), '7 天內到期');

    // Assert
    await waitFor(() => { expect(currentPath(router)).toBe('/projects/AGY?due=soon'); });
    await waitFor(() => { expect(requests.at(-1)?.searchParams.getAll('stateCategory')).toEqual(OPEN); });
    const params = requests.at(-1)?.searchParams;
    expect(params?.has('dueFrom')).toBe(true);
    expect(params?.has('dueTo')).toBe(true);
  });
});

describe('停滯的分界固定在條件生效的那一刻', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('條件開著、時鐘跨過一分鐘再重畫：查詢鍵不變、不多送請求、已載入的項目還在', async () => {
    // Arrange：只假裝 Date，其他計時器照舊，MSW 與 user-event 才不會卡住。
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-26T08:30:45.000Z'));
    const { user, requests } = await openAgy('/projects/AGY?stale=1');
    const countBefore = requests.length;
    const cutoffBefore = requests.at(-1)?.searchParams.get('updatedBefore');

    // Act：時鐘走了 90 秒，在快速新增的標題框打字（讓整個表格狀態重畫）。
    vi.setSystemTime(new Date('2026-11-26T08:32:15.000Z'));
    await user.type(screen.getByRole('textbox', { name: '標題' }), '新的需求');

    // Assert
    expect(requests).toHaveLength(countBefore);
    expect(requests.at(-1)?.searchParams.get('updatedBefore')).toBe(cutoffBefore);
    expect(screen.getByText('官網需求')).toBeInTheDocument();
  });

  it('換條件之後重新取分界', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-26T08:30:45.000Z'));
    const { user, requests } = await openAgy('/projects/AGY?stale=1');
    const cutoffBefore = requests.at(-1)?.searchParams.get('updatedBefore');

    // Act
    vi.setSystemTime(new Date('2026-11-26T08:40:45.000Z'));
    await user.type(screen.getByRole('searchbox', { name: '搜尋' }), 'a');

    // Assert
    await waitFor(() => { expect(requests.at(-1)?.searchParams.get('updatedBefore')).not.toBe(cutoffBefore); });
  });
});
