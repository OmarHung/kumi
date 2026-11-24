import { describe, expect, it } from 'vitest';
import type { Permission } from '../../api/blueprint';
import { commented, deletedComment, memberRef, ownerRef } from '../../test/activity';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { commentAbilities } from './commentAbilities';

const AUTHOR_ID = exampleOwner.personId;
const OTHER_ID = exampleMember.personId;
const WRITER: readonly Permission[] = ['item.read', 'comment.create'];
const READER: readonly Permission[] = ['item.read'];
const MANAGER: readonly Permission[] = ['item.read', 'project.manage'];

function abilities(activity: Parameters<typeof commentAbilities>[0]['activity'], personId: string, permissions: readonly Permission[], isArchived = false) {
  return commentAbilities({ activity, personId, permissions, isArchived });
}

describe('留言的權限（誰能編輯、刪除、復原）', () => {
  it('作者且有 comment.create：能編輯、能刪除、不能復原（沒刪除）', () => {
    // Act
    const result = abilities(commented('內容', { actor: ownerRef }), AUTHOR_ID, WRITER);

    // Assert
    expect(result).toEqual({ canEdit: true, canDelete: true, canRestore: false, isAuthor: true });
  });

  it('作者但沒有 comment.create（只能看）：什麼都不能做', () => {
    // Act
    const result = abilities(commented('內容', { actor: ownerRef }), AUTHOR_ID, READER);

    // Assert
    expect(result).toMatchObject({ canEdit: false, canDelete: false });
  });

  it('別人的留言：有 comment.create 也不能編輯、不能刪除', () => {
    // Act
    const result = abilities(commented('內容', { actor: memberRef }), AUTHOR_ID, WRITER);

    // Assert
    expect(result).toMatchObject({ canEdit: false, canDelete: false, isAuthor: false });
  });

  it('管理專案的人：不能編輯別人的留言，但能刪除', () => {
    // Act
    const result = abilities(commented('內容', { actor: memberRef }), AUTHOR_ID, MANAGER);

    // Assert
    expect(result).toMatchObject({ canEdit: false, canDelete: true });
  });

  it('封存的專案：作者與管理者都一樣什麼都不能做（project.manage 在封存的專案還在，要自己擋）', () => {
    // Act
    const asAuthor = abilities(commented('內容', { actor: ownerRef }), AUTHOR_ID, WRITER, true);
    const asManager = abilities(deletedComment({ actor: memberRef, deletedBy: ownerRef }), AUTHOR_ID, MANAGER, true);

    // Assert
    expect(asAuthor).toMatchObject({ canEdit: false, canDelete: false });
    expect(asManager).toMatchObject({ canRestore: false });
  });

  it('作者自己刪的：作者（有 comment.create）與管理者能復原，只能看的作者不能', () => {
    // Arrange
    const selfDeleted = deletedComment({ actor: ownerRef, deletedBy: ownerRef });

    // Act & Assert
    expect(abilities(selfDeleted, AUTHOR_ID, WRITER).canRestore).toBe(true);
    expect(abilities(selfDeleted, OTHER_ID, MANAGER).canRestore).toBe(true);
    expect(abilities(selfDeleted, AUTHOR_ID, READER).canRestore).toBe(false);
    expect(abilities(selfDeleted, OTHER_ID, WRITER).canRestore).toBe(false);
  });

  it('管理者刪別人的留言：只有管理者能復原，作者自己不能把它找回來', () => {
    // Arrange
    const removedByManager = deletedComment({ actor: ownerRef, deletedBy: memberRef });

    // Act & Assert
    expect(abilities(removedByManager, AUTHOR_ID, WRITER).canRestore).toBe(false);
    expect(abilities(removedByManager, OTHER_ID, MANAGER).canRestore).toBe(true);
  });

  it('刪除的留言不能編輯也不能再刪除；沒有登入的人什麼都不能做', () => {
    // Act
    const deleted = abilities(deletedComment({ actor: ownerRef }), AUTHOR_ID, WRITER);
    const anonymous = commentAbilities({ activity: commented('x'), personId: null, permissions: WRITER, isArchived: false });

    // Assert
    expect(deleted).toMatchObject({ canEdit: false, canDelete: false });
    expect(anonymous).toMatchObject({ canEdit: false, canDelete: false, canRestore: false });
  });
});
