import type { ReactNode } from 'react';
import type { Member, ProjectDetail, SessionUser } from '../../api/types';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Avatar } from '../../components/Avatar/Avatar';
import { Badge } from '../../components/Badge/Badge';
import { projectRoleLabel } from '../../domain/labels';
import { usePersonTitles } from '../people/usePersonTitles';
import { isLastAdmin, roleOptions, type MemberFeedback } from './memberRoles';
import { RemoveMemberButton } from './RemoveMember';
import { RoleSelect } from './RoleSelect';
import styles from './MembersDialog.module.css';

export interface MemberPartsProps extends MemberFeedback {
  readonly detail: ProjectDetail;
  readonly member: Member;
  readonly canManage: boolean;
  readonly idPrefix: string;
  /** 最後一位管理員的說明（在表格或清單下面一段）的 id：被擋住的角色選單與「移除」用 aria-describedby 連過去。 */
  readonly lastAdminNoteId: string;
  readonly onRemove: (member: Member) => void;
}

/** 一位成員畫成表格的一列或清單的一筆，內容都一樣：頭像、名稱、角色（或選單）、「移除」。 */
export interface MemberParts {
  /** 頭像：旁邊有名字，只是裝飾。 */
  readonly avatar: ReactNode;
  /** 名字，自己後面標「你」。 */
  readonly name: ReactNode;
  /** 名稱加職稱的純文字：表格裡被截斷時放在 title，看得到完整的內容。 */
  readonly nameText: string;
  readonly role: ReactNode;
  readonly remove: ReactNode;
}

/** 移除之後焦點要去的按鈕：表格與清單用同一個 id，兩種畫法都找得到。 */
export function removeButtonId(idPrefix: string, member: Member): string {
  return `${idPrefix}-remove-${member.personId}`;
}

/**
 * 角色與「移除」會不會被「最後一位管理員」擋住。組織管理員不擋（3A.1）：他們在每個專案都有全部權限，
 * 不會因此管不到；伺服器也放行。
 */
export function isLockedAdmin(user: SessionUser, detail: ProjectDetail, member: Member, canManage: boolean): boolean {
  return canManage && !isOrganizationAdmin(user) && isLastAdmin(detail.members, member);
}

export function useMemberParts(props: MemberPartsProps, avatarSize: 'sm' | 'md'): MemberParts {
  const { detail, member, canManage, idPrefix, lastAdminNoteId, onRemove, onSaved, onFailed } = props;
  const user = useCurrentUser();
  const jobTitle = usePersonTitles().get(member.personId);
  const isLocked = isLockedAdmin(user, detail, member, canManage);
  const reasonId = isLocked ? lastAdminNoteId : undefined;
  const role = canManage ? (
    <RoleSelect
      projectKey={detail.project.key}
      member={member}
      options={roleOptions(detail.blueprint)}
      lockedReasonId={reasonId}
      onSaved={onSaved}
      onFailed={onFailed}
    />
  ) : (
    projectRoleLabel(member.role, detail.blueprint.roles)
  );
  const remove = canManage ? (
    <RemoveMemberButton id={removeButtonId(idPrefix, member)} member={member} reasonId={reasonId} onRemove={onRemove} />
  ) : null;
  const name = (
    <>
      {member.displayName}
      {member.personId === user.personId && <> <Badge>你</Badge></>}
      {jobTitle !== undefined && <> <span className={styles.jobTitle}>{jobTitle}</span></>}
    </>
  );
  const nameText = jobTitle === undefined ? member.displayName : `${member.displayName} · ${jobTitle}`;
  return { avatar: <Avatar name={member.displayName} size={avatarSize} decorative />, name, nameText, role, remove };
}
