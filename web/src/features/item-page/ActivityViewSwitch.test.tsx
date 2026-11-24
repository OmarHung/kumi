import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { commented, created, manyComments, serveActivity, serveComment, transitioned, updated } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { editorText, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';
import type { Activity } from '../../api/types';
import { ACTIVITY_VIEW_STORAGE_KEY, setActivityView } from './activityView';
import { commentHash } from './commentTarget';

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿', state: 'doing', stateCategory: 'in_progress' }));

const HISTORY: readonly Activity[] = [
  created({ at: '2026-11-10T01:00:00Z' }),
  commented('先看這一版', { at: '2026-11-10T02:00:00Z' }),
  transitioned('todo', 'doing', { at: '2026-11-10T03:00:00Z' }),
  updated([{ field: 'due_date', from: '2026-11-20', to: '2026-11-30' }], { at: '2026-11-10T04:00:00Z' }),
];

// 不呼叫 showAllActivity：這個檔案一開始是沒選過，預設要是「留言」；選擇與寫不進去的暫存由 test/setup.ts 在每個測試後清掉。
afterEach(() => {
  vi.restoreAllMocks();
});

async function openItem(history: readonly Activity[] = HISTORY) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  const activity = serveActivity(task.key, history);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  return { ...view, section, activity };
}

function switchTo(section: HTMLElement, name: '留言' | '全部') {
  return within(section).getByRole('button', { name });
}

describe('時間軸：留言｜全部', () => {
  it('預設是「留言」：只有留言，清單名稱是「留言」，請求帶 kind=commented', async () => {
    // Arrange & Act
    const { section, activity } = await openItem();

    // Assert
    const list = await within(section).findByRole('list', { name: '留言' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(1);
    expect(list).toHaveTextContent('先看這一版');
    expect(within(section).queryByText(/把狀態從/)).not.toBeInTheDocument();
    expect(switchTo(section, '留言')).toHaveAttribute('aria-pressed', 'true');
    expect(switchTo(section, '全部')).toHaveAttribute('aria-pressed', 'false');
    expect(activity.requests.map((url) => url.searchParams.get('kind'))).toEqual(['commented']);
  });

  it('切到「全部」看得到改狀態那一行，不帶 kind；切回來又看不到', async () => {
    // Arrange
    const { user, section, activity } = await openItem();
    await within(section).findByRole('list', { name: '留言' });

    // Act
    await user.click(switchTo(section, '全部'));

    // Assert
    const all = await within(section).findByRole('list', { name: '活動與留言' });
    expect(await within(all).findByText(/把狀態從/)).toBeInTheDocument();
    expect(switchTo(section, '全部')).toHaveFocus();
    expect(activity.requests.at(-1)?.searchParams.has('kind')).toBe(false);

    // Act
    await user.click(switchTo(section, '留言'));

    // Assert
    await within(section).findByRole('list', { name: '留言' });
    expect(within(section).queryByText(/把狀態從/)).not.toBeInTheDocument();
  });

  it('選擇記住：重新掛上之後還是「全部」', async () => {
    // Arrange
    const first = await openItem();
    await first.user.click(await within(first.section).findByRole('button', { name: '全部' }));
    await within(first.section).findByRole('list', { name: '活動與留言' });
    first.unmount();

    // Act
    const { section } = await openItem();

    // Assert
    expect(localStorage.getItem(ACTIVITY_VIEW_STORAGE_KEY)).toBe('all');
    expect(await within(section).findByText(/把狀態從/)).toBeInTheDocument();
    expect(switchTo(section, '全部')).toHaveAttribute('aria-pressed', 'true');
  });

  it('localStorage 寫不進去時照樣能切換', async () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { user, section } = await openItem();
    await within(section).findByRole('list', { name: '留言' });

    // Act
    await user.click(switchTo(section, '全部'));

    // Assert
    expect(await within(section).findByText(/把狀態從/)).toBeInTheDocument();
  });

  it('只有變更沒有留言時說「還沒有留言。」，切到「全部」才看得到建立那一行', async () => {
    // Arrange
    const { user, section } = await openItem([created()]);

    // Act & Assert
    expect(await within(section).findByText('還沒有留言。')).toBeInTheDocument();
    await user.click(switchTo(section, '全部'));
    expect(await within(section).findByText(/建立了這個項目/)).toBeInTheDocument();
  });

  it('讀不到時依看哪一份說「留言載入失敗」「活動載入失敗」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(task);
    server.use(http.get('/v1/items/SMK-7/activity', () => failure(500, 'server.error', '壞了')));
    renderApp('/projects/SMK/items/SMK-7');
    const section = await screen.findByRole('region', { name: '活動與留言' });

    // Act
    await within(section).findByText('留言載入失敗');
    await userEvent.click(switchTo(section, '全部'));

    // Assert
    expect(await within(section).findByText('活動載入失敗')).toBeInTheDocument();
  });

  it('切換不算新活動：不唸', async () => {
    // Arrange
    const { user, section } = await openItem();
    await within(section).findByRole('list', { name: '留言' });

    // Act
    await user.click(switchTo(section, '全部'));
    await within(section).findByRole('list', { name: '活動與留言' });
    await user.click(switchTo(section, '留言'));
    await within(section).findByRole('list', { name: '留言' });

    // Assert
    const statuses = within(section).getAllByRole('status');
    statuses.forEach((status) => {
      expect(status).toBeEmptyDOMElement();
    });
  });

  it('留言框打到一半切換：同一個文字框，字與焦點都還在', async () => {
    // Arrange
    const { user, section } = await openItem();
    const textarea = await findEditor('留言', section);
    await typeInEditor(user, textarea, '打到一半');

    // Act
    await user.click(switchTo(section, '全部'));
    await within(section).findByRole('list', { name: '活動與留言' });

    // Assert
    const after = await findEditor('留言', section);
    expect(after).toBe(textarea);
    expect(editorText(after)).toBe('打到一半');
  });

  it('送出留言後兩份都看得到', async () => {
    // Arrange
    const { user, section, activity } = await openItem();
    serveComment(task.key, { timeline: activity });
    const textarea = await findEditor('留言', section);
    await within(section).findByRole('list', { name: '留言' });

    // Act
    await user.click(switchTo(section, '全部'));
    await within(section).findByRole('list', { name: '活動與留言' });
    await user.click(switchTo(section, '留言'));
    await within(section).findByRole('list', { name: '留言' });
    await typeInEditor(user, textarea, '新的留言');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(await within(section).findByText('新的留言')).toBeInTheDocument();
    await user.click(switchTo(section, '全部'));
    const all = await within(section).findByRole('list', { name: '活動與留言' });
    expect(await within(all).findByText('新的留言')).toBeInTheDocument();
    expect(within(all).getAllByText('新的留言')).toHaveLength(1);
  });

  it('「留言」裡分頁：假資料超過一頁，每一頁都只有留言', async () => {
    // Arrange
    const many = [
      created(),
      ...Array.from({ length: 60 }, (_, index) => [
        commented(`留言 ${String(index + 1)}`),
        transitioned('todo', 'doing'),
      ]).flat(),
    ];
    const { user, section, activity } = await openItem(many);

    // Act
    await within(section).findByText('留言 50');
    expect(within(section).queryByText('留言 51')).not.toBeInTheDocument();
    await user.click(within(section).getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await within(section).findByText('留言 60')).toBeInTheDocument();
    expect(within(section).queryByText(/把狀態從/)).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '載入更多' })).not.toBeInTheDocument();
    expect(activity.requests.map((url) => url.searchParams.get('kind'))).toEqual(['commented', 'commented']);
  });

  it('點通知捲到留言：在「留言」裡也找得到，不必切換', async () => {
    // Arrange
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
    const target = commented('要看的這一則');
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(task);
    serveActivity(task.key, [created(), transitioned('todo', 'doing'), target]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);

    // Assert
    await screen.findByText('要看的這一則');
    await waitFor(() => {
      expect(scrollIntoView).toHaveBeenCalledTimes(1);
    });
    expect(screen.queryByText(/把狀態從/)).not.toBeInTheDocument();
    Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
  });

  it('在「全部」按了「載入更多」，載入中切到「留言」再切回來再按一次：載完把焦點放到新載入的第一筆，不會落到別份', async () => {
    // Arrange
    setActivityView('all');
    const firstPage = [created(), ...manyComments(49)];
    const later = commented('晚到的第二頁');
    const { user, section } = await openItem([...firstPage, later]);
    await within(section).findByText('留言 49');
    let release = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/items/SMK-7/activity', async ({ request }) => {
        const url = new URL(request.url);
        if (url.searchParams.get('kind') === 'commented') return ok({ items: firstPage.filter((entry) => entry.kind === 'commented'), nextCursor: null });
        if (url.searchParams.get('cursor') === null) return ok({ items: firstPage, nextCursor: firstPage.at(-1)?.id ?? null });
        await gate;
        return ok({ items: [later], nextCursor: null });
      }),
    );

    // Act
    await user.click(within(section).getByRole('button', { name: '載入更多' }));
    await within(section).findByRole('button', { name: '載入中…' });
    await user.click(switchTo(section, '留言'));
    await within(section).findByRole('list', { name: '留言' });
    await user.click(switchTo(section, '全部'));
    await within(section).findByRole('list', { name: '活動與留言' });
    // 載入中換份會中止那一頁：回來時是「載入更多」，再按一次。
    await user.click(await within(section).findByRole('button', { name: '載入更多' }));
    release();

    // Assert
    const added = (await within(section).findByText('晚到的第二頁')).closest('li');
    await waitFor(() => {
      expect(added).toHaveFocus();
    });
  });

  it('換到還沒載入過的那一份：上一份留下的「已送出留言」立刻清掉，不等載入完', async () => {
    // Arrange
    setActivityView('all');
    const { user, section, activity } = await openItem();
    await within(section).findByRole('list', { name: '活動與留言' });
    serveComment(task.key, { timeline: activity });
    await typeInEditor(user, await findEditor('留言', section), '新的留言');
    await user.keyboard('{Control>}{Enter}{/Control}');
    await waitFor(() => {
      expect(within(section).getAllByRole('status').at(-1)).toHaveTextContent('已送出留言');
    });
    server.use(http.get('/v1/items/SMK-7/activity', () => new Promise(() => undefined)));

    // Act
    await user.click(switchTo(section, '留言'));

    // Assert
    expect(await within(section).findByText('正在載入留言…')).toBeInTheDocument();
    within(section)
      .getAllByRole('status')
      .forEach((status) => {
        expect(status).not.toHaveTextContent('已送出留言');
      });
  });
});
