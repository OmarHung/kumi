import type { UseMutationResult } from '@tanstack/react-query';
import { errorMessage } from '../../../api/http';
import type { KnowledgePageDeletion, KnowledgePageNode } from '../../../api/knowledgeTypes';
import { ConfirmDialog } from '../../../components/Dialog/ConfirmDialog';
import { useKnowledgePage } from '../space/spaceQueries';
import { descendantNumbers, type TreeModel } from '../tree/treeModel';

interface DeletePageDialogProps {
  readonly spaceKey: string;
  readonly node: KnowledgePageNode;
  readonly model: TreeModel;
  readonly remove: UseMutationResult<KnowledgePageDeletion, Error, KnowledgePageNode>;
  readonly onClose: () => void;
}

/** 刪不了的原因；可以刪是 null。要讀到那一頁才知道底下有沒有自己看不到的頁面（樹上只有看得到的）。 */
function blockedReasonOf(page: ReturnType<typeof useKnowledgePage>): string | null {
  if (page.isPending) return '正在確認這一頁能不能刪除…';
  if (page.data === undefined) return '現在無法確認這一頁能不能刪除，請稍後再試。';
  const { canEdit, canDelete } = page.data.myPermissions;
  if (!canEdit) return '你沒有權限刪除這一頁。';
  return canDelete ? null : '底下有你看不到的頁面，不能刪除。';
}

/**
 * 刪除一頁的確認（docs/design/08-interaction.md 8.3「刪除與垃圾桶」）：說出後果（連同底下幾頁、進垃圾桶、能復原）。
 * 選單裡的「刪除…」一直都在（不因為底下有看不到的頁面就藏起來，使用者才知道為什麼刪不了），打開之後才說明原因。
 */
export function DeletePageDialog({ spaceKey, node, model, remove, onClose }: DeletePageDialogProps) {
  const page = useKnowledgePage(spaceKey, node.number);
  const below = descendantNumbers(model, node).size;
  const description =
    below === 0
      ? '這一頁會放進垃圾桶，可以從垃圾桶復原。'
      : `連同底下 ${String(below)} 頁一起放進垃圾桶，可以從垃圾桶復原。`;
  return (
    <ConfirmDialog
      title={`刪除「${node.title}」？`}
      description={description}
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={remove.isPending}
      keepsFocusWhilePending
      blockedReason={blockedReasonOf(page)}
      error={remove.isError ? errorMessage(remove.error) : null}
      onConfirm={() => {
        remove.mutate(node, { onSuccess: onClose });
      }}
      onCancel={onClose}
    />
  );
}
