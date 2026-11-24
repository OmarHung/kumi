import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Permission } from '../../api/blueprint';
import { queryKeys } from '../../api/queryKeys';
import type { Activity, SessionUser } from '../../api/types';
import { commented, created, editedComment, memberRef, ownerRef, serveActivity, serveCommentEditing } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem, smkProject } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { clearEditor, editorText, findEditor, tiptapOf, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';
import { draftKey } from '../../lib/draftStorage';

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const WRITER: readonly Permission[] = ['item.read', 'comment.create', 'blueprint.read'];
const READER: readonly Permission[] = ['item.read', 'blueprint.read'];
const MANAGER: readonly Permission[] = ['item.read', 'comment.create', 'project.manage', 'blueprint.read'];

interface OpenOptions {
  readonly viewer?: SessionUser;
  readonly permissions?: readonly Permission[];
  readonly isArchived?: boolean;
}

async function openComments(history: readonly Activity[], { viewer = exampleOwner, permissions = WRITER, isArchived = false }: OpenOptions = {}) {
  const project = isArchived ? { ...smkProject, archivedAt: '2026-11-20T00:00:00Z' } : smkProject;
  signedInAs(viewer, { details: [smkDetail({ project, myPermissions: permissions })] });
  serveItem(task);
  const timeline = serveActivity(task.key, history);
  const served = serveCommentEditing(task.key, timeline, { viewer: { personId: viewer.personId, displayName: viewer.displayName } });
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  await within(section).findByRole('list', { name: '留言' });
  return { ...view, section, timeline, served };
}

const MORE = '範例使用者 的留言的更多動作';

/** 「留言」那份只有留言：第一筆就是要編輯的那一則。 */
function draftKeyOfFirstComment(section: HTMLElement): string {
  const entry = section.querySelector<HTMLElement>('[data-activity-id]');
  return draftKey('comment-edit', task.key, entry?.dataset.activityId ?? '');
}

async function chooseFromMenu(user: Awaited<ReturnType<typeof openComments>>['user'], section: HTMLElement, name: string) {
  await user.click(within(section).getByRole('button', { name: MORE }));
  await user.click(await screen.findByRole('menuitem', { name }));
}

describe('留言的動作選單：誰看得到', () => {
  it('作者（有 comment.create）看到「編輯」與「刪除」，刪除在最後、自成一組', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('我寫的')]);

    // Act
    await user.click(within(section).getByRole('button', { name: MORE }));

    // Assert
    const items = await screen.findAllByRole('menuitem');
    expect(items.map((item) => item.textContent)).toEqual(['編輯', '刪除']);
    expect(screen.getByRole('separator')).toBeInTheDocument();
  });

  it('別人的留言：沒有「更多」按鈕（沒有管理專案的權限）', async () => {
    // Arrange & Act
    const { section } = await openComments([created(), commented('別人寫的', { actor: memberRef })]);

    // Assert
    expect(within(section).queryByRole('button', { name: /的留言的更多動作/ })).not.toBeInTheDocument();
  });

  it('管理專案的人看別人的留言：只有「刪除」，沒有「編輯」', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('別人寫的', { actor: memberRef })], { permissions: MANAGER });

    // Act
    await user.click(within(section).getByRole('button', { name: '範例成員 的留言的更多動作' }));

    // Assert
    const items = await screen.findAllByRole('menuitem');
    expect(items.map((item) => item.textContent)).toEqual(['刪除']);
  });

  it('只能看的人（沒有 comment.create）自己的留言也沒有選單', async () => {
    // Arrange & Act
    const { section } = await openComments([created(), commented('我寫的')], { permissions: READER });

    // Assert
    expect(within(section).queryByRole('button', { name: MORE })).not.toBeInTheDocument();
  });

  it('封存的專案：作者與管理者都沒有選單', async () => {
    // Arrange & Act
    const { section } = await openComments([created(), commented('我寫的')], { permissions: MANAGER, isArchived: true });

    // Assert
    expect(within(section).queryByRole('button', { name: MORE })).not.toBeInTheDocument();
  });

  it('回覆也有選單，作者本人能編輯自己的回覆', async () => {
    // Arrange
    const top = commented('最上層', { actor: memberRef });
    const { section } = await openComments([created(), top, commented('我的回覆', { replyToId: top.id })]);

    // Act
    const replies = within(section).getByRole('list', { name: '回覆 範例成員 的留言的回覆' });

    // Assert
    expect(within(replies).getByRole('button', { name: MORE })).toBeInTheDocument();
  });
});

describe('編輯留言', () => {
  it('按「編輯」：就地換成編輯器、內容是現在的 Markdown，焦點在編輯器，說明版本紀錄的去向', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('原本的內容')]);

    // Act
    await chooseFromMenu(user, section, '編輯');

    // Assert
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    expect(editorText(editor)).toBe('原本的內容');
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    expect(within(section).getByText('儲存後，舊的內容會留在版本紀錄裡，看得到這個項目的人都看得到。')).toBeInTheDocument();
  });

  it('開啟時游標在內容的最後面（接著寫）', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('原本的內容')]);

    // Act
    await chooseFromMenu(user, section, '編輯');
    const editor = await findEditor('編輯 範例使用者 的留言', section);

    // Assert
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    const { state } = tiptapOf(editor);
    expect(state.selection.from).toBe(state.doc.content.size - 1);
  });

  it('改了內容再儲存：送出新的內容，卡片換成新的、標「已編輯」，編輯器收起來、焦點回到「更多」', async () => {
    // Arrange
    const { section, user, served } = await openComments([created(), commented('原本的內容')]);
    await chooseFromMenu(user, section, '編輯');
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    await typeInEditor(user, editor, '，加幾個字');

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(section).findByText('原本的內容，加幾個字')).toBeInTheDocument();
    expect(served.edits).toHaveLength(1);
    expect(served.edits[0]?.body).toEqual({ body: '原本的內容，加幾個字' });
    expect(await within(section).findByRole('button', { name: /^已編輯/ })).toBeInTheDocument();
    expect(within(section).queryByRole('form', { name: '編輯 範例使用者 的留言' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: MORE })).toHaveFocus();
    });
  });

  it('打開不改就儲存：送出去的是原文，一個字都不差（伺服器才認得出「沒改」）', async () => {
    // Arrange
    const original = '第一行\n\n- 項目一\n- 項目二\n\n**粗體**與 `code`';
    const { section, user, served } = await openComments([created(), commented(original)]);
    await chooseFromMenu(user, section, '編輯');
    await findEditor('編輯 範例使用者 的留言', section);

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(served.edits).toHaveLength(1);
    });
    expect(served.edits[0]?.body).toEqual({ body: original });
  });

  it('清成空白時不能儲存', async () => {
    // Arrange
    const { section, user, served } = await openComments([created(), commented('原本的內容')]);
    await chooseFromMenu(user, section, '編輯');
    await clearEditor(user, await findEditor('編輯 範例使用者 的留言', section));

    // Act
    const save = within(section).getByRole('button', { name: '儲存' });

    // Assert
    expect(save).toBeDisabled();
    expect(served.edits).toHaveLength(0);
  });

  it('伺服器擋下（內容太長）：錯誤放在編輯器裡，打的內容還在、編輯器沒有關', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('原本的內容')]);
    server.use(http.patch(`/v1/items/${task.key}/comments/:id`, () => failure(422, 'comment.body_too_long', '留言太長了。')));
    await chooseFromMenu(user, section, '編輯');
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    await typeInEditor(user, editor, '新增');

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(section).findByText('留言太長了。')).toBeInTheDocument();
    expect(editorText(editor)).toBe('原本的內容新增');
    expect(within(section).getByRole('form', { name: '編輯 範例使用者 的留言' })).toBeInTheDocument();
  });

  it('儲存時留言已經被刪除了（409 comment.deleted）：說一聲、編輯器收起來，重抓之後是佔位', async () => {
    // Arrange
    const { section, user, timeline } = await openComments([created(), commented('原本的內容')]);
    await chooseFromMenu(user, section, '編輯');
    await typeInEditor(user, await findEditor('編輯 範例使用者 的留言', section), '新增');
    // 在別處被刪除：伺服器那邊已經是刪除的樣子，畫面上還不知道。
    const [, target] = timeline.all();
    if (target === undefined) throw new Error('沒有留言');
    timeline.set([created(), { ...target, comment: null, deletedAt: '2026-11-15T10:00:00Z', deletedBy: ownerRef }]);
    server.use(http.patch(`/v1/items/${task.key}/comments/:id`, () => failure(409, 'comment.deleted', '這則留言已經刪除，先復原才能編輯。')));

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(within(section).queryByRole('form', { name: '編輯 範例使用者 的留言' })).not.toBeInTheDocument();
    });
    expect(await within(section).findByText('這則留言已刪除。')).toBeInTheDocument();
  });
});

describe('編輯留言：取消與草稿', () => {
  it('沒改過時 Esc 就是取消：編輯器收起來、焦點回到「更多」，沒有送出請求', async () => {
    // Arrange
    const { section, user, served } = await openComments([created(), commented('原本的內容')]);
    await chooseFromMenu(user, section, '編輯');
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(within(section).queryByRole('form', { name: '編輯 範例使用者 的留言' })).not.toBeInTheDocument();
    });
    expect(within(section).getByText('原本的內容')).toBeInTheDocument();
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: MORE })).toHaveFocus();
    });
    expect(served.edits).toHaveLength(0);
  });

  it('打了字再按「取消」：丟掉修改、草稿清掉，卡片還是原本的內容', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('原本的內容')]);
    await chooseFromMenu(user, section, '編輯');
    await typeInEditor(user, await findEditor('編輯 範例使用者 的留言', section), '多打的');
    const key = draftKeyOfFirstComment(section);
    expect(sessionStorage.getItem(key)).toBe('原本的內容多打的');

    // Act
    await user.click(within(section).getByRole('button', { name: '取消' }));

    // Assert
    expect(await within(section).findByText('原本的內容')).toBeInTheDocument();
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  it('改回和原本一樣就不存草稿', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('原本的內容')]);
    await chooseFromMenu(user, section, '編輯');
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    const key = draftKeyOfFirstComment(section);

    // Act
    await typeInEditor(user, editor, 'X');
    await user.keyboard('{Backspace}');

    // Assert
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  it('重新整理（重新掛上）之後：沒儲存的編輯還在，編輯器開著並說明已還原，不搶焦點，「捨棄」回到原本的內容', async () => {
    // Arrange
    const target = commented('原本的內容');
    sessionStorage.setItem(draftKey('comment-edit', task.key, target.id), '原本的內容，改到一半');

    // Act
    const { section, user } = await openComments([created(), target]);

    // Assert
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    expect(editorText(editor)).toBe('原本的內容，改到一半');
    expect(editor).not.toHaveFocus();
    expect(await within(section).findByText('已還原上次沒送出的內容。')).toBeInTheDocument();
    await user.click(await within(section).findByRole('button', { name: '捨棄沒儲存的編輯' }));
    await waitFor(() => {
      expect(editorText(editor)).toBe('原本的內容');
    });
    expect(sessionStorage.getItem(draftKey('comment-edit', task.key, target.id))).toBeNull();
  });

  it('沒有編輯權限的人不還原別人（或自己權限被拿掉之後）留下的草稿', async () => {
    // Arrange
    const target = commented('原本的內容', { actor: memberRef });
    sessionStorage.setItem(draftKey('comment-edit', task.key, target.id), '草稿');

    // Act
    const { section } = await openComments([created(), target]);

    // Assert
    expect(within(section).queryByRole('form', { name: '編輯 範例使用者 的留言' })).not.toBeInTheDocument();
  });
});

describe('編輯中收到即時更新', () => {
  it('活動重抓、換了新的物件：編輯器不關掉、打的內容不被蓋掉', async () => {
    // Arrange
    const { section, user, timeline, queryClient } = await openComments([created(), commented('原本的內容')]);
    await chooseFromMenu(user, section, '編輯');
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    await typeInEditor(user, editor, '，打到一半');

    // Act：別的分頁多了一則留言，即時更新讓活動失效、重抓。
    timeline.add(commented('別人新的留言', { actor: memberRef }));
    await queryClient.invalidateQueries({ queryKey: queryKeys.activity(task.key) });

    // Assert
    expect(await within(section).findByText('別人新的留言')).toBeInTheDocument();
    expect(within(section).getByRole('form', { name: '編輯 範例使用者 的留言' })).toBeInTheDocument();
    expect(editorText(editor)).toBe('原本的內容，打到一半');
  });
});

describe('已編輯與留言的版本', () => {
  it('編輯過的留言時間後面有「已編輯」，title 是編輯的時間；沒編輯過的沒有', async () => {
    // Arrange & Act
    const { section } = await openComments([created(), editedComment('改過的'), commented('沒改過的')]);

    // Assert
    const marks = within(section).getAllByRole('button', { name: /^已編輯/ });
    expect(marks).toHaveLength(1);
    expect(marks[0]).toHaveAttribute('title', expect.stringContaining('編輯於 2026/11/13'));
  });

  it('點「已編輯」開「留言的版本」：新的在前、現在的標「目前」，內容用 Markdown 顯示', async () => {
    // Arrange
    const { section, user } = await openComments([created(), commented('第一版')]);
    await chooseFromMenu(user, section, '編輯');
    const editor = await findEditor('編輯 範例使用者 的留言', section);
    await clearEditor(user, editor);
    await typeInEditor(user, editor, '第二版');
    await user.click(within(section).getByRole('button', { name: '儲存' }));
    await user.click(await within(section).findByRole('button', { name: /^已編輯/ }));

    // Act
    const dialog = await screen.findByRole('dialog', { name: '留言的版本' });
    const versions = await within(dialog).findAllByRole('listitem');

    // Assert
    expect(versions).toHaveLength(2);
    expect(within(versions[0] as HTMLElement).getByText('第 2 版')).toBeInTheDocument();
    expect(within(versions[0] as HTMLElement).getByText('目前')).toBeInTheDocument();
    expect(within(versions[0] as HTMLElement).getByText('第二版')).toBeInTheDocument();
    expect(within(versions[1] as HTMLElement).getByText('第 1 版')).toBeInTheDocument();
    expect(within(versions[1] as HTMLElement).getByText('第一版')).toBeInTheDocument();
    expect(within(versions[1] as HTMLElement).queryByText('目前')).not.toBeInTheDocument();
  });

  it('讀取中先給骨架；關掉對話框之後焦點回到「已編輯」', async () => {
    // Arrange
    const { section, user } = await openComments([created(), editedComment('改過的')]);
    const mark = within(section).getByRole('button', { name: /^已編輯/ });
    await user.click(mark);
    const dialog = await screen.findByRole('dialog', { name: '留言的版本' });
    await within(dialog).findByRole('list', { name: '版本' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: '留言的版本' })).not.toBeInTheDocument();
    });
    expect(mark).toHaveFocus();
  });

  it('讀不到版本：說明失敗並給「重試」，重試之後讀到', async () => {
    // Arrange
    const { section, user } = await openComments([created(), editedComment('改過的')]);
    server.use(http.get(`/v1/items/${task.key}/comments/:id/revisions`, () => failure(500, 'server.error', '伺服器暫時無法處理。'), { once: true }));
    await user.click(within(section).getByRole('button', { name: /^已編輯/ }));
    const dialog = await screen.findByRole('dialog', { name: '留言的版本' });
    expect(await within(dialog).findByText('版本載入失敗')).toBeInTheDocument();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '重試' }));

    // Assert
    expect(await within(dialog).findByRole('list', { name: '版本' })).toBeInTheDocument();
  });
});
