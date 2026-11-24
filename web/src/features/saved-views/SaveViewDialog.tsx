import { useEffect, useRef, useState, type SubmitEvent } from 'react';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { nameLength, SAVED_VIEW_NAME_MAX_LENGTH } from '../../domain/savedViews';
import { mapApiErrorToFields } from '../../lib/formErrors';
import styles from './SavedViews.module.css';

/** 伺服器的欄位問題對到名稱；名稱重複是 409，沒有 path，用代碼對。其他（例如檢視滿了）放在表單上方。 */
const NAME_MAPPING = { fields: ['name'], codes: { 'saved_view.name_taken': 'name' } } as const;

interface SaveViewDialogProps {
  readonly title: string;
  readonly description: string;
  readonly submitLabel: string;
  readonly pendingLabel: string;
  readonly initialName: string;
  readonly isPending: boolean;
  /** 送出失敗的原因（伺服器的回應）；null 是沒有。 */
  readonly error: unknown;
  /** 名稱已經檢查過（去掉前後空白、長度）才會呼叫。 */
  readonly onSubmit: (name: string) => void;
  /** 打字時清掉上一次的失敗。 */
  readonly onEdit: () => void;
  readonly onClose: () => void;
}

function nameProblem(name: string): string | null {
  if (name === '') return '請輸入檢視的名稱。';
  return nameLength(name) > SAVED_VIEW_NAME_MAX_LENGTH ? `名稱最多 ${String(SAVED_VIEW_NAME_MAX_LENGTH)} 個字。` : null;
}

/**
 * 另存成檢視與重新命名共用：只有一個名稱。送出中不讓 Esc 或「取消」關掉，結果才不會沒人看到；
 * 名稱重複（不分大小寫）留在欄位上、焦點回到名稱，改了就能再送。
 */
export function SaveViewDialog({ title, description, submitLabel, pendingLabel, initialName, isPending, error, onSubmit, onEdit, onClose }: SaveViewDialogProps) {
  const [name, setName] = useState(initialName);
  const [localError, setLocalError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const mapped = error === null || error === undefined ? null : mapApiErrorToFields(error, NAME_MAPPING);
  const fieldError = localError ?? mapped?.fieldErrors.name;
  const close = isPending ? () => undefined : onClose;
  const hasNameError = mapped?.fieldErrors.name !== undefined;

  // 預設的名稱整個選取：要換名稱直接打字就好，不用先清掉。
  useEffect(() => {
    inputRef.current?.select();
  }, []);

  // 送出中焦點留在送出鈕（keepsFocusWhilePending）；名稱重複的回應回來之後，焦點到名稱，改了就能再送。
  useEffect(() => {
    if (hasNameError) inputRef.current?.focus();
  }, [error, hasNameError]);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = name.trim();
    const problem = nameProblem(trimmed);
    setLocalError(problem);
    if (problem !== null) {
      inputRef.current?.focus();
      return;
    }
    onSubmit(trimmed);
  }

  return (
    <Dialog title={title} description={description} onClose={close}>
      <form className={styles.form} noValidate onSubmit={handleSubmit}>
        <TextField
          ref={inputRef}
          label="名稱"
          value={name}
          error={fieldError}
          onChange={(event) => {
            setName(event.target.value);
            setLocalError(null);
            onEdit();
          }}
        />
        <FormAlert message={mapped?.formError ?? null} />
        <DialogFormActions submitLabel={submitLabel} pendingLabel={pendingLabel} isPending={isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
