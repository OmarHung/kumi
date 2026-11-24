import type { Permission, RoleDefinition } from '../../api/blueprint';
import { Badge } from '../../components/Badge/Badge';
import { IconButton } from '../../components/IconButton/IconButton';
import { PERMISSION_LABELS, PERMISSION_ORDER } from '../../domain/labels';
import { exclusiveReadPermissions, removeRole, restoreRole, roleStatus, updateRole, type BuiltInRole } from '../../domain/settingsRoles';
import { cx } from '../../lib/cx';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { Checklist, checkedNames } from './Checklist';
import { IssueMessages, ROW_CLASSES, StatusBadge } from './RowParts';
import { elementId, issueBinding, useSettingsEditor } from './SettingsContext';
import { RemovedRoleNotes, removedRoleNoteIds } from './RoleNotes';
import controls from './SettingsControls.module.css';
import styles from './Settings.module.css';

export const PERMISSION_OPTIONS = PERMISSION_ORDER.map((permission) => ({ key: permission, label: PERMISSION_LABELS[permission] }));

function roleTarget(role: string) {
  return { kind: 'role', role } as const;
}

function permissionText(permissions: readonly Permission[]): string {
  return checkedNames(PERMISSION_OPTIONS, permissions, '沒有任何權限');
}

/** 內建角色：名稱與權限寫死在伺服器，只列出來。 */
export function BuiltInRoleRow({ role }: { readonly role: BuiltInRole }) {
  return (
    <li className={cx(styles.row, styles.fieldRow)}>
      <div className={styles.cells}>
        <span className={styles.name}>{role.name}</span>
        <span className={styles.details}>
          <span className={styles.key}>{role.key}</span>
          <Badge>內建</Badge>
        </span>
      </div>
      <p className={styles.note}>權限：{permissionText(role.permissions)}</p>
    </li>
  );
}

/** 列上的名稱用目前藍圖裡的；新的角色用 key。 */
function roleLabel(base: readonly RoleDefinition[], role: RoleDefinition): string {
  return `角色「${base.find((candidate) => candidate.key === role.key)?.name ?? role.key}」`;
}

function RoleName({ role, label }: { readonly role: RoleDefinition; readonly label: string }) {
  const editor = useSettingsEditor();
  const binding = issueBinding(editor, roleTarget(role.key));
  if (!editor.canEdit) {
    return <span className={styles.name}>{role.name}</span>;
  }
  return (
    <input
      className={controls.input}
      aria-label={`${label}的名稱`}
      value={role.name}
      aria-invalid={binding.isInvalid || undefined}
      aria-describedby={binding.describedBy}
      onChange={(event) => { editor.update((draft) => updateRole(draft, role.key, { name: event.target.value })); }}
    />
  );
}

/** 自訂角色：名稱、key、權限勾選；可以刪除（換到「要刪除」那一列，可以復原）。 */
export function RoleRow({ role, addButtonId }: { readonly role: RoleDefinition; readonly addButtonId: string }) {
  const editor = useSettingsEditor();
  const target = roleTarget(role.key);
  const label = roleLabel(editor.base.roles ?? [], role);
  const status = roleStatus(editor.base, editor.draft, role.key);
  const remove = () => {
    // 原本就有的角色換到「要刪除」那一列；剛新增的直接不見，回到「新增角色」。
    const ids = [elementId(editor.idPrefix, target, 'restore'), addButtonId];
    updateThenFocus(() => { editor.update((draft) => removeRole(editor.base, draft, role.key)); }, ids);
  };
  return (
    <li className={cx(styles.row, styles.fieldRow, ROW_CLASSES[status])}>
      <div className={styles.cells}>
        <RoleName role={role} label={label} />
        <span className={styles.details}>
          <span className={styles.key}>{role.key}</span>
          <StatusBadge status={status} />
        </span>
      </div>
      {editor.canEdit && (
        <div className={styles.actions}>
          <IconButton id={elementId(editor.idPrefix, target, 'delete')} icon="delete" size="sm" label={`刪除${label}`} onClick={remove} />
        </div>
      )}
      {editor.canEdit ? (
        <Checklist
          legend={`${label}的權限`}
          options={PERMISSION_OPTIONS}
          value={role.permissions}
          onChange={(next) => { editor.update((draft) => updateRole(draft, role.key, { permissions: exclusiveReadPermissions(role.permissions, PERMISSION_ORDER.filter((permission) => next.includes(permission))) })); }}
        />
      ) : (
        <p className={styles.note}>權限：{permissionText(role.permissions)}</p>
      )}
      <IssueMessages binding={issueBinding(editor, target)} />
    </li>
  );
}

/** 要刪除的角色：說明還有幾位成員是這個角色、會一起拿掉哪些引用，可以復原。 */
export function RemovedRoleRow({ role }: { readonly role: RoleDefinition }) {
  const editor = useSettingsEditor();
  const target = roleTarget(role.key);
  const restore = () => {
    updateThenFocus(() => { editor.update((draft) => restoreRole(editor.base, draft, role.key)); }, [elementId(editor.idPrefix, target, 'delete')]);
  };
  return (
    <li className={cx(styles.row, styles.fieldRow, styles.rowRemoved)}>
      <div className={styles.cells}>
        <span className={cx(styles.name, styles.removedName)}>{role.name}</span>
        <span className={styles.details}>
          <span className={styles.key}>{role.key}</span>
          <StatusBadge status="removed" />
        </span>
      </div>
      <div className={styles.actions}>
        <IconButton
          id={elementId(editor.idPrefix, target, 'restore')}
          icon="restore"
          size="sm"
          label={`復原角色「${role.name}」`}
          aria-describedby={removedRoleNoteIds(editor, role.key)}
          onClick={restore}
        />
      </div>
      <RemovedRoleNotes role={role} />
      <IssueMessages binding={issueBinding(editor, target)} />
    </li>
  );
}
