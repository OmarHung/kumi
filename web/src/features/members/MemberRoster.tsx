import { useId, useState } from 'react';
import type { Member, ProjectDetail } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Notice } from '../../components/Notice/Notice';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { useIsCompact } from '../../lib/useMediaQuery';
import { MemberList } from './MemberList';
import { isLockedAdmin, removeButtonId } from './MemberParts';
import { useRemoveMember, type MemberFeedback } from './memberRoles';
import { MemberTable } from './MemberTable';
import { LAST_ADMIN_REASON, RemoveMemberDialog } from './RemoveMember';

/** 表格與清單共用的內容：誰是成員、能不能管理、按了「移除」之後交給誰。 */
export interface MemberRosterProps extends MemberFeedback {
  readonly detail: ProjectDetail;
  readonly canManage: boolean;
  readonly idPrefix: string;
  /** 最後一位管理員的說明的 id（見 MemberParts）。 */
  readonly lastAdminNoteId: string;
  readonly onRemove: (member: Member) => void;
}

/**
 * 移除的確認放在這一層：成功時那一列先從快取拿掉，確認對話框不能跟著那一列一起卸下。
 * 成功後焦點到下一位（沒有就上一位）的「移除」，都沒有了到對話框的「關閉」。
 */
function useMemberRemoval(detail: ProjectDetail, idPrefix: string, fallbackFocusId: string, onSaved: MemberFeedback['onSaved']) {
  const [removing, setRemoving] = useState<Member | null>(null);
  const removal = useRemoveMember(detail.project.key);

  function confirm(member: Member) {
    const index = detail.members.findIndex((candidate) => candidate.personId === member.personId);
    const neighbours = [detail.members[index + 1], detail.members[index - 1]].flatMap((other) => (other === undefined ? [] : [other]));
    const targets = [...neighbours.map((other) => removeButtonId(idPrefix, other)), fallbackFocusId];
    removal.mutate(member, {
      onSuccess: () => {
        updateThenFocus(() => {
          setRemoving(null);
          onSaved(`已把「${member.displayName}」移出專案。他負責的項目留著原本的負責人。`);
        }, targets);
      },
    });
  }

  const dialog =
    removing === null ? null : (
      <RemoveMemberDialog
        member={removing}
        mutation={removal}
        onConfirm={() => { confirm(removing); }}
        onCancel={() => {
          removal.reset();
          setRemoving(null);
        }}
      />
    );
  return { start: setRemoving, dialog };
}

interface MemberRosterOwnProps extends MemberFeedback {
  readonly detail: ProjectDetail;
  readonly canManage: boolean;
  /** 移除最後剩下的那一位之後，焦點去的地方（對話框的「關閉」）。 */
  readonly fallbackFocusId: string;
}

/**
 * 成員與角色。有 project.manage 的人可以改角色、移除成員；不是組織管理員時，最後一位管理員不能移除也不能換成別的角色
 * （伺服器 409 為準）。寬螢幕是表格，窄螢幕換成清單（換元件，不是把表格擠小：docs/DESIGN.md 6.3）。
 */
export function MemberRoster({ detail, canManage, fallbackFocusId, onSaved, onFailed }: MemberRosterOwnProps) {
  const idPrefix = useId();
  const user = useCurrentUser();
  const isCompact = useIsCompact();
  const removal = useMemberRemoval(detail, idPrefix, fallbackFocusId, onSaved);
  const lastAdminNoteId = `${idPrefix}-last-admin`;
  const props: MemberRosterProps = { detail, canManage, idPrefix, lastAdminNoteId, onRemove: removal.start, onSaved, onFailed };
  const hasLockedAdmin = detail.members.some((member) => isLockedAdmin(user, detail, member, canManage));
  return (
    <>
      {isCompact ? <MemberList {...props} /> : <MemberTable {...props} />}
      {/*
        最後一位管理員為什麼不能換角色、不能移除：放在表格（清單）下面一段，那一列才維持一行。
        一開始就在、不必唸（role="none"）；被擋住的角色選單與「移除」用 aria-describedby 連到這裡。
      */}
      {hasLockedAdmin && (
        <Notice tone="info" role="none" id={lastAdminNoteId}>
          {LAST_ADMIN_REASON}
        </Notice>
      )}
      {removal.dialog}
    </>
  );
}
