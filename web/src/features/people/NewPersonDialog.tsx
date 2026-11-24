import type { Person } from '../../api/types';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { NewPersonFields, useNewPersonForm } from './NewPersonFields';
import styles from './PeoplePage.module.css';

interface NewPersonDialogProps {
  readonly onClose: () => void;
  readonly onCreated: (person: Person) => void;
}

/** 組織管理員新增人員：名稱、Email、初始密碼、組織角色。欄位與送出在 NewPersonFields.tsx（成員對話框也用）。 */
export function NewPersonDialog({ onClose, onCreated }: NewPersonDialogProps) {
  const form = useNewPersonForm(onCreated);

  // 送出中不讓 Esc 或「取消」關掉，結果才不會沒人看到。
  const close = form.isPending ? () => undefined : onClose;

  return (
    <Dialog title="新增人員" description="新的人用這裡設定的 Email 與密碼登入。" onClose={close}>
      <form className={styles.form} noValidate onSubmit={form.handleSubmit}>
        <NewPersonFields form={form} />
        <DialogFormActions submitLabel="新增" pendingLabel="新增中…" isPending={form.isPending} onCancel={close} />
      </form>
    </Dialog>
  );
}
