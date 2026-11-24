import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { errorMessage } from '../../../api/http';
import { listTrash, restorePage } from '../../../api/knowledge';
import type { KnowledgePage, KnowledgeSpace, KnowledgeTrashEntry } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';
import { Button } from '../../../components/Button/Button';
import { List, ListRow, type ListMeta } from '../../../components/List/List';
import { PageBar } from '../../../components/PageBar/PageBar';
import { PageLayout } from '../../../components/PageLayout/PageLayout';
import { Skeleton } from '../../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState, RefetchErrorNotice } from '../../../components/StatusViews/StatusViews';
import { formatRelativeTime } from '../../../domain/time';
import { KNOWLEDGE_HOME, knowledgePageUrl, knowledgeSpaceUrl } from '../knowledgeLinks';
import { SpaceGate } from '../SpaceGate';
import styles from '../Knowledge.module.css';

const SKELETON_ROWS = 3;

/** 每一批的復原：成功之後那一列換成「已復原」的說明（不重抓清單，列不消失、清單不跳動）。 */
type RestoreOutcome = { readonly status: 'restored'; readonly page: KnowledgePage } | { readonly status: 'failed'; readonly message: string };

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
    mutationFn: (entry: KnowledgeTrashEntry) => restorePage(spaceKey, entry.page.number),
    onMutate: (entry) => {
      setPending(entry.deletionId, true);
      record(entry.deletionId, null);
    },
    onSuccess: (page, entry) => {
      record(entry.deletionId, { status: 'restored', page });
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeTree(spaceKey) });
    },
    onError: (error, entry) => {
      record(entry.deletionId, { status: 'failed', message: errorMessage(error) });
    },
    onSettled: (_page, _error, entry) => {
      setPending(entry.deletionId, false);
    },
  });
  return { outcomes, pendingIds, restore: (entry: KnowledgeTrashEntry) => { mutation.mutate(entry); } };
}

function metaOf(entry: KnowledgeTrashEntry, now: number): readonly ListMeta[] {
  return [
    ...(entry.pageCount > 1 ? [{ key: 'count', content: `連同底下 ${String(entry.pageCount - 1)} 頁` }] : []),
    ...(entry.formerParent === null ? [] : [{ key: 'parent', content: `原本在「${entry.formerParent.title}」底下` }]),
    { key: 'deleted', content: `${entry.deletedBy.displayName} · ${formatRelativeTime(entry.deletedAt, now)}刪除` },
  ];
}

interface TrashListProps {
  readonly space: KnowledgeSpace;
  readonly entries: readonly KnowledgeTrashEntry[];
}

function TrashList({ space, entries }: TrashListProps) {
  const { outcomes, pendingIds, restore } = useRestore(space.key);
  const [now] = useState(() => Date.now());
  return (
    <List label="垃圾桶">
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
            trailing={
              space.myPermissions.canEdit ? (
                <Button
                  size="sm"
                  disabled={isPending}
                  aria-label={`復原「${entry.page.title}」`}
                  onClick={() => {
                    restore(entry);
                  }}
                >
                  {isPending ? '復原中…' : '復原'}
                </Button>
              ) : undefined
            }
          />
        );
      })}
    </List>
  );
}

function TrashContent({ space }: { readonly space: KnowledgeSpace }) {
  const trash = useQuery({
    queryKey: queryKeys.knowledgeTrash(space.key),
    queryFn: ({ signal }) => listTrash(space.key, signal),
  });
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
  if (trash.data.length === 0) {
    return (
      <>
        {notice}
        <EmptyState title="垃圾桶是空的">刪除的頁面會連同底下的頁面放在這裡，可以從這裡復原。</EmptyState>
      </>
    );
  }
  return (
    <>
      {notice}
      <TrashList space={space} entries={trash.data} />
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
