import type { ProjectBlueprint } from '../../api/blueprint';
import type { DeletedItemSummary } from '../../api/itemRecovery';
import { EmptyState, ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { useIsCompact } from '../../lib/useMediaQuery';
import { DeletedItemsCompactList } from './DeletedItemsCompactList';
import { DeletedItemsFooter } from './DeletedItemsFooter';
import { DeletedItemsTable } from './DeletedItemsTable';
import type { useDeletedItemPages } from './useDeletedItemPages';

interface DeletedItemsBodyProps {
  readonly result: ReturnType<typeof useDeletedItemPages>;
  readonly items: readonly DeletedItemSummary[];
  readonly blueprint: ProjectBlueprint;
  readonly now: number;
  readonly canRestore: boolean;
  readonly pendingKeys: ReadonlySet<string>;
  readonly onRestore: (item: DeletedItemSummary) => void;
}

/** 載入中、讀取失敗、空的、表格（窄螢幕是清單）加「載入更多」。 */
export function DeletedItemsBody({ result, items, ...listProps }: DeletedItemsBodyProps) {
  const isCompact = useIsCompact();
  if (result.isPending) return <LoadingState label="正在載入已刪除的項目…" />;
  if (result.isLoadingError) {
    return (
      <ErrorState
        error={result.error}
        title="已刪除的項目載入失敗"
        onRetry={() => {
          void result.refetch();
        }}
      />
    );
  }
  if (items.length === 0) {
    return <EmptyState title="沒有已刪除的項目">刪除的項目不會出現在清單、看板與我的工作，會先放在這裡，需要時可以復原。</EmptyState>;
  }
  return (
    <>
      {isCompact ? <DeletedItemsCompactList items={items} {...listProps} /> : <DeletedItemsTable items={items} {...listProps} />}
      <DeletedItemsFooter result={result} count={items.length} />
    </>
  );
}
