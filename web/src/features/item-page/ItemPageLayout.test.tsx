import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ItemDetail, ProjectDetail } from '../../api/types';
import { created, serveActivity, serveComment, showAllActivity } from '../../test/activity';
import { ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, ALL_PROJECT_PERMISSIONS, exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { editorText, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const requirement = detailOf(agyItem({ number: 3, title: '官網需求' }), '第一段說明');
const child = agyItem({
  number: 5,
  type: 'task',
  title: '首頁切版',
  state: 'doing',
  stateCategory: 'in_progress',
  parentKey: 'AGY-3',
  assignee: { personId: exampleMember.personId, displayName: exampleMember.displayName },
});

async function openItem(item: ItemDetail = requirement, detail: ProjectDetail = agyDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItem(item);
  serveItems(detail.project.key, [child]);
  serveActivity(item.key, [created()]);
  const view = renderApp(`/projects/${detail.project.key}/items/${item.key}`);
  await screen.findByRole('heading', { level: 1 });
  return view;
}

describe('項目頁的頁列：更多動作', () => {
  it('「改類型」與「刪除」收在頁列的「更多動作」裡，刪除排最後', async () => {
    // Arrange
    const { user } = await openItem();

    // Act
    await user.click(screen.getByRole('button', { name: 'AGY-3 的更多動作' }));

    // Assert
    const menu = screen.getByRole('menu', { name: 'AGY-3 的更多動作' });
    expect(within(menu).getAllByRole('menuitem').map((entry) => entry.textContent)).toEqual(['改類型', '刪除']);
    expect(within(screen.getByRole('region', { name: '欄位' })).queryByRole('button', { name: '改類型' })).not.toBeInTheDocument();
  });

  it('選了「改類型」打開對話框，關上之後焦點回到「更多動作」', async () => {
    // Arrange
    const { user } = await openItem();
    await user.click(screen.getByRole('button', { name: 'AGY-3 的更多動作' }));

    // Act
    await user.click(screen.getByRole('menuitem', { name: '改類型' }));
    const dialog = screen.getByRole('dialog', { name: '改 AGY-3 的類型' });
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'AGY-3 的更多動作' })).toHaveFocus();
  });

  it('只能刪除時選單只有「刪除」', async () => {
    // Arrange
    const permissions = ALL_PROJECT_PERMISSIONS.filter((permission) => permission !== 'item.update');
    const { user } = await openItem(requirement, agyDetail({ myPermissions: permissions }));

    // Act
    await user.click(screen.getByRole('button', { name: 'AGY-3 的更多動作' }));

    // Assert
    const menu = screen.getByRole('menu', { name: 'AGY-3 的更多動作' });
    expect(within(menu).getAllByRole('menuitem').map((entry) => entry.textContent)).toEqual(['刪除']);
  });

  it('兩件事都不能做時沒有「更多動作」', async () => {
    // Arrange
    const permissions = ALL_PROJECT_PERMISSIONS.filter((permission) => permission !== 'item.delete');

    // Act
    await openItem(detailOf(smkItem({ number: 7 })), smkDetail({ myPermissions: permissions }));

    // Assert
    expect(screen.queryByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ })).not.toBeInTheDocument();
  });
});

describe('項目頁的標題區', () => {
  it('依序是類型與編號、標題（h1）、建立與更新時間', async () => {
    // Arrange & Act
    await openItem();

    // Assert
    expect(screen.getByRole('article')).toHaveTextContent(/^需求 AGY-3\s*官網需求\s*建立於 .+ 更新於 .+欄位/);
  });

  it('第一筆內容是欄位面板：原始碼排在說明前面，窄螢幕在最上面，寬螢幕和說明同一列', async () => {
    // Arrange & Act
    await openItem();

    // Assert
    expect(screen.getByRole('region', { name: '欄位' })).toHaveAttribute('data-content-start');
    expect(screen.getByRole('region', { name: '說明' })).not.toHaveAttribute('data-content-start');
  });
});

const LINES = Array.from({ length: 20 }, (_, index) => `第 ${String(index + 1)} 行`);
const LONG_DESCRIPTION = LINES.join('\n');

/** jsdom 沒有版面：讓說明那一塊量起來比收起來的高度高（超過 12 行）。 */
function measureDescriptionAsLong() {
  const isDescription = (element: Element) => element.textContent === LONG_DESCRIPTION;
  vi.spyOn(Element.prototype, 'scrollHeight', 'get').mockImplementation(function (this: Element) {
    return isDescription(this) ? 600 : 0;
  });
  vi.spyOn(Element.prototype, 'clientHeight', 'get').mockImplementation(function (this: Element) {
    return isDescription(this) ? 300 : 0;
  });
}

describe('說明很長時先收起來', () => {
  it('超過 12 行時先收起來，「顯示全部」展開、「收起」收回；全文一直都在', async () => {
    // Arrange
    measureDescriptionAsLong();
    const { user } = await openItem(detailOf(agyItem({ number: 3, title: '官網需求' }), LONG_DESCRIPTION));
    const region = screen.getByRole('region', { name: '說明' });
    const toggle = await within(region).findByRole('button', { name: '顯示全部' });
    const text = document.getElementById(toggle.getAttribute('aria-controls') ?? '');

    // Act
    await user.click(toggle);

    // Assert：按鈕的文字就是狀態，不另外標 aria-expanded（否則唸成「收起，已展開」）
    expect(toggle).toHaveAccessibleName('收起');
    expect(toggle).not.toHaveAttribute('aria-expanded');
    expect(text).not.toBeNull();
    await waitFor(() => {
      expect(text?.querySelectorAll('br')).toHaveLength(LINES.length - 1);
    });
    // 說明改用 Markdown 顯示：每一個換行是一個 <br>，文字照舊一個字都不少。
    expect(text?.textContent).toBe(LONG_DESCRIPTION);
    await user.click(toggle);
    expect(within(region).getByRole('button', { name: '顯示全部' })).toBe(toggle);
  });

  it('沒有超過 12 行就不收，沒有「顯示全部」', async () => {
    // Arrange & Act
    await openItem();

    // Assert
    const region = screen.getByRole('region', { name: '說明' });
    expect(within(region).getByText('第一段說明')).toBeInTheDocument();
    expect(within(region).queryByRole('button', { name: '顯示全部' })).not.toBeInTheDocument();
  });

  it('編輯時一律是全文', async () => {
    // Arrange
    measureDescriptionAsLong();
    const { user } = await openItem(detailOf(agyItem({ number: 3, title: '官網需求' }), LONG_DESCRIPTION));
    const region = screen.getByRole('region', { name: '說明' });
    await within(region).findByRole('button', { name: '顯示全部' });

    // Act
    await user.click(within(region).getByRole('button', { name: '編輯說明' }));

    // Assert
    const editor = await findEditor('說明', region);
    expect(editor).toHaveTextContent(LINES[0] ?? '');
    expect(editor).toHaveTextContent(LINES.at(-1) ?? '');
    expect(within(region).queryByRole('button', { name: '顯示全部' })).not.toBeInTheDocument();
  });
});

describe('下層項目的清單列', () => {
  it('頭像是裝飾（旁邊有名字），清單裡沒有另外唸的圖', async () => {
    // Arrange & Act
    await openItem();

    // Assert
    const list = await screen.findByRole('list', { name: '下層項目' });
    const [row] = within(list).getAllByRole('listitem');
    expect(row).toHaveTextContent('範例成員');
    expect(within(list).queryAllByRole('img')).toHaveLength(0);
  });
});

describe('活動的作者', () => {
  it('每一筆前面是作者的頭像，旁邊有名字，只是裝飾', async () => {
    // Arrange & Act
    await openItem();

    // Assert
    const section = screen.getByRole('region', { name: '活動與留言' });
    const timeline = await within(section).findByRole('list', { name: '活動與留言' });
    const [first] = within(timeline).getAllByRole('listitem');
    expect(first).toHaveTextContent(/^範範例使用者 建立了這個項目/);
    expect(within(timeline).queryAllByRole('img')).toHaveLength(0);
  });
});

/** 收起來的留言框（編輯器的外框標著 data-collapsed）。 */
function isCollapsed(editor: HTMLElement): boolean {
  return editor.closest('[data-collapsed]')?.getAttribute('data-collapsed') === 'true';
}

describe('留言框', () => {
  it('平常是一行，取得焦點才展開；焦點留在框裡，空白時離開又收回', async () => {
    // Arrange
    const { user } = await openItem();
    const textarea = await findEditor('留言', screen.getByRole('region', { name: '活動與留言' }));
    expect(isCollapsed(textarea)).toBe(true);

    // Act
    await user.click(textarea);

    // Assert
    expect(isCollapsed(textarea)).toBe(false);
    expect(textarea).toHaveFocus();
    // 空白時「送出留言」停用（跳過）：「插入圖片或檔案」在編輯器上方的工具列裡，Tab 一次就離開、收回。
    await user.tab();
    expect(isCollapsed(textarea)).toBe(true);
  });

  it('「插入圖片或檔案」在編輯器的工具列裡：文字框按 Tab 直接到送出留言', async () => {
    // Arrange
    const { user } = await openItem();
    const section = screen.getByRole('region', { name: '活動與留言' });
    await typeInEditor(user, await findEditor('留言', section), '寫到一半');

    // Act
    await user.tab();

    // Assert
    expect(document.activeElement).toBe(within(section).getByRole('button', { name: '送出留言' }));
    const toolbar = within(section).getByRole('toolbar', { name: '格式' });
    expect(within(toolbar).getByRole('button', { name: '插入圖片或檔案' })).toBeInTheDocument();
    expect(within(section).getAllByRole('button', { name: '插入圖片或檔案' })).toHaveLength(1);
  });

  it('點了展開時把整個框捲進看得到的地方（下面不被面板的底切掉）；打字時不再捲', async () => {
    // Arrange：jsdom 沒有版面，整頁的項目頁沒有會捲的容器，所以走 scrollIntoView 的 nearest。
    const reveal = vi.fn();
    Element.prototype.scrollIntoView = reveal;
    const { user } = await openItem();
    const section = screen.getByRole('region', { name: '活動與留言' });
    const textarea = await findEditor('留言', section);
    const form = within(section).getByRole('form', { name: '新增留言' });

    // Act
    await user.click(textarea);

    // Assert
    await waitFor(() => {
      expect(reveal).toHaveBeenCalledTimes(1);
    });
    expect(reveal.mock.contexts[0]).toBe(form);
    expect(reveal).toHaveBeenCalledWith(expect.objectContaining({ block: 'nearest' }));

    // Act
    await typeInEditor(user, textarea, '寫到一半');

    // Assert
    expect(reveal).toHaveBeenCalledTimes(1);
    Reflect.deleteProperty(Element.prototype, 'scrollIntoView');
  });

  it('打了字之後離開也不收起來', async () => {
    // Arrange
    const { user } = await openItem();
    const textarea = await findEditor('留言', screen.getByRole('region', { name: '活動與留言' }));

    // Act
    await typeInEditor(user, textarea, '寫到一半');
    await user.tab();

    // Assert
    expect(isCollapsed(textarea)).toBe(false);
    expect(textarea).not.toHaveFocus();
  });
});

describe('留言框：用「送出留言」送出', () => {
  async function openWithComments() {
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItem(requirement);
    serveItems('AGY', [child]);
    const timeline = serveActivity(requirement.key, [created()]);
    serveComment(requirement.key, { timeline });
    const view = renderApp('/projects/AGY/items/AGY-3');
    const section = await screen.findByRole('region', { name: '活動與留言' });
    const textarea = await findEditor('留言', section);
    return { ...view, section, textarea };
  }

  it('點按鈕送出：送出中與送出之後焦點都在編輯器，可以接著寫', async () => {
    // Arrange
    const { user, section, textarea } = await openWithComments();
    await typeInEditor(user, textarea, '用按鈕送出');

    // Act
    await user.click(within(section).getByRole('button', { name: '送出留言' }));

    // Assert
    expect(document.activeElement).toBe(textarea);
    expect(await within(section).findByText('用按鈕送出')).toBeInTheDocument();
    expect(editorText(textarea)).toBe('');
    expect(document.activeElement).toBe(textarea);
  });

  it('在按鈕上按 Enter 送出：焦點回到編輯器', async () => {
    // Arrange
    const { user, section, textarea } = await openWithComments();
    await typeInEditor(user, textarea, '用 Enter 送出');
    // 「插入圖片或檔案」在編輯器上方的工具列裡（8.3「貼圖與附加檔案」）：文字框之後就是「送出留言」。
    await user.tab();
    expect(within(section).getByRole('button', { name: '送出留言' })).toHaveFocus();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(section).findByText('用 Enter 送出')).toBeInTheDocument();
    expect(document.activeElement).toBe(textarea);
  });
});

describe('下層項目的「載入更多」', () => {
  const later = agyItem({ number: 6, type: 'task', title: '表單串接', state: 'todo', stateCategory: 'not_started', parentKey: 'AGY-3' });

  it('載入中按鈕停得到（aria-disabled），焦點不掉；載完最後一頁按鈕不見時，焦點到新載入的第一筆', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItem(requirement);
    serveActivity(requirement.key, [created()]);
    let release = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/projects/AGY/items', async ({ request }) => {
        const isSecondPage = new URL(request.url).searchParams.get('cursor') === 'page-1';
        if (isSecondPage) await gate;
        return ok(isSecondPage ? { items: [later], nextCursor: null } : { items: [child], nextCursor: 'page-1' });
      }),
    );
    const { user } = renderApp('/projects/AGY/items/AGY-3');
    const section = await screen.findByRole('region', { name: '下層項目' });
    const more = await within(section).findByRole('button', { name: '載入更多' });

    // Act
    await user.click(more);

    // Assert
    const loading = await within(section).findByRole('button', { name: '載入中…' });
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).toBeEnabled();
    expect(loading).toHaveFocus();
    release();
    const added = await within(section).findByRole('link', { name: /^AGY-6 表單串接 / });
    await waitFor(() => {
      expect(added).toHaveFocus();
    });
    expect(within(section).queryByRole('button', { name: /載入/ })).not.toBeInTheDocument();
  });
});
