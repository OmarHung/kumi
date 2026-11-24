import type { FieldDefinition, ProjectBlueprint } from '../../api/blueprint';
import { IconButton } from '../../components/IconButton/IconButton';
import { Notice } from '../../components/Notice/Notice';
import { FIELD_TYPE_LABELS } from '../../domain/labels';
import { fieldAutomationReminder } from '../../domain/settingsAutomations';
import { detachConflicts, fieldReferences } from '../../domain/settingsChangeSet';
import { removeField, renameField, restoreField, setFieldTypes, type FieldDraft, type SettingsDraft } from '../../domain/settingsDraft';
import { fieldStatus } from '../../domain/settingsStatus';
import { cx } from '../../lib/cx';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { FieldOptions } from './FieldOptions';
import { IssueMessages, ROW_CLASSES, StatusBadge } from './RowParts';
import { elementId, issueBinding, useSettingsEditor, type SettingsEditorValue } from './SettingsContext';
import { TypeChecklist, typeNames } from './TypeChecklist';
import controls from './SettingsControls.module.css';
import styles from './Settings.module.css';

function fieldTarget(field: string) {
  return { kind: 'field', field } as const;
}

function typeText(field: FieldDefinition): string {
  const type = FIELD_TYPE_LABELS[field.type];
  return field.type === 'currency' && field.currency !== undefined ? `${type}（${field.currency}）` : type;
}

function isSelect(field: FieldDefinition): boolean {
  return field.type === 'single_select' || field.type === 'multi_select';
}

function changeThenFocus(editor: SettingsEditorValue, change: (draft: SettingsDraft) => SettingsDraft, ids: readonly string[]) {
  updateThenFocus(() => { editor.update(change); }, ids);
}

interface FieldRowProps {
  readonly field: FieldDraft;
  readonly addButtonId: string;
}

function FieldName({ field, label }: { readonly field: FieldDraft; readonly label: string }) {
  const editor = useSettingsEditor();
  const binding = issueBinding(editor, fieldTarget(field.key));
  if (!editor.canEdit) {
    return <span className={styles.name}>{field.name}</span>;
  }
  return (
    <input
      className={controls.input}
      aria-label={`${label}的名稱`}
      value={field.name}
      aria-invalid={binding.isInvalid || undefined}
      aria-describedby={binding.describedBy}
      onChange={(event) => { editor.update((draft) => renameField(draft, field.key, event.target.value)); }}
    />
  );
}

/**
 * 取消套用到某個類型、而那個類型用的流程有轉換要求先填這個欄位時的提醒（送出前的檢查也會標在那條轉換上）。
 * 例如「「需求變更單」用的「交付流程」有轉換要求先填這個欄位…」。沒有這種情況時是空字串。
 */
function detachReminder(base: ProjectBlueprint, draft: SettingsDraft, field: FieldDraft): string {
  const conflicts = detachConflicts(draft, field);
  const workflows = [...new Set(conflicts.map((conflict) => conflict.workflow))];
  const parts = workflows.map((workflow) => {
    const types = conflicts
      .filter((conflict) => conflict.workflow === workflow)
      .map((conflict) => `「${draft.itemTypes.find((type) => type.key === conflict.itemType)?.name ?? conflict.itemType}」`);
    return `${types.join('、')}用的「${base.workflows.find((candidate) => candidate.key === workflow)?.name ?? workflow}」`;
  });
  return parts.length === 0 ? '' : `${parts.join('、')}有轉換要求先填這個欄位：取消套用之後，送出會被伺服器擋下。`;
}

/** 套用到哪些類型；提醒放在一直掛著的即時區域，取消勾選時螢幕報讀器會唸出來。 */
function FieldTypes({ field, label }: { readonly field: FieldDraft; readonly label: string }) {
  const editor = useSettingsEditor();
  if (!editor.canEdit) {
    return <p className={styles.note}>套用到：{typeNames(editor.draft.itemTypes, field.appliesTo)}</p>;
  }
  const reminderId = elementId(editor.idPrefix, fieldTarget(field.key), 'detach');
  const reminder = detachReminder(editor.base, editor.draft, field);
  return (
    <>
      <TypeChecklist
        types={editor.draft.itemTypes}
        legend={`${label}套用到的類型`}
        value={field.appliesTo}
        describedBy={reminder === '' ? undefined : reminderId}
        onChange={(next) => { editor.update((draft) => setFieldTypes(draft, field.key, next)); }}
      />
      <p id={reminderId} className={reminder === '' ? 'visually-hidden' : styles.note} aria-live="polite">
        {reminder}
      </p>
    </>
  );
}

/** 一個自訂欄位：名稱、key、型別、套用到哪些類型、選項；可以改名、改套用的類型、刪除。這一片不做改型別。 */
export function FieldRow({ field, addButtonId }: FieldRowProps) {
  const editor = useSettingsEditor();
  const target = fieldTarget(field.key);
  const status = fieldStatus(editor.base, editor.draft, field.key);
  const original = editor.base.fields.find((candidate) => candidate.key === field.key);
  const label = `「${original?.name ?? field.key}」`;
  const remove = () => {
    changeThenFocus(editor, (draft) => removeField(editor.base, draft, field.key), [elementId(editor.idPrefix, target, 'restore'), addButtonId]);
  };
  return (
    <li className={cx(styles.row, styles.fieldRow, ROW_CLASSES[status])}>
      <div className={styles.cells}>
        <FieldName field={field} label={label} />
        <span className={styles.details}>
          <span className={styles.key}>{field.key}</span>
          <span className={styles.fieldType}>{typeText(field)}</span>
          <StatusBadge status={status} />
        </span>
      </div>
      {editor.canEdit && (
        <div className={styles.actions}>
          <IconButton id={elementId(editor.idPrefix, target, 'delete')} icon="delete" size="sm" label={`刪除${label}`} onClick={remove} />
        </div>
      )}
      <FieldTypes field={field} label={label} />
      <IssueMessages binding={issueBinding(editor, target)} />
      {isSelect(field) && <FieldOptions field={field} label={label} />}
    </li>
  );
}

/** 要刪除的欄位：說明值會封存、還有哪些地方會一起拿掉它，可以復原。 */
export function RemovedFieldRow({ field }: { readonly field: FieldDefinition }) {
  const editor = useSettingsEditor();
  const target = fieldTarget(field.key);
  const references = fieldReferences(editor.base, editor.draft, field.key);
  const automation = fieldAutomationReminder(editor.base, field.key);
  const noteId = elementId(editor.idPrefix, target, 'removal');
  const automationId = elementId(editor.idPrefix, target, 'automation');
  const restore = () => {
    changeThenFocus(editor, (draft) => restoreField(editor.base, draft, field.key), [elementId(editor.idPrefix, target, 'delete')]);
  };
  return (
    <li className={cx(styles.row, styles.fieldRow, styles.rowRemoved)}>
      <div className={styles.cells}>
        <span className={cx(styles.name, styles.removedName)}>{field.name}</span>
        <span className={styles.details}>
          <span className={styles.key}>{field.key}</span>
          <span className={styles.fieldType}>{typeText(field)}</span>
          <StatusBadge status="removed" />
        </span>
      </div>
      <div className={styles.actions}>
        {/* 刪除後焦點到「復原」：封存的說明與自動化的提醒也連過來，螢幕報讀器才唸得到。 */}
        <IconButton
          id={elementId(editor.idPrefix, target, 'restore')}
          icon="restore"
          size="sm"
          label={`復原「${field.name}」`}
          aria-describedby={automation === '' ? noteId : `${noteId} ${automationId}`}
          onClick={restore}
        />
      </div>
      <p id={noteId} className={styles.note}>
        套用後，項目裡這個欄位的值會封存起來，畫面上不再顯示。
        {references.length > 0 && `也會從${references.join('、')}拿掉。`}
      </p>
      {automation !== '' && (
        <Notice id={automationId} tone="warning" role="none" className={styles.rowNotice}>
          {automation}
        </Notice>
      )}
      <IssueMessages binding={issueBinding(editor, target)} />
    </li>
  );
}
