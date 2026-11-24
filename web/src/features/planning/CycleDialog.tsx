import { useRef } from 'react';
import type { CycleDraft } from '../../api/planning';
import { DateField } from '../../components/DatePicker/DateField';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { validateCycle } from './planningRules';
import { usePlanningForm } from './usePlanningForm';
import styles from './Planning.module.css';

/** 重疊與滿了沒有欄位可以對，留在表單上方；名稱重複（409）沒有 path，用代碼對到名稱。 */
const MAPPING = { fields: ['name', 'startDate', 'endDate'], codes: { 'cycle.name_taken': 'name' } } as const;

interface CycleDialogProps {
  readonly title: string;
  readonly submitLabel: string;
  readonly initial: CycleDraft;
  readonly isPending: boolean;
  /** 送出失敗的原因（伺服器的回應）。 */
  readonly error: unknown;
  readonly onSubmit: (draft: CycleDraft) => void;
  /** 改了欄位時清掉上一次的失敗。 */
  readonly onEdit: () => void;
  readonly onClose: () => void;
}

/** 新增與編輯週期共用。送出中不讓 Esc 或「取消」關掉，結果才不會沒人看到。 */
export function CycleDialog({ title, submitLabel, initial, isPending, error, onSubmit, onEdit, onClose }: CycleDialogProps) {
  const nameRef = useRef<HTMLInputElement>(null);
  const form = usePlanningForm({
    nameRef,
    initial,
    mapping: MAPPING,
    validate: validateCycle,
    normalize: (draft) => ({ ...draft, name: draft.name.trim() }),
    error,
    onSubmit,
    onEdit,
  });
  const close = isPending ? () => undefined : onClose;
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
        <div className={styles.dateRow}>
          <DateField label="開始日" value={form.draft.startDate} error={form.errorOf('startDate')} onChange={(startDate) => { form.change({ startDate }); }} />
          <DateField label="結束日" value={form.draft.endDate} error={form.errorOf('endDate')} onChange={(endDate) => { form.change({ endDate }); }} />
        </div>
        <FormAlert message={form.formError} />
        <DialogFormActions submitLabel={submitLabel} pendingLabel="送出中…" isPending={isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
