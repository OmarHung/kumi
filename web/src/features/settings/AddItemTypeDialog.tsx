import { useState, type SubmitEvent } from 'react';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { SelectField, TextField } from '../../components/Field/Field';
import { itemTypeKeyError } from '../../domain/settingsChecks';
import { displayName, KEY_HINT, nameError } from '../../domain/settingsNames';
import { addItemType, fieldsRequiredByWorkflow } from '../../domain/settingsTypes';
import { suggestKey } from '../../domain/settingsValidation';
import { Checklist } from './Checklist';
import { useSettingsDialog, useSettingsEditor } from './SettingsContext';
import styles from './Settings.module.css';

interface TypeForm {
  readonly name: string;
  /** null 表示還沒自己改過 key，跟著名稱給建議值。 */
  readonly key: string | null;
  readonly workflow: string;
  readonly parents: readonly string[];
  /** null 表示還沒自己勾過，跟著流程預設勾選它的轉換要求先填的自訂欄位。 */
  readonly fields: readonly string[] | null;
}

interface FormErrors {
  readonly name?: string;
  readonly key?: string;
}

/** 新增類型：名稱、key、流程、可以掛在哪些類型底下、要套用的自訂欄位。加進草稿，還沒送出。 */
export function AddItemTypeDialog({ onClose }: { readonly onClose: () => void }) {
  const editor = useSettingsEditor();
  useSettingsDialog();
  const { draft, base } = editor;
  const [form, setForm] = useState<TypeForm>(() => ({ name: '', key: null, workflow: draft.workflows[0]?.key ?? '', parents: [], fields: null }));
  const [errors, setErrors] = useState<FormErrors>({});
  const key = form.key ?? suggestKey(form.name, 'type', new Set([...base.itemTypes, ...draft.itemTypes].map((type) => type.key)));
  const fields = form.fields ?? fieldsRequiredByWorkflow(draft, form.workflow);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = { name: nameError(form.name), key: itemTypeKeyError(base, draft, key) };
    setErrors(next);
    if (next.name !== undefined || next.key !== undefined) return;
    const type = { key, name: form.name.trim(), workflow: form.workflow, parents: form.parents };
    editor.update((current) => addItemType(current, type, fields));
    onClose();
  }

  const typeOptions = draft.itemTypes.map((type) => ({ key: type.key, label: displayName(type) }));
  const fieldOptions = draft.fields.map((field) => ({ key: field.key, label: displayName(field) }));
  const workflowOptions = draft.workflows.map((workflow) => ({
    value: workflow.key,
    label: base.workflows.find((candidate) => candidate.key === workflow.key)?.name ?? workflow.key,
  }));
  return (
    <Dialog title="新增類型" description="加進這個專案的類型。送出變更之前都還可以改。" onClose={onClose}>
      <form className={styles.dialogForm} noValidate onSubmit={handleSubmit}>
        <TextField label="名稱" value={form.name} error={errors.name} onChange={(event) => { setForm({ ...form, name: event.target.value }); }} />
        <TextField label="key" hint={KEY_HINT} value={key} error={errors.key} onChange={(event) => { setForm({ ...form, key: event.target.value }); }} />
        <SelectField label="流程" value={form.workflow} options={workflowOptions} onChange={(workflow) => { setForm({ ...form, workflow }); }} />
        <Checklist isStacked legend="可以掛在哪些類型底下（都不勾就不能有上層）" options={typeOptions} value={form.parents} onChange={(parents) => { setForm({ ...form, parents }); }} />
        <Checklist isStacked legend="要套用的自訂欄位" options={fieldOptions} value={fields} onChange={(next) => { setForm({ ...form, fields: next }); }} />
        <DialogFormActions submitLabel="加入" pendingLabel="加入" isPending={false} onCancel={onClose} />
      </form>
    </Dialog>
  );
}
