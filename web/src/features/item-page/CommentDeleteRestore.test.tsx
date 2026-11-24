import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Permission } from '../../api/blueprint';
import type { Activity, SessionUser } from '../../api/types';
import { commented, created, deletedComment, editedComment, memberRef, ownerRef, serveActivity, serveCommentEditing } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner, smkDetail, smkItem, smkProject } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

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

type Opened = Awaited<ReturnType<typeof openComments>>;

async function openDeleteDialog({ user, section }: Pick<Opened, 'user' | 'section'>, menuName: string): Promise<HTMLElement> {
  await user.click(within(section).getByRole('button', { name: menuName }));
  await user.click(await screen.findByRole('menuitem', { name: '刪除' }));
  return screen.findByRole('dialog', { name: '刪除這則留言？' });
}

const OWN_MENU = '範例使用者 的留言的更多動作';
const MEMBER_MENU = '範例成員 的留言的更多動作';

describe('刪除留言：確認框', () => {
  it('說明可以復原、通知會一起刪掉且復原不會回來、已寄出的信收不回來', async () => {
    // Arrange
    const opened = await openComments([created(), commented('我寫的')]);

    // Act
    const dialog = await openDeleteDialog(opened, OWN_MENU);

    // Assert
    expect(within(dialog).getByText(/刪除之後可以復原。這則留言的通知會一起刪掉，復原也不會回來；已經寄出的通知信收不回來。/)).toBeInTheDocument();
    expect(within(dialog).queryByText(/附件/)).not.toBeInTheDocument();
    expect(within(dialog).queryByText(/只有管理專案的人能復原/)).not.toBeInTheDocument();
  });

  it('內容裡有 attachment: 時提醒附件不會一起刪掉', async () => {
    // Arrange
    const opened = await openComments([created(), commented('看圖 ![圖](attachment:0190a000-0000-7000-8000-0000000000ff)')]);

    // Act
    const dialog = await openDeleteDialog(opened, OWN_MENU);

    // Assert
    expect(within(dialog).getByText(/貼在留言裡的圖片與檔案是項目的附件，不會一起刪掉。/)).toBeInTheDocument();
  });

  it('刪除別人的留言（管理專案的人）：說這是誰的留言、刪除之後只有管理專案的人能復原', async () => {
    // Arrange
    const opened = await openComments([created(), commented('別人的', { actor: memberRef })], { permissions: MANAGER });

    // Act
    const dialog = await openDeleteDialog(opened, MEMBER_MENU);

    // Assert
    expect(within(dialog).getByText(/這是 範例成員 的留言，刪除之後只有管理專案的人能復原。/)).toBeInTheDocument();
  });

  it('按「取消」：什麼都不送、留言還在', async () => {
    // Arrange
    const opened = await openComments([created(), commented('我寫的')]);
    const dialog = await openDeleteDialog(opened, OWN_MENU);

    // Act
    await opened.user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(opened.served.deletes).toHaveLength(0);
    expect(within(opened.section).getByText('我寫的')).toBeInTheDocument();
  });

  it('伺服器擋下：訊息放在確認框裡，留言還在', async () => {
    // Arrange
    const opened = await openComments([created(), commented('我寫的')]);
    server.use(http.delete(`/v1/items/${task.key}/comments/:id`, () => failure(403, 'comment.not_author', '只能刪除自己的留言。')));
    const dialog = await openDeleteDialog(opened, OWN_MENU);

    // Act
    await opened.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(dialog).findByText('只能刪除自己的留言。')).toBeInTheDocument();
    expect(within(opened.section).getByText('我寫的')).toBeInTheDocument();
  });
});

describe('刪除留言：佔位', () => {
  it('作者刪除自己的留言：留在原位變成「這則留言已刪除。」，沒有選單、沒有「已編輯」、沒有「回覆」，焦點在佔位上', async () => {
    // Arrange
    const opened = await openComments([created(), editedComment('我寫的')]);
    const dialog = await openDeleteDialog(opened, OWN_MENU);

    // Act
    await opened.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    const placeholder = await within(opened.section).findByText('這則留言已刪除。');
    expect(opened.served.deletes).toHaveLength(1);
    expect(within(opened.section).queryByText('我寫的')).not.toBeInTheDocument();
    expect(within(opened.section).queryByRole('button', { name: OWN_MENU })).not.toBeInTheDocument();
    expect(within(opened.section).queryByRole('button', { name: /^已編輯/ })).not.toBeInTheDocument();
    expect(within(opened.section).queryByRole('button', { name: /^回覆/ })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(placeholder.closest('[data-deleted]')).toHaveFocus();
    });
  });

  it('管理專案的人刪別人的留言：寫「已由 某某 刪除」，管理者看得到「復原」', async () => {
    // Arrange
    const opened = await openComments([created(), commented('別人的', { actor: memberRef })], { permissions: MANAGER });
    const dialog = await openDeleteDialog(opened, MEMBER_MENU);

    // Act
    await opened.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(opened.section).findByText('這則留言已由 範例使用者 刪除。')).toBeInTheDocument();
    expect(within(opened.section).getByRole('button', { name: '復原 範例成員 的留言' })).toBeInTheDocument();
  });

  it('作者看自己被管理者刪掉的留言：看不到「復原」；管理者看得到', async () => {
    // Arrange
    const removed = deletedComment({ actor: ownerRef, deletedBy: memberRef });

    // Act
    const asAuthor = await openComments([created(), removed]);

    // Assert
    expect(await within(asAuthor.section).findByText('這則留言已由 範例成員 刪除。')).toBeInTheDocument();
    expect(within(asAuthor.section).queryByRole('button', { name: /^復原/ })).not.toBeInTheDocument();
  });

  it('作者自己刪的：只能看的作者、封存的專案都沒有「復原」', async () => {
    // Arrange
    const selfDeleted = deletedComment({ actor: ownerRef });

    // Act
    const reader = await openComments([created(), selfDeleted], { permissions: READER });

    // Assert
    expect(await within(reader.section).findByText('這則留言已刪除。')).toBeInTheDocument();
    expect(within(reader.section).queryByRole('button', { name: /^復原/ })).not.toBeInTheDocument();
  });

  it('封存的專案：管理者也看不到「復原」', async () => {
    // Arrange & Act
    const { section } = await openComments([created(), deletedComment({ actor: memberRef, deletedBy: memberRef })], { permissions: MANAGER, isArchived: true });

    // Assert
    expect(await within(section).findByText('這則留言已刪除。')).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /^復原/ })).not.toBeInTheDocument();
  });

  it('最上層的留言刪除之後：底下的回覆留著，沒有「回覆」，也沒有回覆框', async () => {
    // Arrange
    const top = deletedComment({ actor: memberRef, deletedBy: memberRef });
    const reply = commented('底下的回覆', { actor: memberRef, replyToId: top.id });

    // Act
    const { section } = await openComments([created(), top, reply]);

    // Assert
    expect(await within(section).findByText('底下的回覆')).toBeInTheDocument();
    expect(within(section).getByText('這則留言已刪除。')).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /^回覆 / })).not.toBeInTheDocument();
  });

  it('回覆也能刪除，刪除之後同樣留在原位', async () => {
    // Arrange
    const top = commented('最上層', { actor: memberRef });
    const opened = await openComments([created(), top, commented('我的回覆', { replyToId: top.id })]);
    const replies = within(opened.section).getByRole('list', { name: '回覆 範例成員 的留言的回覆' });
    await opened.user.click(within(replies).getByRole('button', { name: OWN_MENU }));
    await opened.user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    const dialog = await screen.findByRole('dialog', { name: '刪除這則留言？' });

    // Act
    await opened.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(replies).findByText('這則留言已刪除。')).toBeInTheDocument();
    expect(within(opened.section).getByText('最上層')).toBeInTheDocument();
  });
});

describe('復原留言', () => {
  it('按「復原」：一按就復原、不另外確認，內容、「已編輯」回來，選單回來，焦點放到留言上', async () => {
    // Arrange
    const opened = await openComments([created(), editedComment('我寫的')]);
    const dialog = await openDeleteDialog(opened, OWN_MENU);
    await opened.user.click(within(dialog).getByRole('button', { name: '刪除' }));
    const restore = await within(opened.section).findByRole('button', { name: '復原 範例使用者 的留言' });

    // Act
    await opened.user.click(restore);

    // Assert
    expect(await within(opened.section).findByText('我寫的')).toBeInTheDocument();
    expect(opened.served.restores).toHaveLength(1);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(opened.section).getByRole('button', { name: /^已編輯/ })).toBeInTheDocument();
    expect(within(opened.section).getByRole('button', { name: OWN_MENU })).toBeInTheDocument();
    await waitFor(() => {
      expect(within(opened.section).getByText('我寫的').closest('[data-comment-card]')).toHaveFocus();
    });
    expect(within(opened.section).getByRole('button', { name: '回覆 範例使用者 的留言' })).toBeInTheDocument();
  });

  it('復原失敗：把伺服器的訊息放在那一行，佔位還在', async () => {
    // Arrange
    const { section, user } = await openComments([created(), deletedComment({ actor: ownerRef })]);
    server.use(http.post(`/v1/items/${task.key}/comments/:id/restore`, () => failure(403, 'comment.not_author', '只能復原自己的留言。')));

    // Act
    await user.click(await within(section).findByRole('button', { name: '復原 範例使用者 的留言' }));

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('只能復原自己的留言。');
    expect(within(section).getByText('這則留言已刪除。')).toBeInTheDocument();
  });

  it('管理者復原別人刪掉的留言', async () => {
    // Arrange
    const viewer = exampleMember;
    const removed = deletedComment({ actor: ownerRef, deletedBy: memberRef });
    const { section, user } = await openComments([created(), removed], { viewer, permissions: MANAGER });

    // Act
    await user.click(await within(section).findByRole('button', { name: '復原 範例使用者 的留言' }));

    // Assert
    await waitFor(() => {
      expect(within(section).queryByText(/這則留言已/)).not.toBeInTheDocument();
    });
  });
});
