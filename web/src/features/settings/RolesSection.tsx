import { useState } from 'react';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { ITEM_READ_OWN_NOTE } from '../../domain/labels';
import { BUILT_IN_ROLES, removedRoles } from '../../domain/settingsRoles';
import { sectionHeadingId } from './settingsSections';
import { AddRoleDialog } from './AddRoleDialog';
import { BuiltInRoleRow, RemovedRoleRow, RoleRow } from './RoleRow';
import { useSettingsEditor } from './SettingsContext';
import styles from './Settings.module.css';

/** 角色：內建的五個（唯讀）、自訂角色（照藍圖的順序，新的接在最後），要刪除的列在後面。 */
export function RolesSection() {
  const editor = useSettingsEditor();
  const [isAdding, setIsAdding] = useState(false);
  const headingId = sectionHeadingId(editor.idPrefix, 'roles');
  const addButtonId = `${editor.idPrefix}-add-role`;
  const removed = removedRoles(editor.base, editor.draft);
  return (
    // 設定頁目前這個區塊是第一筆內容（docs/DESIGN.md 6.2）：給 scripts/ui-audit.js 量高度預算。
    <section className={styles.section} aria-labelledby={headingId} data-content-start>
      <div className={styles.sectionHeader}>
        <h2 id={headingId} tabIndex={-1} className={styles.sectionTitle}>
          角色
        </h2>
        <p className={styles.sectionHint}>內建的五個角色寫死在系統裡，不能修改；自訂角色可以改名稱與權限。成員的角色在「成員」裡調整。{ITEM_READ_OWN_NOTE}</p>
      </div>
      <ul className={styles.list} aria-label="內建角色">
        {BUILT_IN_ROLES.map((role) => (
          <BuiltInRoleRow key={role.key} role={role} />
        ))}
      </ul>
      {editor.draft.roles.length > 0 ? (
        <ul className={styles.list} aria-label="自訂角色">
          {editor.draft.roles.map((role) => (
            <RoleRow key={role.key} role={role} addButtonId={addButtonId} />
          ))}
        </ul>
      ) : (
        <p className={styles.note}>還沒有自訂角色。</p>
      )}
      {removed.length > 0 && (
        <ul className={styles.list} aria-label="要刪除的角色">
          {removed.map((role) => (
            <RemovedRoleRow key={role.key} role={role} />
          ))}
        </ul>
      )}
      {editor.canEdit && (
        <Button id={addButtonId} size="sm" className={styles.addButton} onClick={() => { setIsAdding(true); }}>
          <Icon name="add" />
          新增角色
        </Button>
      )}
      {isAdding && <AddRoleDialog onClose={() => { setIsAdding(false); }} />}
    </section>
  );
}
