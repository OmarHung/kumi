import type { DeletedItemSummary } from '../../api/itemRecovery';
import { Button } from '../../components/Button/Button';
import { restoreLabel } from './deletedItemRows';

interface RestoreButtonProps {
  readonly item: DeletedItemSummary;
  readonly isPending: boolean;
  readonly onRestore: (item: DeletedItemSummary) => void;
}

/** 屬性 data-restore-key：復原之後與「先復原上層」的連結用它找到那一列的按鈕、把焦點放上去。 */
export const RESTORE_KEY_ATTRIBUTE = 'data-restore-key';

/**
 * 送出中標 aria-disabled（不用 disabled）：停用的按鈕會讓焦點掉到頁面本體，
 * 失敗之後（那一列還在）使用者得重新找回原來的位置。名稱不變（復原 AGY-7），只換文字。
 */
export function RestoreButton({ item, isPending, onRestore }: RestoreButtonProps) {
  return (
    <Button
      size="sm"
      variant="ghost"
      aria-label={restoreLabel(item)}
      aria-disabled={isPending || undefined}
      {...{ [RESTORE_KEY_ATTRIBUTE]: item.key }}
      onClick={() => {
        if (!isPending) onRestore(item);
      }}
    >
      {isPending ? '復原中…' : '復原'}
    </Button>
  );
}
