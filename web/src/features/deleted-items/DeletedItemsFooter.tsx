import { errorMessage } from '../../api/http';
import { Button } from '../../components/Button/Button';
import type { useDeletedItemPages } from './useDeletedItemPages';
import styles from './DeletedItems.module.css';

interface DeletedItemsFooterProps {
  readonly result: ReturnType<typeof useDeletedItemPages>;
  readonly count: number;
}

/**
 * 清單的最下面：已載入幾筆、「載入更多」、載入更多失敗的說明（和項目清單的 LoadMore 一樣）。
 * 載入中標 aria-disabled（不用 disabled）：用鍵盤按的，焦點留在按鈕上。
 */
export function DeletedItemsFooter({ result, count }: DeletedItemsFooterProps) {
  return (
    <div className={styles.footer}>
      <p role="status" className={styles.count}>
        {`已載入 ${String(count)} 個項目`}
      </p>
      {result.hasNextPage && (
        <Button
          size="sm"
          aria-disabled={result.isFetching || undefined}
          onClick={() => {
            if (!result.isFetching) void result.fetchNextPage();
          }}
        >
          {result.isFetchingNextPage ? '載入中…' : '載入更多'}
        </Button>
      )}
      {result.isFetchNextPageError && (
        <p role="alert" className={styles.loadError}>
          載入更多失敗：{errorMessage(result.error)}
        </p>
      )}
    </div>
  );
}
