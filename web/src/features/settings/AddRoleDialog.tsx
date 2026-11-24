import { useState, type SubmitEvent } from 'react';
import type { Permission } from '../../api/blueprint';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { TextField } from '../../components/Field/Field';
import { roleKeyError } from '../../domain/settingsChecks';
import { KEY_HINT, nameError } from '../../domain/settingsNames';
import { PERMISSION_ORDER } from '../../domain/labels';
import { addRole, BUILT_IN_ROLES, DEFAULT_ROLE_PERMISSIONS, exclusiveReadPermissions } from '../../domain/settingsRoles';
import { suggestKey } from '../../domain/settingsValidation';
import { Checklist } from './Checklist';
import { PERMISSION_OPTIONS } from './RoleRow';
import { useSettingsDialog, useSettingsEditor } from './SettingsContext';
import styles from './Settings.module.css';

interface RoleForm {
  readonly name: string;
  /** null 表示還沒自己改過 key，跟著名稱給建議值。 */
  readonly key: string | null;
  readonly permissions: readonly Permission[];
}

interface FormErrors {
  readonly name?: string;
  readonly key?: string;
}

/** 新增自訂角色：名稱、key、權限（預設和「檢視者」一樣）。加進草稿，還沒送出。 */
export function AddRoleDialog({ onClose }: { readonly onClose: () => void }) {
  const editor = useSettingsEditor();
  useSettingsDialog();
  const [form, setForm] = useState<RoleForm>({ name: '', key: null, permissions: DEFAULT_ROLE_PERMISSIONS });
  const [errors, setErrors] = useState<FormErrors>({});
  const taken = new Set([...BUILT_IN_ROLES, ...(editor.base.roles ?? []), ...editor.draft.roles].map((role) => role.key));
  const key = form.key ?? suggestKey(form.name, 'role', taken);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = { name: nameError(form.name), key: roleKeyError(editor.base, editor.draft, key) };
    setErrors(next);
    if (next.name !== undefined || next.key !== undefined) return;
    editor.update((draft) => addRole(draft, { key, name: form.name.trim(), permissions: form.permissions }));
    onClose();
  }

  return (
    <Dialog title="新增角色" description="加進這個專案的自訂角色。成員的角色在「成員」裡調整。" onClose={onClose}>
      <form className={styles.dialogForm} noValidate onSubmit={handleSubmit}>
        <TextField label="名稱" value={form.name} error={errors.name} onChange={(event) => { setForm({ ...form, name: event.target.value }); }} />
        <TextField label="key" hint={KEY_HINT} value={key} error={errors.key} onChange={(event) => { setForm({ ...form, key: event.target.value }); }} />
        <Checklist
          isStacked
          legend="權限"
          options={PERMISSION_OPTIONS}
          value={form.permissions}
          onChange={(next) => { setForm({ ...form, permissions: exclusiveReadPermissions(form.permissions, PERMISSION_ORDER.filter((permission) => next.includes(permission))) }); }}
        />
        <DialogFormActions submitLabel="加入" pendingLabel="加入" isPending={false} onCancel={onClose} />
      </form>
    </Dialog>
  );
}
