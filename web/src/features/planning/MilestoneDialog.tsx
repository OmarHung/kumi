import { useRef } from 'react';
import type { MilestoneDraft } from '../../api/planning';
import { DateField } from '../../components/DatePicker/DateField';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { fieldControlClassName, FieldShell, TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { MILESTONE_DESCRIPTION_MAX, validateMilestone } from './planningRules';
import { usePlanningForm } from './usePlanningForm';
import styles from './Planning.module.css';

const MAPPING = { fields: ['name', 'dueDate', 'description'], codes: { 'milestone.name_taken': 'name' } } as const;

interface MilestoneDialogProps {
  readonly title: string;
  readonly submitLabel: string;
  readonly initial: MilestoneDraft;
  readonly isPending: boolean;
  readonly error: unknown;
  readonly onSubmit: (draft: MilestoneDraft) => void;
  readonly onEdit: () => void;
  readonly onClose: () => void;
}

/** 說明空白（或只有空白字元）送 null：伺服器把「沒有說明」存成 null，不存空字串。 */
function normalizeMilestone(draft: MilestoneDraft): MilestoneDraft {
  const description = draft.description?.trim() ?? '';
  return { ...draft, name: draft.name.trim(), description: description === '' ? null : description };
}

/** 新增與編輯里程碑共用：名稱、到期日、說明（可以不填）。 */
export function MilestoneDialog({ title, submitLabel, initial, isPending, error, onSubmit, onEdit, onClose }: MilestoneDialogProps) {
  const nameRef = useRef<HTMLInputElement>(null);
  const form = usePlanningForm({ nameRef, initial, mapping: MAPPING, validate: validateMilestone, normalize: normalizeMilestone, error, onSubmit, onEdit });
  const close = isPending ? () => undefined : onClose;
  const descriptionError = form.errorOf('description');
  return (
    <Dialog title={title} onClose={close}>
      <form className={styles.form} noValidate onSubmit={form.handleSubmit}>
        <TextField
          ref={nameRef}
          label="名稱"
          value={form.draft.name}
          error={form.errorOf('name')}
          onChange={(event) => {
            form.change({ name: event.target.value });
          }}
        />
        <DateField label="到期日" value={form.draft.dueDate} error={form.errorOf('dueDate')} onChange={(dueDate) => { form.change({ dueDate }); }} />
        <FieldShell label="說明" hint={`可以不填，最多 ${MILESTONE_DESCRIPTION_MAX.toLocaleString('en-US')} 個字。`} error={descriptionError}>
          {(control) => (
            <textarea
              id={control.id}
              className={fieldControlClassName('textarea', descriptionError !== undefined)}
              rows={3}
              value={form.draft.description ?? ''}
              aria-invalid={descriptionError === undefined ? undefined : true}
              aria-describedby={control.describedBy}
              onChange={(event) => {
                form.change({ description: event.target.value });
              }}
            />
          )}
        </FieldShell>
        <FormAlert message={form.formError} />
        <DialogFormActions submitLabel={submitLabel} pendingLabel="送出中…" isPending={isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
