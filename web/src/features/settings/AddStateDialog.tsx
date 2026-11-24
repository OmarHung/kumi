import { useState, type SubmitEvent } from 'react';
import type { StateCategory, Workflow, WorkflowState } from '../../api/blueprint';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { SelectField, TextField } from '../../components/Field/Field';
import { addState } from '../../domain/settingsDraft';
import { displayName } from '../../domain/settingsNames';
import { addStateTransitions } from '../../domain/settingsTransitions';
import { KEY_HINT, nameError, stateKeyError, suggestKey } from '../../domain/settingsValidation';
import { CATEGORY_OPTIONS } from '../work-items/selectOptionMarks';
import { Checklist } from './Checklist';
import { useSettingsDialog, useSettingsEditor } from './SettingsContext';
import styles from './Settings.module.css';

interface AddStateDialogProps {
  readonly workflow: Workflow;
  readonly onClose: () => void;
}

interface StateForm {
  readonly name: string;
  /** null 表示還沒自己改過 key，跟著名稱給建議值。 */
  readonly key: string | null;
  readonly category: StateCategory;
  readonly position: number;
  /** 從哪些狀態可以轉進來、可以轉去哪些狀態；null 表示還沒自己勾過，跟著位置與類別給預設。 */
  readonly incoming: readonly string[] | null;
  readonly outgoing: readonly string[] | null;
}

interface FormErrors {
  readonly name?: string;
  readonly key?: string;
}

function isTerminal(category: StateCategory): boolean {
  return category === 'done' || category === 'canceled';
}

/** 預設放在第一個結束類（完成、已取消）的狀態前面：新的狀態通常是流程中間的一步。 */
function defaultPosition(states: readonly { readonly category: StateCategory }[]): number {
  const index = states.findIndex((state) => isTerminal(state.category));
  return index < 0 ? states.length : index;
}

/** 預設從插入位置的前一個狀態轉進來、轉去後一個狀態；完成或已取消類的新狀態預設不轉出去。 */
function defaultLinks(states: readonly WorkflowState[], form: StateForm) {
  const before = states[form.position - 1];
  const after = states[form.position];
  return {
    incoming: form.incoming ?? (before === undefined ? [] : [before.key]),
    outgoing: form.outgoing ?? (after === undefined || isTerminal(form.category) ? [] : [after.key]),
  };
}

interface LinksProps {
  readonly states: readonly WorkflowState[];
  readonly form: StateForm;
  readonly setForm: (form: StateForm) => void;
}

function StateLinks({ states, form, setForm }: LinksProps) {
  // 草稿裡的名稱（正在改的那一個）；空的時候用 key，才看得出是哪一個。
  const options = states.map((state) => ({ key: state.key, label: displayName(state) }));
  const { incoming, outgoing } = defaultLinks(states, form);
  return (
    <>
      <Checklist isStacked legend="從哪些狀態可以轉進來" options={options} value={incoming} onChange={(next) => { setForm({ ...form, incoming: next }); }} />
      <Checklist isStacked legend="可以轉去哪些狀態" options={options} value={outgoing} onChange={(next) => { setForm({ ...form, outgoing: next }); }} />
    </>
  );
}

function PositionField({ states, form, setForm }: LinksProps) {
  const options = [
    { value: '0', label: '放在最前面（新項目的起始狀態）' },
    ...states.map((state, index) => ({ value: String(index + 1), label: `放在「${displayName(state)}」之後` })),
  ];
  return <SelectField label="位置" value={String(form.position)} options={options} onChange={(position) => { setForm({ ...form, position: Number(position) }); }} />;
}

/**
 * 新增狀態：名稱、key（有建議值，可以自己改）、類別、放在哪裡；有限制轉換的流程再選從哪裡轉進來、可以轉去哪裡。
 * 加進草稿，還沒送出。
 */
export function AddStateDialog({ workflow, onClose }: AddStateDialogProps) {
  const editor = useSettingsEditor();
  useSettingsDialog();
  const draftWorkflow = editor.draft.workflows.find((candidate) => candidate.key === workflow.key);
  const states = draftWorkflow?.states ?? [];
  const isRestricted = draftWorkflow?.transitions !== undefined;
  const [form, setForm] = useState<StateForm>(() => ({ name: '', key: null, category: 'in_progress', position: defaultPosition(states), incoming: null, outgoing: null }));
  const [errors, setErrors] = useState<FormErrors>({});
  const key = form.key ?? suggestKey(form.name, 'state', new Set([...workflow.states, ...states].map((state) => state.key)));

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = { name: nameError(form.name), key: stateKeyError(editor.base, editor.draft, workflow.key, key) };
    setErrors(next);
    if (next.name !== undefined || next.key !== undefined) return;
    const state = { key, name: form.name.trim(), category: form.category };
    const { incoming, outgoing } = defaultLinks(states, form);
    editor.update((draft) => addStateTransitions(addState(draft, workflow.key, state, form.position), workflow.key, key, incoming, outgoing));
    onClose();
  }

  const description = isRestricted
    ? `「${workflow.name}」有限制轉換：選新狀態從哪些狀態轉進來、可以轉去哪些狀態，之後也可以在「轉換」的表格裡改。`
    : `加進「${workflow.name}」。送出變更之前都還可以改。`;
  return (
    <Dialog title="新增狀態" description={description} onClose={onClose}>
      <form className={styles.dialogForm} noValidate onSubmit={handleSubmit}>
        <TextField label="名稱" value={form.name} error={errors.name} onChange={(event) => { setForm({ ...form, name: event.target.value }); }} />
        <TextField label="key" hint={KEY_HINT} value={key} error={errors.key} onChange={(event) => { setForm({ ...form, key: event.target.value }); }} />
        <SelectField label="類別" value={form.category} options={CATEGORY_OPTIONS} onChange={(category) => { setForm({ ...form, category: category as StateCategory }); }} />
        <PositionField states={states} form={form} setForm={setForm} />
        {isRestricted && <StateLinks states={states} form={form} setForm={setForm} />}
        <DialogFormActions submitLabel="加入" pendingLabel="加入" isPending={false} onCancel={onClose} />
      </form>
    </Dialog>
  );
}
