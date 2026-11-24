import { useState } from 'react';
import { errorMessage } from '../../api/http';
import type { Member } from '../../api/types';
import { SelectField } from '../../components/Field/Field';
import type { SelectOption } from '../../components/Select/Select';
import { useSetMemberRole, type MemberFeedback, type RoleOption } from './memberRoles';
import styles from './MembersDialog.module.css';

export interface RoleSelectProps extends MemberFeedback {
  readonly projectKey: string;
  readonly member: Member;
  readonly options: readonly RoleOption[];
  /** 最後一位管理員：只能是管理員，其他選項不能選；表格（清單）下面那段說明的 id。 */
  readonly lockedReasonId?: string;
}

/**
 * 角色的選單值：選了先換成新的角色（送出中 aria-busy、不收別的值），失敗時退回原本的角色。
 * 後端的角色變了（重新讀到專案內容），選單跟著換。
 */
function useRoleDraft({ projectKey, member, options, onSaved, onFailed }: RoleSelectProps) {
  const mutation = useSetMemberRole(projectKey);
  const [draft, setDraft] = useState(member.role);
  const [syncedRole, setSyncedRole] = useState(member.role);
  if (member.role !== syncedRole) {
    setSyncedRole(member.role);
    setDraft(member.role);
  }

  function save(role: string) {
    setDraft(role);
    const label = options.find((option) => option.value === role)?.label ?? role;
    mutation.mutate(
      { personId: member.personId, role },
      {
        onSuccess: () => {
          onSaved(`已把「${member.displayName}」的角色改成「${label}」。`);
        },
        onError: (error) => {
          setDraft(member.role);
          onFailed(errorMessage(error));
        },
      },
    );
  }

  return { draft, save, isPending: mutation.isPending };
}

/**
 * 最後一位管理員時，管理員以外的角色是不能選的選項（規範 7.3）。選項裡的原因是短語：完整的句子
 * （LAST_ADMIN_REASON）每個選項都寫一次太長，只放在表格下面那一段，選單用 aria-describedby 連過去。
 */
const LAST_ADMIN_OPTION_REASON = '最後一位管理員不能換角色';

function roleSelectOptions(options: readonly RoleOption[], isLocked: boolean): SelectOption[] {
  return options.map((option) =>
    isLocked && option.value !== 'admin' ? { ...option, disabledReason: LAST_ADMIN_OPTION_REASON } : option,
  );
}

/**
 * 一個成員的角色：馬上生效的下拉（docs/DESIGN.md 7.3），選了就儲存，不分滑鼠或鍵盤（方向鍵只移動反白）。
 * 長相是共用的小型下拉（SelectField size="sm"，和工具列、表格列上的下拉一樣）；名稱「某某的角色」是只給報讀軟體的標籤。
 * 最後一位管理員的原因兩邊都有：選項裡（不能選的那幾個，短語）與表格下面那一段（完整的句子，選單用 aria-describedby 連過去）。
 */
export function RoleSelect(props: RoleSelectProps) {
  const { member, options, lockedReasonId } = props;
  const { draft, save, isPending } = useRoleDraft(props);
  return (
    <SelectField
      label={`${member.displayName}的角色`}
      isLabelHidden
      size="sm"
      className={styles.roleField}
      aria-busy={isPending}
      aria-describedby={lockedReasonId}
      value={draft}
      options={roleSelectOptions(options, lockedReasonId !== undefined)}
      onChange={save}
    />
  );
}
