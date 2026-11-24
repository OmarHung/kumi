import { useId, useState } from 'react';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { Dialog, DialogActions, DialogFormActions } from '../../components/Dialog/Dialog';
import { SelectField, TextField } from '../../components/Field/Field';
import { tableStyles } from '../../components/Table/Table';
import { CATEGORY_OPTIONS } from '../work-items/selectOptionMarks';
import { Demo } from './DemoSection';
import styles from './Design.module.css';

type OpenDialog = 'form' | 'lg' | 'danger' | 'blocked' | null;

function FormDialog({ onClose }: { readonly onClose: () => void }) {
  const reasonId = useId();
  // 對話框裡的下拉：清單浮在對話框上面（top layer），開著時 Esc 只關清單。
  const [category, setCategory] = useState('in_progress');
  return (
    <Dialog title="新增狀態" description="一般大小的對話框：要先填幾個欄位才能完成的動作。" onClose={onClose}>
      <form
        className={styles.dialogForm}
        onSubmit={(event) => {
          event.preventDefault();
        }}
      >
        <TextField label="名稱" defaultValue="內部驗收" />
        <SelectField label="類別" value={category} options={CATEGORY_OPTIONS} onChange={setCategory} />
        <p id={reasonId} className={styles.note}>
          「交付流程」有限制轉換：要先選從哪些狀態可以轉進來，才能新增。
        </p>
        <DialogFormActions submitLabel="新增狀態" pendingLabel="新增中…" isPending={false} blockedReasonIds={[reasonId]} onCancel={onClose} />
      </form>
    </Dialog>
  );
}

function TableDialog({ onClose }: { readonly onClose: () => void }) {
  return (
    <Dialog title="成員" description="有表格的對話框用 size=&quot;lg&quot;。" size="lg" onClose={onClose}>
      <table className={tableStyles.table}>
        <thead>
          <tr>
            <th scope="col">名稱</th>
            <th scope="col">角色</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>範例使用者</td>
            <td>專案經理</td>
          </tr>
        </tbody>
      </table>
      <DialogActions>
        <Button variant="primary" onClick={onClose}>
          關閉
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function OpenButtons({ onOpen }: { readonly onOpen: (dialog: OpenDialog) => void }) {
  return (
    <>
      <Button onClick={() => { onOpen('form'); }}>打開一般的對話框</Button>
      <Button onClick={() => { onOpen('lg'); }}>打開有表格的對話框</Button>
      <Button variant="danger" onClick={() => { onOpen('danger'); }}>刪除 AGY-7…</Button>
      <Button onClick={() => { onOpen('blocked'); }}>套用變更集…</Button>
    </>
  );
}

/** 對話框的四種：一般的表單（送出鈕停用但停得到）、有表格的、不可回復的確認、還不能確認的確認。 */
export function DialogDemos() {
  const [open, setOpen] = useState<OpenDialog>(null);
  const close = () => {
    setOpen(null);
  };
  return (
    <Demo name="Dialog／ConfirmDialog">
      <OpenButtons onOpen={setOpen} />
      {open === 'form' && <FormDialog onClose={close} />}
      {open === 'lg' && <TableDialog onClose={close} />}
      {open === 'danger' && (
        <ConfirmDialog
          title="刪除 AGY-7「首頁」？"
          description="刪除之後不會出現在任何清單、看板與我的工作；活動紀錄留著。"
          confirmLabel="刪除"
          tone="danger"
          onConfirm={close}
          onCancel={close}
        />
      )}
      {open === 'blocked' && (
        <ConfirmDialog
          title="套用變更集 #3？"
          description="套用之後是藍圖第 4 版，不能一鍵還原。"
          confirmLabel="套用"
          blockedReason="正在確認會動到哪些項目…"
          onConfirm={close}
          onCancel={close}
        />
      )}
    </Demo>
  );
}
