import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { errorMessage } from '../../../api/http';
import { listTrash, restorePage } from '../../../api/knowledge';
import type { KnowledgePage, KnowledgeSpace, KnowledgeTrashCapacity, KnowledgeTrashEntry } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';
import { Button } from '../../../components/Button/Button';
import { Notice } from '../../../components/Notice/Notice';
import { List, ListRow, type ListMeta } from '../../../components/List/List';
import { PageBar } from '../../../components/PageBar/PageBar';
import { PageLayout } from '../../../components/PageLayout/PageLayout';
import { Skeleton } from '../../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState, RefetchErrorNotice } from '../../../components/StatusViews/StatusViews';
import { formatRelativeTime } from '../../../domain/time';
import { KNOWLEDGE_HOME, knowledgePageUrl, knowledgeSpaceUrl } from '../knowledgeLinks';
import { SpaceGate } from '../SpaceGate';
import { TrashPurgeDialog } from './TrashPurgeDialog';
import { useTrashPurge, type PurgeOutcome, type TrashPurge } from './useTrashPurge';
import styles from '../Knowledge.module.css';

const SKELETON_ROWS = 3;

/** 每一批的復原：成功之後那一列換成「已復原」的說明（清單與容量照樣重抓，但那一列留在原位，清單不跳動）。 */
type RestoreOutcome =
  | { readonly status: 'restored'; readonly page: KnowledgePage; readonly entry: KnowledgeTrashEntry; readonly index: number }
  | { readonly status: 'failed'; readonly message: string };

interface RestoreRequest {
  readonly entry: KnowledgeTrashEntry;
  readonly index: number;
}

/** 重抓之後已經不在伺服器清單裡的「已復原」列，放回它原本的位置。 */
function withRestoredRows(entries: readonly KnowledgeTrashEntry[], outcomes: ReadonlyMap<string, RestoreOutcome>): readonly KnowledgeTrashEntry[] {
  const present = new Set(entries.map((entry) => entry.deletionId));
  const vanished = [...outcomes.values()]
    .flatMap((outcome) => (outcome.status === 'restored' && !present.has(outcome.entry.deletionId) ? [outcome] : []))
    .sort((a, b) => a.index - b.index);
  const merged = [...entries];
  vanished.forEach((outcome) => {
    merged.splice(Math.min(outcome.index, merged.length), 0, outcome.entry);
  });
  return merged;
}

function useRestore(spaceKey: string) {
  const queryClient = useQueryClient();
  const [outcomes, setOutcomes] = useState<ReadonlyMap<string, RestoreOutcome>>(new Map());
  // 可以連按幾批：一格會被先完成的那一批清掉，後面還在送的按鈕就被放開了。
  const [pendingIds, setPendingIds] = useState<ReadonlySet<string>>(new Set());
  const setPending = (deletionId: string, isPending: boolean) => {
    setPendingIds((current) => {
      const next = new Set(current);
      if (isPending) next.add(deletionId);
      else next.delete(deletionId);
      return next;
    });
  };
  const record = (deletionId: string, outcome: RestoreOutcome | null) => {
    setOutcomes((current) => {
      const next = new Map(current);
      if (outcome === null) next.delete(deletionId);
      else next.set(deletionId, outcome);
      return next;
    });
  };
  const mutation = useMutation({
    mutationFn: ({ entry }: RestoreRequest) => restorePage(spaceKey, entry.page.number),
    onMutate: ({ entry }) => {
      setPending(entry.deletionId, true);
      record(entry.deletionId, null);
    },
    onSuccess: (page, { entry, index }) => {
      record(entry.deletionId, { status: 'restored', page, entry, index });
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeTree(spaceKey) });
      // 容量的批數在清單的第一頁裡，一起重抓。
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeTrash(spaceKey) });
    },
    onError: (error, { entry }) => {
      record(entry.deletionId, { status: 'failed', message: errorMessage(error) });
    },
    onSettled: (_page, _error, { entry }) => {
      setPending(entry.deletionId, false);
    },
  });
  return { outcomes, pendingIds, restore: (entry: KnowledgeTrashEntry, index: number) => { mutation.mutate({ entry, index }); } };
}

function metaOf(entry: KnowledgeTrashEntry, now: number): readonly ListMeta[] {
  return [
    ...(entry.pageCount > 1 ? [{ key: 'count', content: `連同底下 ${String(entry.pageCount - 1)} 頁` }] : []),
    ...(entry.formerParent === null ? [] : [{ key: 'parent', content: `原本在「${entry.formerParent.title}」底下` }]),
    { key: 'deleted', content: `${entry.deletedBy.displayName} · ${formatRelativeTime(entry.deletedAt, now)}刪除` },
  ];
}

interface RowActionsProps {
  readonly space: KnowledgeSpace;
  readonly entry: KnowledgeTrashEntry;
  readonly isPending: boolean;
  readonly index: number;
  readonly restore: (entry: KnowledgeTrashEntry, index: number) => void;
  readonly purge: TrashPurge;
}

/** 一列的動作：復原（canEdit），與永久刪除…（只有空間的管理者；不能復原，所以是 ghost、要再確認）。 */
function RowActions({ space, entry, index, isPending, restore, purge }: RowActionsProps) {
  return (
    <>
      {space.myPermissions.canEdit && (
        <Button
          size="sm"
          disabled={isPending}
          aria-label={`復原「${entry.page.title}」`}
          onClick={() => {
            restore(entry, index);
          }}
        >
          {isPending ? '復原中…' : '復原'}
        </Button>
      )}
      {space.myPermissions.canManage && (
        <Button
          size="sm"
          variant="ghost"
          aria-label={`永久刪除「${entry.page.title}」…`}
          onClick={() => {
            purge.open({ deletionId: entry.deletionId, title: entry.page.title, pageCount: entry.pageCount });
          }}
        >
          永久刪除…
        </Button>
      )}
    </>
  );
}

interface TrashListProps {
  readonly space: KnowledgeSpace;
  readonly entries: readonly KnowledgeTrashEntry[];
  readonly restoreState: ReturnType<typeof useRestore>;
  readonly purge: TrashPurge;
  /** 清單下面的東西（「載入更多」）。 */
  readonly footer: ReactNode;
}

function TrashList({ space, entries, restoreState, purge, footer }: TrashListProps) {
  const { outcomes, pendingIds, restore } = restoreState;
  const [now] = useState(() => Date.now());
  return (
    <List label="垃圾桶" footer={footer}>
      {entries.map((entry, index) => {
        const outcome = outcomes.get(entry.deletionId);
        if (outcome?.status === 'restored') {
          const page = outcome.page;
          return (
            <ListRow
              key={entry.deletionId}
              to={knowledgePageUrl(space.key, page.number)}
              isContentStart={index === 0}
              title={`已復原「${page.title}」`}
            />
          );
        }
        const isPending = pendingIds.has(entry.deletionId);
        return (
          <ListRow
            key={entry.deletionId}
            isContentStart={index === 0}
            title={entry.page.title}
            meta={[
              ...metaOf(entry, now),
              ...(outcome?.status === 'failed' ? [{ key: 'error', content: <span className={styles.rowError} role="alert">{outcome.message}</span> }] : []),
            ]}
            trailing={<RowActions space={space} entry={entry} index={index} isPending={isPending} restore={restore} purge={purge} />}
          />
        );
      })}
    </List>
  );
}

/** 「載入更多」：載入中用 aria-disabled 而不是 disabled，有焦點的按鈕一停用焦點就掉回頁面本體。 */
function LoadMore({ trash }: { readonly trash: ReturnType<typeof useTrashQuery> }) {
  if (!trash.hasNextPage && !trash.isFetchNextPageError) return null;
  const isFetching = trash.isFetchingNextPage;
  return (
    <div className={styles.loadMore}>
      {trash.hasNextPage && (
        <Button
          size="sm"
          aria-disabled={isFetching || undefined}
          onClick={() => {
            if (!isFetching) void trash.fetchNextPage();
          }}
        >
          {isFetching ? '載入中…' : '載入更多'}
        </Button>
      )}
      {trash.isFetchNextPageError && <span role="alert">載入更多失敗，請再試一次。</span>}
    </div>
  );
}

function useTrashQuery(spaceKey: string) {
  return useInfiniteQuery({
    queryKey: queryKeys.knowledgeTrash(spaceKey),
    queryFn: ({ pageParam, signal }) => listTrash(spaceKey, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  });
}

/** 容量（只有空間的管理者）與「清掉 30 天以上的…」；滿了就說明，結果寫在下面。 */
function TrashToolbar({ capacity, purge }: { readonly capacity: KnowledgeTrashCapacity; readonly purge: TrashPurge }) {
  const isFull = capacity.batches >= capacity.maxBatches;
  return (
    <div className={styles.trashBar}>
      <p className={styles.trashCapacity}>
        <span>{`垃圾桶 ${capacity.batches.toLocaleString('en-US')}／${capacity.maxBatches.toLocaleString('en-US')} 批`}</span>
        {isFull && <span>{'。已經滿了，先清掉或永久刪除不要的，才能再刪頁面。'}</span>}
      </p>
      <Button
        size="sm"
        onClick={() => {
          purge.open({ deletionId: null, title: null, pageCount: null });
        }}
      >
        清掉 30 天以上的…
      </Button>
    </div>
  );
}

function PurgeResult({ result }: { readonly result: PurgeOutcome | null }) {
  if (result === null) return null;
  const { purged, isSingleBatch } = result;
  if (purged.purgedBatches === 0) {
    // 伺服器回 200 但什麼都沒刪：不能用成功的樣式，使用者會以為刪掉了。
    return isSingleBatch ? (
      <Notice tone="warning" role="status">
        這一批現在不能永久刪除：它底下還有留著的頁面（還在用的，或垃圾桶裡別的批）。垃圾桶裡的先永久刪除或復原，再試一次。什麼都沒有刪。
      </Notice>
    ) : (
      <Notice tone="info" role="status">
        沒有刪除超過 30 天、而且可以清掉的批。
      </Notice>
    );
  }
  const hasMore = !isSingleBatch && purged.hasMoreExpired;
  return (
    <Notice tone="success" role="status">
      {`已永久刪除 ${String(purged.purgedBatches)} 批、${String(purged.purgedPages)} 頁、${String(purged.purgedAttachments)} 個附件。${hasMore ? '還有更多，可以再清一次。' : ''}`}
    </Notice>
  );
}

function TrashContent({ space }: { readonly space: KnowledgeSpace }) {
  const trash = useTrashQuery(space.key);
  const purge = useTrashPurge(space.key);
  const restoreState = useRestore(space.key);
  if (trash.isPending) {
    return <Skeleton label="正在載入垃圾桶…" rows={SKELETON_ROWS} />;
  }
  if (trash.isLoadingError) {
    return (
      <ErrorState
        error={trash.error}
        onRetry={() => {
          void trash.refetch();
        }}
      />
    );
  }
  const notice = <RefetchErrorNotice query={trash} message="無法更新垃圾桶，畫面上可能是舊的。" />;
  // 最後一批復原之後清單重抓成空的，已復原的那一列還是要留著。
  const entries = withRestoredRows(trash.data.pages.flatMap((page) => page.items), restoreState.outcomes);
  const capacity = trash.data.pages[0]?.capacity ?? null;
  return (
    <>
      {notice}
      {capacity !== null && <TrashToolbar capacity={capacity} purge={purge} />}
      <PurgeResult result={purge.result} />
      {entries.length === 0 ? (
        <EmptyState title="垃圾桶是空的">刪除的頁面會連同底下的頁面放在這裡，可以從這裡復原。</EmptyState>
      ) : (
        <TrashList space={space} entries={entries} restoreState={restoreState} purge={purge} footer={<LoadMore trash={trash} />} />
      )}
      <TrashPurgeDialog purge={purge} />
    </>
  );
}

/** 垃圾桶（/knowledge/:spaceKey/trash）：每一批刪除一列；看得到空間的人都看得到，復原要 canEdit。 */
export function KnowledgeTrashPage() {
  return (
    <SpaceGate title="垃圾桶" loadingLabel="正在載入垃圾桶…">
      {(space, notice) => (
        <PageLayout
          variant="list"
          bar={
            <PageBar
              title="垃圾桶"
              documentTitle={`垃圾桶 · ${space.name}`}
              crumbs={[
                { label: '知識庫', to: KNOWLEDGE_HOME },
                { label: space.name, to: knowledgeSpaceUrl(space.key) },
              ]}
            />
          }
        >
          {notice}
          <TrashContent key={space.key} space={space} />
        </PageLayout>
      )}
    </SpaceGate>
  );
}
