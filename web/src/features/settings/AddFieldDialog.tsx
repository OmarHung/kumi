import { useId, useRef, useState, type SubmitEvent } from 'react';
import type { FieldType } from '../../api/blueprint';
import { Button } from '../../components/Button/Button';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { SelectField, TextField } from '../../components/Field/Field';
import { FIELD_TYPE_LABELS } from '../../domain/labels';
import { addField } from '../../domain/settingsDraft';
import { KEY_HINT } from '../../domain/settingsValidation';
import { updateThenFocus, type FocusTarget } from '../../lib/updateThenFocus';
import {
  CURRENCIES,
  emptyFieldForm,
  FIELD_TYPE_ORDER,
  fieldFromForm,
  hasFieldFormErrors,
  isSelectType,
  resolvedOptionKeys,
  suggestedFieldKey,
  validateFieldForm,
  type FieldForm,
  type FieldFormErrors,
  type OptionInput,
} from './fieldForm';
import { useSettingsDialog, useSettingsEditor } from './SettingsContext';
import { TypeChecklist } from './TypeChecklist';
import styles from './Settings.module.css';

const NO_ERRORS: FieldFormErrors = { optionErrors: new Map() };

const TYPE_OPTIONS = FIELD_TYPE_ORDER.map((type) => ({ value: type, label: FIELD_TYPE_LABELS[type] }));
const CURRENCY_OPTIONS = CURRENCIES.map((currency) => ({ value: currency.code, label: `${currency.code}（${currency.name}）` }));

interface OptionsEditorProps {
  readonly options: readonly OptionInput[];
  readonly errors: FieldFormErrors;
  readonly onChange: (options: readonly OptionInput[]) => void;
}

/** 新欄位的選項：每一個都有名稱與 key（有建議值），至少要一個。 */
function OptionsEditor({ options, errors, onChange }: OptionsEditorProps) {
  const idPrefix = useId();
  const addRef = useRef<HTMLButtonElement>(null);
  const keys = resolvedOptionKeys(options);
  const change = (id: number, patch: Partial<OptionInput>) => {
    onChange(options.map((option) => (option.id === id ? { ...option, ...patch } : option)));
  };
  const nextId = Math.max(0, ...options.map((option) => option.id)) + 1;
  const removeId = (id: number) => `${idPrefix}-option-${String(id)}-remove`;
  // 拿掉的按鈕跟著那一列不見：焦點換到下一列（沒有就上一列）的「拿掉」，都沒有了到「再加一個選項」，才不會掉到對話框外面。
  const remove = (index: number, id: number) => {
    const neighbors = [options[index + 1], options[index - 1]].flatMap((neighbor) => (neighbor === undefined ? [] : [removeId(neighbor.id)]));
    const targets: FocusTarget[] = [...neighbors, addRef];
    updateThenFocus(() => { onChange(options.filter((candidate) => candidate.id !== id)); }, targets);
  };
  return (
    <fieldset className={styles.newOptions}>
      <legend className={styles.typesLegend}>選項</legend>
      {errors.options !== undefined && <p className={styles.issue}>{errors.options}</p>}
      {options.map((option, index) => {
        const optionErrors = errors.optionErrors.get(option.id);
        const position = String(index + 1);
        return (
          <div key={option.id} className={styles.newOption}>
            <TextField label={`第 ${position} 個選項的名稱`} value={option.name} error={optionErrors?.name} onChange={(event) => { change(option.id, { name: event.target.value }); }} />
            <TextField label={`第 ${position} 個選項的 key`} value={keys[index] ?? ''} error={optionErrors?.key} onChange={(event) => { change(option.id, { key: event.target.value }); }} />
            <Button id={removeId(option.id)} size="sm" variant="ghost" className={styles.newOptionButton} aria-label={`拿掉第 ${position} 個選項`} onClick={() => { remove(index, option.id); }}>
              拿掉
            </Button>
          </div>
        );
      })}
      <Button ref={addRef} size="sm" onClick={() => { onChange([...options, { id: nextId, name: '', key: null }]); }}>
        再加一個選項
      </Button>
    </fieldset>
  );
}

function TypeSpecificFields({ form, errors, setForm }: { readonly form: FieldForm; readonly errors: FieldFormErrors; readonly setForm: (form: FieldForm) => void }) {
  if (form.type === 'currency') {
    return <SelectField label="幣別" value={form.currency} options={CURRENCY_OPTIONS} onChange={(currency) => { setForm({ ...form, currency }); }} />;
  }
  return isSelectType(form.type) ? (
    <OptionsEditor options={form.options} errors={errors} onChange={(options) => { setForm({ ...form, options }); }} />
  ) : null;
}

/** 新增欄位：名稱、key、型別（選項型要有選項、金額要選幣別）、套用到哪些類型。加進草稿，還沒送出。 */
export function AddFieldDialog({ onClose }: { readonly onClose: () => void }) {
  const editor = useSettingsEditor();
  useSettingsDialog();
  const [form, setForm] = useState<FieldForm>(() => emptyFieldForm(editor.draft.itemTypes));
  const [errors, setErrors] = useState<FieldFormErrors>(NO_ERRORS);
  const key = suggestedFieldKey(form, editor.base, editor.draft);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = validateFieldForm(form, editor.base, editor.draft);
    setErrors(next);
    if (hasFieldFormErrors(next)) return;
    editor.update((draft) => addField(draft, fieldFromForm(form, key)));
    onClose();
  }

  return (
    <Dialog title="新增欄位" description="加進這個專案的自訂欄位。送出變更之前都還可以改。" onClose={onClose}>
      <form className={styles.dialogForm} noValidate onSubmit={handleSubmit}>
        <TextField label="名稱" value={form.name} error={errors.name} onChange={(event) => { setForm({ ...form, name: event.target.value }); }} />
        <TextField label="key" hint={KEY_HINT} value={key} error={errors.key} onChange={(event) => { setForm({ ...form, key: event.target.value }); }} />
        <SelectField
          label="型別"
          hint="送出後這一版還不能改型別。"
          value={form.type}
          options={TYPE_OPTIONS}
          onChange={(type) => { setForm({ ...form, type: type as FieldType }); }}
        />
        <TypeSpecificFields form={form} errors={errors} setForm={setForm} />
        <TypeChecklist types={editor.draft.itemTypes} legend="套用到的類型" value={form.appliesTo} onChange={(appliesTo) => { setForm({ ...form, appliesTo }); }} />
        <DialogFormActions submitLabel="加入" pendingLabel="加入" isPending={false} onCancel={onClose} />
      </form>
    </Dialog>
  );
}
