import { useEffect, useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { mapApiErrorToFields } from '../../lib/formErrors';
import { PROJECT_ERROR_MAPPING, validateProject } from './projectForm';
import { useRenameProject } from './useProjectLifecycle';
import styles from './NewProjectDialog.module.css';

interface RenameProjectDialogProps {
  readonly projectKey: string;
  readonly currentName: string;
  readonly onClose: () => void;
}

const NAME_MAPPING = { fields: ['name'] as const, codes: PROJECT_ERROR_MAPPING.codes } as const;

/** 打開時預設的名稱整個選取；伺服器退回之後焦點回到名稱，改了就能再送。 */
function useNameFocus(inputRef: RefObject<HTMLInputElement | null>, hasServerError: boolean, error: unknown) {
  useEffect(() => {
    inputRef.current?.select();
  }, [inputRef]);
  useEffect(() => {
    if (hasServerError) inputRef.current?.focus();
  }, [inputRef, hasServerError, error]);
}

/**
 * 改專案的名稱（代號不能改：它是項目編號的前綴）。預設是目前的名稱、整個選取；伺服器的 422 放在欄位旁。
 * 送出中不讓 Esc 或「取消」關掉，結果才不會沒人看到。名稱沒變就直接關上，不送請求。
 */
export function RenameProjectDialog({ projectKey, currentName, onClose }: RenameProjectDialogProps) {
  const [name, setName] = useState(currentName);
  const [localError, setLocalError] = useState<string | undefined>(undefined);
  const inputRef = useRef<HTMLInputElement>(null);
  const rename = useRenameProject(projectKey);
  const mapped = rename.isError ? mapApiErrorToFields(rename.error, NAME_MAPPING) : null;
  const fieldError = localError ?? mapped?.fieldErrors.name;
  const close = rename.isPending ? () => undefined : onClose;
  const hasServerError = mapped !== null;

  useNameFocus(inputRef, hasServerError, rename.error);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = name.trim();
    const problem = validateProject({ key: '', name: trimmed, template: '' }).name;
    setLocalError(problem);
    if (problem !== undefined) {
      inputRef.current?.focus();
      return;
    }
    if (trimmed === currentName) {
      onClose();
      return;
    }
    rename.mutate(trimmed, { onSuccess: onClose });
  }

  return (
    <Dialog title="重新命名專案" description={`只改名稱；代號 ${projectKey} 不能改，它是項目編號的前綴。`} onClose={close}>
      <form className={styles.form} noValidate onSubmit={handleSubmit}>
        <TextField
          ref={inputRef}
          label="名稱"
          value={name}
          autoComplete="off"
          error={fieldError}
          onChange={(event) => {
            setName(event.target.value);
            setLocalError(undefined);
            if (rename.isError) rename.reset();
          }}
        />
        <FormAlert message={mapped?.formError ?? null} />
        <DialogFormActions submitLabel="儲存" pendingLabel="儲存中…" isPending={rename.isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
