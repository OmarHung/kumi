import { IconButton } from '../../components/IconButton/IconButton';
import { Select } from '../../components/Select/Select';
import type { ItemTypeDraft } from '../../domain/settingsDraft';
import { displayName } from '../../domain/settingsNames';
import { removeItemType } from '../../domain/settingsTypeRemoval';
import { itemTypeStatus, updateItemType } from '../../domain/settingsTypes';
import { cx } from '../../lib/cx';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { Checklist, checkedNames } from './Checklist';
import { IssueMessages, ROW_CLASSES, StatusBadge } from './RowParts';
import { elementId, issueBinding, useSettingsEditor, type SettingsEditorValue } from './SettingsContext';
import { TypeMoves, TypeMovesStatus, workflowName } from './TypeMoves';
import controls from './SettingsControls.module.css';
import styles from './Settings.module.css';

function typeTarget(itemType: string) {
  return { kind: 'itemType', itemType } as const;
}

/** 列上的名稱用目前藍圖裡的（正在改的名字一直變，操作的名稱跟著變會讓人搞不清楚）；新的類型用 key。 */
function typeLabel(editor: SettingsEditorValue, type: ItemTypeDraft): string {
  return `類型「${editor.base.itemTypes.find((candidate) => candidate.key === type.key)?.name ?? type.key}」`;
}

function parentOptions(editor: SettingsEditorValue, type: ItemTypeDraft) {
  return editor.draft.itemTypes.filter((candidate) => candidate.key !== type.key).map((candidate) => ({ key: candidate.key, label: displayName(candidate) }));
}

interface TypeProps {
  readonly type: ItemTypeDraft;
  readonly label: string;
}

function TypeName({ type, label }: TypeProps) {
  const editor = useSettingsEditor();
  const binding = issueBinding(editor, typeTarget(type.key));
  if (!editor.canEdit) {
    return <span className={styles.name}>{type.name}</span>;
  }
  return (
    <input
      className={controls.input}
      aria-label={`${label}的名稱`}
      value={type.name}
      aria-invalid={binding.isInvalid || undefined}
      aria-describedby={binding.describedBy}
      onChange={(event) => { editor.update((draft) => updateItemType(draft, type.key, { name: event.target.value })); }}
    />
  );
}

function TypeWorkflow({ type, label }: TypeProps) {
  const editor = useSettingsEditor();
  if (!editor.canEdit) {
    return <span className={styles.category}>{`流程：${workflowName(editor, type.workflow)}`}</span>;
  }
  const options = editor.draft.workflows.map((workflow) => ({ value: workflow.key, label: workflowName(editor, workflow.key) }));
  return (
    <Select
      size="sm"
      width="content"
      aria-label={`${label}的流程`}
      value={type.workflow}
      options={options}
      onChange={(workflow) => { editor.update((draft) => updateItemType(draft, type.key, { workflow })); }}
    />
  );
}

function TypeParents({ type, label }: TypeProps) {
  const editor = useSettingsEditor();
  const options = parentOptions(editor, type);
  if (!editor.canEdit) {
    return <p className={styles.note}>上層：{checkedNames(options, type.parents, '不能掛在任何類型底下')}</p>;
  }
  return (
    <Checklist
      legend={`${label}可以掛在哪些類型底下`}
      options={options}
      value={type.parents}
      onChange={(parents) => { editor.update((draft) => updateItemType(draft, type.key, { parents })); }}
    />
  );
}

const LAST_TYPE_REASON = '最後一個類型：要先新增另一個類型，才能刪除它。';

/**
 * 「刪除」：原本就有的類型換到「要刪除」那一列、焦點到它的「復原」；剛新增的直接不見，焦點到「新增類型」。
 * 只剩一個類型時停用但仍然停得到（aria-disabled），在畫面上說明為什麼（和成員對話框的最後一位管理員一樣），
 * 說明用 aria-describedby 連到按鈕。
 */
function DeleteTypeButton({ type, label, addButtonId }: TypeProps & { readonly addButtonId: string }) {
  const editor = useSettingsEditor();
  const target = typeTarget(type.key);
  const reasonId = elementId(editor.idPrefix, target, 'last');
  const isLast = editor.draft.itemTypes.length === 1;
  const remove = () => {
    if (isLast) return;
    const ids = [elementId(editor.idPrefix, target, 'restore'), addButtonId];
    updateThenFocus(() => { editor.update((draft) => removeItemType(editor.base, draft, type.key)); }, ids);
  };
  return (
    <>
      <div className={styles.actions}>
        <IconButton
          id={elementId(editor.idPrefix, target, 'delete')}
          icon="delete"
          size="sm"
          label={`刪除${label}`}
          aria-disabled={isLast || undefined}
          aria-describedby={isLast ? reasonId : undefined}
          onClick={remove}
        />
      </div>
      {isLast && (
        <p id={reasonId} className={styles.note}>
          {LAST_TYPE_REASON}
        </p>
      )}
    </>
  );
}

/** 一個類型：名稱、key、流程、可以掛在哪些類型底下；換流程時列出要搬的狀態；可以刪除（換到「要刪除」那一列，可以復原）。 */
export function ItemTypeRow({ type, addButtonId }: { readonly type: ItemTypeDraft; readonly addButtonId: string }) {
  const editor = useSettingsEditor();
  const label = typeLabel(editor, type);
  const status = itemTypeStatus(editor.base, editor.draft, type.key);
  return (
    <li className={cx(styles.row, styles.fieldRow, ROW_CLASSES[status])}>
      <div className={styles.cells}>
        <TypeName type={type} label={label} />
        <span className={styles.details}>
          <TypeWorkflow type={type} label={label} />
          <span className={styles.key}>{type.key}</span>
          <StatusBadge status={status} />
        </span>
      </div>
      {editor.canEdit && <DeleteTypeButton type={type} label={label} addButtonId={addButtonId} />}
      <TypeParents type={type} label={label} />
      <TypeMoves type={type} label={label} />
      <TypeMovesStatus type={type} />
      <IssueMessages binding={issueBinding(editor, typeTarget(type.key))} />
    </li>
  );
}
