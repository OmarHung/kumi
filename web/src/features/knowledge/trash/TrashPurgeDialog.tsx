import { errorMessage } from '../../../api/http';
import { ConfirmDialog } from '../../../components/Dialog/ConfirmDialog';
import type { TrashPurge } from './useTrashPurge';

const WARNING = '永久刪除之後不能復原，頁面、版本、附件一起刪掉。';

/**
 * 永久刪除的確認（docs/design/08-interaction.md 8.3「刪除與垃圾桶」）：知識庫唯一不能復原的動作，所以和一般的刪除分開，
 * 確認鈕用 danger、寫出會刪掉什麼；失敗的原因（已經不在了、空間封存了、沒有權限）留在對話框裡。
 */
export function TrashPurgeDialog({ purge }: { readonly purge: TrashPurge }) {
  const { target, mutation, close } = purge;
  if (target === null) return null;
  const scope =
    target.title === null
      ? '會刪掉垃圾桶裡刪除超過 30 天的頁面（一次最多 200 批、2,000 頁；還有剩的話再清一次）。'
      : `會刪掉「${target.title}」${target.pageCount === null ? '' : `（這一批共 ${String(target.pageCount)} 頁）`}。`;
  return (
    <ConfirmDialog
      title={target.title === null ? '清掉 30 天以上的頁面？' : `永久刪除「${target.title}」？`}
      description={WARNING}
      confirmLabel="永久刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={mutation.isPending}
      keepsFocusWhilePending
      error={mutation.isError ? errorMessage(mutation.error) : null}
      onConfirm={() => {
        mutation.mutate(target);
      }}
      onCancel={close}
    >
      <p>{scope}</p>
    </ConfirmDialog>
  );
}
