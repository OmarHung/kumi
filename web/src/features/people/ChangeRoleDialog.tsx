import { useState, type SubmitEvent } from 'react';
import { errorMessage } from '../../api/http';
import type { OrganizationRole, Person } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { SelectField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import type { SelectOption } from '../../components/Select/Select';
import { ORGANIZATION_ROLE_DESCRIPTIONS, ORGANIZATION_ROLE_LABELS } from '../../domain/labels';
import { ORGANIZATION_ROLES, roleUnavailableReason } from './personAccess';
import { useSetPersonRole } from './personMutations';
import styles from './PeoplePage.module.css';

interface ChangeRoleDialogProps {
  readonly person: Person;
  readonly onClose: () => void;
  /** 改好了（已經是那個角色時也一樣）：呼叫端關上對話框、在頁面上方說明結果。 */
  readonly onChanged: (person: Person) => void;
}

/** 不能選的原因不只寫在選項裡（選單沒打開時看不到）：不是擁有者的人打開時，下拉下面就先說明。 */
const OWNER_ONLY_NOTE = '擁有者與管理員只有擁有者能設定。';

function isOrganizationRole(value: string): value is OrganizationRole {
  return ORGANIZATION_ROLES.some((role) => role === value);
}

/**
 * 「變更角色…」：一個角色的下拉，選到哪個就在下面寫出它是什麼。管理員不能設定擁有者與管理員：那兩個選項標
 * aria-disabled 並寫出原因（伺服器的檢查才算數）。已經是那個角色時「儲存」照常送出，伺服器照樣成功。
 * 送出中 Esc 與「取消」都不作用；儲存鈕只標 aria-disabled，焦點留在它身上，失敗之後 Esc 與 Tab 照常在對話框裡。
 */
export function ChangeRoleDialog({ person, onClose, onChanged }: ChangeRoleDialogProps) {
  const user = useCurrentUser();
  const [role, setRole] = useState<OrganizationRole>(person.role);
  const change = useSetPersonRole();
  const options: SelectOption[] = ORGANIZATION_ROLES.map((value) => ({
    value,
    label: ORGANIZATION_ROLE_LABELS[value],
    disabledReason: roleUnavailableReason(user, value),
  }));
  const close = change.isPending ? () => undefined : onClose;

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    // 送出中按鈕只標 aria-disabled（焦點留在上面），再按一次照樣觸發 submit：這裡擋下重送。
    if (change.isPending) return;
    change.mutate({ personId: person.personId, role }, { onSuccess: onChanged });
  }

  return (
    <Dialog title={`變更「${person.displayName}」的角色`} onClose={close}>
      <form className={styles.form} noValidate onSubmit={handleSubmit}>
        <FormAlert message={change.isError ? errorMessage(change.error) : null} />
        <SelectField
          label="組織角色"
          hint={user.role === 'owner' ? ORGANIZATION_ROLE_DESCRIPTIONS[role] : `${ORGANIZATION_ROLE_DESCRIPTIONS[role]}${OWNER_ONLY_NOTE}`}
          value={role}
          options={options}
          onChange={(next) => {
            if (isOrganizationRole(next)) setRole(next);
          }}
        />
        <DialogFormActions submitLabel="儲存" pendingLabel="儲存中…" isPending={change.isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
