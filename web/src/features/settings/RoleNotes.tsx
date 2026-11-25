import type { RoleDefinition } from '../../api/blueprint';
import { Notice } from '../../components/Notice/Notice';
import { PRIVILEGED_PERMISSIONS_NOTE } from '../members/memberRoles';
import { roleReferences, type RoleReference } from '../../domain/settingsRoles';
import { elementId, useSettingsEditor, type SettingsEditorValue } from './SettingsContext';
import { stateLabel } from './transitionLabels';
import styles from './Settings.module.css';

function stateName(editor: SettingsEditorValue, workflow: string, state: string): string {
  return stateLabel(editor.draft.workflows.find((candidate) => candidate.key === workflow)?.states ?? [], state);
}

/** 「轉換「客戶驗收」→「結案」（交付流程）」「檢視「給客戶看」」。 */
function referenceName(editor: SettingsEditorValue, reference: RoleReference): string {
  if (reference.kind === 'view') {
    return `檢視「${editor.base.views?.find((view) => view.key === reference.view)?.name ?? reference.view}」`;
  }
  const workflow = editor.base.workflows.find((candidate) => candidate.key === reference.workflow)?.name ?? reference.workflow;
  const pair = `「${stateName(editor, reference.workflow, reference.from)}」→「${stateName(editor, reference.workflow, reference.to)}」`;
  return editor.base.workflows.length > 1 ? `轉換${pair}（${workflow}）` : `轉換${pair}`;
}

function membersNote(count: number): string {
  return count === 0
    ? '目前沒有成員是這個角色。'
    : `還有 ${String(count)} 位成員是這個角色：套用前要先在「成員」把他們換成別的角色，否則預覽頁會擋下。`;
}

/**
 * 拿掉之後清單變空的要特別提醒：轉換的允許角色變空是所有角色都能走、檢視的看得到的角色變空是所有成員都看得到，範圍變寬。
 * 這些只提醒、不擋送出。
 */
function emptiedNote(editor: SettingsEditorValue, references: readonly RoleReference[]): string {
  const transitions = references.filter((reference) => reference.kind === 'transition' && reference.becomesEmpty);
  const views = references.filter((reference) => reference.kind === 'view' && reference.becomesEmpty);
  const parts = [
    transitions.length > 0 ? `${transitions.map((reference) => referenceName(editor, reference)).join('、')}不再限制角色（所有角色都能走）` : '',
    views.length > 0 ? `${views.map((reference) => referenceName(editor, reference)).join('、')}變成所有成員都看得到` : '',
  ];
  const text = parts.filter((part) => part !== '').join('；');
  return text === '' ? '' : `注意：拿掉之後，${text}。`;
}

function noteIds(editor: SettingsEditorValue, roleKey: string) {
  const target = { kind: 'role', role: roleKey } as const;
  return {
    members: elementId(editor.idPrefix, target, 'members'),
    references: elementId(editor.idPrefix, target, 'references'),
    emptied: elementId(editor.idPrefix, target, 'emptied'),
  };
}

/**
 * 說明的 id，給「復原」的 aria-describedby：刪除之後焦點到那裡，螢幕報讀器才唸得到還有幾位成員、會拿掉哪些引用。
 * 只給畫面上真的有的那幾段。
 */
export function removedRoleNoteIds(editor: SettingsEditorValue, roleKey: string): string {
  const ids = noteIds(editor, roleKey);
  const references = roleReferences(editor.base, editor.draft, roleKey);
  const present = [ids.members, references.length > 0 ? ids.references : '', emptiedNote(editor, references) !== '' ? ids.emptied : ''];
  return present.filter((id) => id !== '').join(' ');
}

/** 刪除角色的說明：成員、會一起拿掉的引用、拿掉之後變寬的地方。 */
export function RemovedRoleNotes({ role }: { readonly role: RoleDefinition }) {
  const editor = useSettingsEditor();
  const ids = noteIds(editor, role.key);
  const count = editor.members.filter((member) => member.role === role.key).length;
  const references = roleReferences(editor.base, editor.draft, role.key);
  const emptied = emptiedNote(editor, references);
  return (
    <>
      {count > 0 ? (
        <Notice id={ids.members} tone="warning" role="none" className={styles.rowNotice}>
          {membersNote(count)}
        </Notice>
      ) : (
        <p id={ids.members} className={styles.note}>
          {membersNote(count)}
        </p>
      )}
      {references.length > 0 && (
        <p id={ids.references} className={styles.note}>
          {`也會從${references.map((reference) => referenceName(editor, reference)).join('、')}拿掉這個角色。`}
        </p>
      )}
      {emptied !== '' && (
        <Notice id={ids.emptied} tone="warning" role="none" className={styles.rowNotice}>
          {emptied}
        </Notice>
      )}
    </>
  );
}

/** 勾選角色權限的地方：這兩個權限等同專案的最高權限，改之前先讀到。 */
export function PermissionsNote() {
  return (
    <Notice tone="info" role="none" className={styles.rowNotice}>
      {PRIVILEGED_PERMISSIONS_NOTE}
    </Notice>
  );
}
