import type { Permission } from '../../api/blueprint';
import type { Activity } from '../../api/types';
import { useSession } from '../../auth/session';
import { useProjectTable } from '../item-table/ProjectTableContext';

/** 一則留言現在能對它做哪些事。畫面只是不開放，真正的檢查在後端（HANDOFF-comment-edit 第 2 節）。 */
export interface CommentAbilities {
  /** 作者本人而且有 comment.create；刪除的留言要先復原。 */
  readonly canEdit: boolean;
  /** 作者本人有 comment.create，或有 project.manage（可以刪別人的）。 */
  readonly canDelete: boolean;
  /** 作者自己刪的：作者本人有 comment.create 或有 project.manage；別人刪的：只有 project.manage。 */
  readonly canRestore: boolean;
  readonly isAuthor: boolean;
}

export interface AbilityInputs {
  readonly activity: Activity;
  readonly personId: string | null;
  readonly permissions: readonly Permission[];
  readonly isArchived: boolean;
}

const NOTHING: CommentAbilities = { canEdit: false, canDelete: false, canRestore: false, isAuthor: false };

/**
 * 封存的專案一律不行：project.manage 在封存的專案還在，所以要自己擋（唯讀只有一條規則）。
 * 只有留言（含回覆）有這些動作。
 */
export function commentAbilities({ activity, personId, permissions, isArchived }: AbilityInputs): CommentAbilities {
  if (activity.kind !== 'commented' || isArchived || personId === null) return NOTHING;
  const isAuthor = activity.actor.personId === personId;
  const mayWrite = isAuthor && permissions.includes('comment.create');
  const mayManage = permissions.includes('project.manage');
  if (activity.deletedAt !== null) {
    const deletedByAuthor = activity.deletedBy?.personId === activity.actor.personId;
    return { canEdit: false, canDelete: false, canRestore: deletedByAuthor ? mayWrite || mayManage : mayManage, isAuthor };
  }
  return { canEdit: mayWrite, canDelete: mayWrite || mayManage, canRestore: false, isAuthor };
}

/** 目前的使用者在這個專案對這則留言能做的事。 */
export function useCommentAbilities(activity: Activity): CommentAbilities {
  const { detail } = useProjectTable();
  const session = useSession();
  return commentAbilities({
    activity,
    personId: session.data?.personId ?? null,
    permissions: detail.myPermissions,
    isArchived: detail.project.archivedAt !== null,
  });
}
