import { useRef, useState, type KeyboardEvent, type RefObject } from 'react';
import type { FieldOption } from '../../api/blueprint';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { IconButton } from '../../components/IconButton/IconButton';
import { addOption, removeOption, renameOption, type FieldDraft } from '../../domain/settingsDraft';
import { optionStatus } from '../../domain/settingsStatus';
import { nameError, optionKeyError, suggestKey } from '../../domain/settingsValidation';
import { cx } from '../../lib/cx';
import { isCommitEnter } from '../../lib/keyboard';
import { updateThenFocus, type FocusTarget } from '../../lib/updateThenFocus';
import { IssueMessages, ROW_CLASSES, StatusBadge } from './RowParts';
import { elementId, issueBinding, useSettingsEditor } from './SettingsContext';
import controls from './SettingsControls.module.css';
import styles from './Settings.module.css';

const LOCKED_HINT = '既有的選項不能刪除：已經用到它的項目會對不到值。可以改名。';

function optionTarget(field: string, option: string) {
  return { kind: 'option', field, option } as const;
}

interface OptionRowProps {
  readonly field: FieldDraft;
  readonly option: FieldOption;
  readonly hintId: string;
  /** 這一列被移除後焦點的去處：下一列、上一列的按鈕，都沒有就是新選項的名稱。 */
  readonly focusAfterRemove: readonly FocusTarget[];
}

function OptionRow({ field, option, hintId, focusAfterRemove }: OptionRowProps) {
  const editor = useSettingsEditor();
  const status = optionStatus(editor.base, field.key, option.key, option.name);
  const target = optionTarget(field.key, option.key);
  const binding = issueBinding(editor, target);
  const actionId = elementId(editor.idPrefix, target, 'action');
  const original = editor.base.fields.find((candidate) => candidate.key === field.key)?.options?.find((candidate) => candidate.key === option.key);
  const label = `選項「${original?.name ?? option.key}」`;
  if (!editor.canEdit) {
    return <li className={styles.option}>{option.name}</li>;
  }
  return (
    <li className={cx(styles.option, ROW_CLASSES[status])}>
      <input
        className={controls.input}
        aria-label={`${label}的名稱`}
        value={option.name}
        aria-invalid={binding.isInvalid || undefined}
        aria-describedby={binding.describedBy}
        onChange={(event) => { editor.update((draft) => renameOption(draft, field.key, option.key, event.target.value)); }}
      />
      <span className={styles.key}>{option.key}</span>
      <StatusBadge status={status} />
      {original === undefined ? (
        <IconButton
          id={actionId}
          icon="delete"
          size="sm"
          label={`移除${label}`}
          onClick={() => { updateThenFocus(() => { editor.update((draft) => removeOption(draft, field.key, option.key)); }, focusAfterRemove); }}
        />
      ) : (
        // 停用但仍然停得到（aria-disabled），鍵盤使用者才聽得到為什麼不能刪；長相是 Button 的 aria-disabled。
        <IconButton id={actionId} icon="delete" size="sm" aria-disabled="true" aria-describedby={hintId} label={`刪除${label}`} />
      )}
      <IssueMessages binding={binding} />
    </li>
  );
}

interface NewOptionProps {
  readonly field: FieldDraft;
  readonly label: string;
  readonly nameRef: RefObject<HTMLInputElement | null>;
}

function NewOption({ field, label, nameRef }: NewOptionProps) {
  const editor = useSettingsEditor();
  const [name, setName] = useState('');
  const [key, setKey] = useState<string | null>(null);
  const [errors, setErrors] = useState<{ readonly name?: string; readonly key?: string }>({});
  const suggested = key ?? suggestKey(name, 'option', new Set((field.options ?? []).map((option) => option.key)));

  function add() {
    const next = { name: nameError(name), key: optionKeyError(editor.draft, field.key, suggested) };
    setErrors(next);
    if (next.name !== undefined || next.key !== undefined) return;
    editor.update((draft) => addOption(draft, field.key, { key: suggested, name: name.trim() }));
    setName('');
    setKey(null);
  }
  const addOnEnter = (event: KeyboardEvent) => {
    if (isCommitEnter(event)) add();
  };
  return (
    <div className={styles.newOption}>
      <TextField ref={nameRef} label={`${label}新選項的名稱`} value={name} error={errors.name} onKeyDown={addOnEnter} onChange={(event) => { setName(event.target.value); }} />
      <TextField label={`${label}新選項的 key`} value={suggested} error={errors.key} onKeyDown={addOnEnter} onChange={(event) => { setKey(event.target.value); }} />
      <Button size="sm" className={styles.newOptionButton} onClick={add}>
        加入選項
      </Button>
    </div>
  );
}

/** 選項型欄位的選項：可以改名、可以新增；既有的不能刪（停用並說明），剛加的可以拿掉。 */
export function FieldOptions({ field, label }: { readonly field: FieldDraft; readonly label: string }) {
  const editor = useSettingsEditor();
  const target = { kind: 'field', field: field.key } as const;
  const hintId = elementId(editor.idPrefix, target, 'locked');
  const hasExisting = (editor.base.fields.find((candidate) => candidate.key === field.key)?.options ?? []).length > 0;
  const nameRef = useRef<HTMLInputElement>(null);
  const options = field.options ?? [];
  const actionOf = (index: number): FocusTarget[] => {
    const neighbor = options[index];
    return neighbor === undefined ? [] : [elementId(editor.idPrefix, optionTarget(field.key, neighbor.key), 'action')];
  };
  return (
    <div className={styles.options}>
      <ul className={styles.optionList} aria-label={`${label}的選項`}>
        {options.map((option, index) => (
          <OptionRow
            key={option.key}
            field={field}
            option={option}
            hintId={hintId}
            focusAfterRemove={[...actionOf(index + 1), ...actionOf(index - 1), nameRef]}
          />
        ))}
      </ul>
      {editor.canEdit && hasExisting && (
        <p id={hintId} className={styles.note}>
          {LOCKED_HINT}
        </p>
      )}
      {editor.canEdit && <NewOption field={field} label={label} nameRef={nameRef} />}
    </div>
  );
}
