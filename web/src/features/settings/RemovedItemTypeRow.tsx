import type { ItemTypeDefinition } from '../../api/blueprint';
import { IconButton } from '../../components/IconButton/IconButton';
import { Notice } from '../../components/Notice/Notice';
import { itemTypeAutomationReminder } from '../../domain/settingsAutomations';
import { displayName } from '../../domain/settingsNames';
import { itemTypeReferences, restoreItemType, type ItemTypeReference } from '../../domain/settingsTypeRemoval';
import { cx } from '../../lib/cx';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { IssueMessages, StatusBadge } from './RowParts';
import { elementId, issueBinding, useSettingsEditor, type SettingsEditorValue } from './SettingsContext';
import { workflowName } from './TypeMoves';
import styles from './Settings.module.css';

const BLOCKED_NOTE = '還有這種項目時，預覽頁會擋下：要先刪除這些項目或改成別的類型。';

function typeTarget(itemType: string) {
  return { kind: 'itemType', itemType } as const;
}

function typeName(editor: SettingsEditorValue, typeKey: string): string {
  const type = editor.draft.itemTypes.find((candidate) => candidate.key === typeKey);
  return type === undefined ? typeKey : displayName(type);
}

function viewName(editor: SettingsEditorValue, viewKey: string): string {
  return editor.base.views?.find((view) => view.key === viewKey)?.name ?? viewKey;
}

/** 「類型「任務」可以掛的上層」「檢視「給客戶看」的類型篩選」。 */
function referenceName(editor: SettingsEditorValue, reference: ItemTypeReference): string {
  return reference.kind === 'parent'
    ? `類型「${typeName(editor, reference.itemType)}」可以掛的上層`
    : `檢視「${viewName(editor, reference.view)}」的類型篩選`;
}

/** 拿掉之後清單變空：類型不能再有上層；檢視不再篩選類型，範圍變寬。只提醒、不擋送出。 */
function emptiedNote(editor: SettingsEditorValue, references: readonly ItemTypeReference[]): string {
  const types = references.flatMap((reference) => (reference.kind === 'parent' && reference.becomesEmpty ? [`「${typeName(editor, reference.itemType)}」`] : []));
  const views = references.flatMap((reference) => (reference.kind === 'view' && reference.becomesEmpty ? [`檢視「${viewName(editor, reference.view)}」`] : []));
  const parts = [
    types.length > 0 ? `${types.join('、')}不能再掛在任何類型底下` : '',
    views.length > 0 ? `${views.join('、')}不再篩選類型（所有類型都會列出）` : '',
  ].filter((part) => part !== '');
  return parts.length === 0 ? '' : `注意：拿掉之後，${parts.join('；')}。`;
}

interface Note {
  readonly id: string;
  readonly text: string;
  readonly isWarning: boolean;
}

function notesOf(editor: SettingsEditorValue, type: ItemTypeDefinition): readonly Note[] {
  const target = typeTarget(type.key);
  const references = itemTypeReferences(editor.base, editor.draft, type.key);
  const referenceText = references.length === 0 ? '' : `也會從${references.map((reference) => referenceName(editor, reference)).join('、')}拿掉這個類型。`;
  const notes = [
    { part: 'removal', text: BLOCKED_NOTE, isWarning: false },
    { part: 'references', text: referenceText, isWarning: false },
    { part: 'emptied', text: emptiedNote(editor, references), isWarning: true },
    { part: 'automation', text: itemTypeAutomationReminder(editor.base, type.key), isWarning: true },
  ];
  return notes.filter((note) => note.text !== '').map((note) => ({ ...note, id: elementId(editor.idPrefix, target, note.part) }));
}

/**
 * 要刪除的類型：說明還有項目時會被擋下、會一起拿掉哪些引用、拿掉之後變寬的地方與自動化的提醒，可以復原。
 * 刪除後焦點到「復原」，這些說明（與伺服器的問題）都用 aria-describedby 連過去。
 */
export function RemovedItemTypeRow({ type }: { readonly type: ItemTypeDefinition }) {
  const editor = useSettingsEditor();
  const target = typeTarget(type.key);
  const binding = issueBinding(editor, target);
  const notes = notesOf(editor, type);
  const describedBy = [...notes.map((note) => note.id), ...(binding.describedBy === undefined ? [] : [binding.describedBy])].join(' ');
  const restore = () => {
    updateThenFocus(() => { editor.update((draft) => restoreItemType(editor.base, draft, type.key)); }, [elementId(editor.idPrefix, target, 'delete')]);
  };
  return (
    <li className={cx(styles.row, styles.fieldRow, styles.rowRemoved)}>
      <div className={styles.cells}>
        <span className={cx(styles.name, styles.removedName)}>{type.name}</span>
        <span className={styles.details}>
          <span className={styles.category}>{`流程：${workflowName(editor, type.workflow)}`}</span>
          <span className={styles.key}>{type.key}</span>
          <StatusBadge status="removed" />
        </span>
      </div>
      <div className={styles.actions}>
        <IconButton
          id={elementId(editor.idPrefix, target, 'restore')}
          icon="restore"
          size="sm"
          label={`復原類型「${type.name}」`}
          aria-describedby={describedBy}
          onClick={restore}
        />
      </div>
      {notes.map((note) =>
        note.isWarning ? (
          <Notice key={note.id} id={note.id} tone="warning" role="none" className={styles.rowNotice}>
            {note.text}
          </Notice>
        ) : (
          <p key={note.id} id={note.id} className={styles.note}>
            {note.text}
          </p>
        ),
      )}
      <IssueMessages binding={binding} />
    </li>
  );
}
