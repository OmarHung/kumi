import { Button } from '../../components/Button/Button';
import { Dialog, DialogActions } from '../../components/Dialog/Dialog';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Icon } from '../../components/Icon/Icon';
import type { ItemFilters } from '../../domain/itemFilters';
import { CreatedStatus } from './CreatedStatus';
import { QuickAddFields } from './QuickAddItem';
import { useQuickAdd, type QuickAddState } from './useQuickAdd';
import styles from './QuickAddItem.module.css';

interface AddItemDialogProps {
  readonly filters: ItemFilters;
  readonly state: QuickAddState;
}

/**
 * 窄螢幕的新增：和表格上方那一列同一套欄位與送出（useQuickAdd → useCreateItem），只是放在對話框裡、標籤看得到。
 * 新增之後對話框留著（標題清空、焦點在標題框），可以接著加；「關閉」或 Esc 關上，焦點回到「新增項目」。
 * 開著沒有與打到一半的內容放在 QuickAddState（不隨寬窄換掉）：視窗變寬時對話框照樣開著，內容還在。
 */
export function AddItemDialog({ filters, state }: AddItemDialogProps) {
  const quickAdd = useQuickAdd(filters, state);
  const { create } = quickAdd;
  const close = () => {
    if (!create.isPending) state.setIsDialogOpen(false);
  };
  return (
    <Dialog title="新增項目" onClose={close}>
      <form className={styles.dialogForm} aria-label="新增項目" noValidate onSubmit={quickAdd.handleSubmit}>
        <FormAlert message={create.formError} />
        <QuickAddFields quickAdd={quickAdd} placeholder="輸入標題後按 Enter" />
        <CreatedStatus created={create.created} className={styles.status} />
        <DialogActions>
          <Button onClick={close} disabled={create.isPending}>
            關閉
          </Button>
          <Button type="submit" variant="primary" disabled={create.isPending}>
            {create.isPending ? '新增中…' : '新增'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** 窄螢幕工具列上的「新增項目」：打開上面的對話框（快速新增那一列在窄螢幕放不下）。 */
export function AddItemButton({ onOpen }: { readonly onOpen: () => void }) {
  return (
    <Button size="sm" variant="primary" onClick={onOpen}>
      <Icon name="add" />
      新增項目
    </Button>
  );
}
