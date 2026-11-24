import { useState } from 'react';
import { useProjectSearchQuery } from '../../api/queries';
import type { ProjectSearchHit } from '../../api/types';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState } from '../../components/StatusViews/StatusViews';
import { ProjectGroup } from './ProjectGroup';
import styles from './ProjectListPage.module.css';

/** 伺服器允許的最大筆數：專案清單頁是找完整的結果，不像快速跳轉只找一個。 */
export const PROJECT_SEARCH_PAGE_LIMIT = 20;

const SKELETON_ROWS = 4;

/**
 * 結果數的說明（只給報讀軟體，aria-live="polite"）：新的結果回來才換，等的時候維持上一句，
 * 不在舊結果還顯示時唸「找不到」。不用 role="status"：頁面上全域的 status 只有一個（ArrivalNotice）。
 * 搜尋字是停下來 300ms 才進網址的（SearchField），所以這裡不必再等一次。
 */
function useSettledMessage(message: string | null): string {
  const [shown, setShown] = useState('');
  if (message !== null && message !== shown) {
    setShown(message);
  }
  return message ?? shown;
}

function Hits({ hits, isBusy }: { readonly hits: readonly ProjectSearchHit[]; readonly isBusy: boolean }) {
  const projects = hits.map((hit) => hit.project);
  return (
    <div className={styles.results} aria-busy={isBusy}>
      {hits.length === 0 ? (
        <EmptyState title="找不到符合的專案。" />
      ) : (
        <ProjectGroup
          projects={projects}
          isFirstGroup
          label="符合的專案"
          hasArchivedDate={projects.some((project) => project.archivedAt !== null)}
          snippets={new Map(hits.map((hit) => [hit.project.key, hit.snippet]))}
        />
      )}
      {hits.length >= PROJECT_SEARCH_PAGE_LIMIT && (
        <p className={styles.limitNote}>{`只列出前 ${String(PROJECT_SEARCH_PAGE_LIMIT)} 個，可能還有更多；輸入更多字可以縮小範圍。`}</p>
      )}
    </div>
  );
}

/**
 * 專案清單頁的搜尋結果（GET /v1/search/projects）：代號、名稱、說明都找，含封存的專案；
 * 順序照伺服器的（沒封存的在前）。說明比對到的專案多一欄摘錄，代號或名稱就比對到的那一格是空的。
 * 改字時先留著舊的結果（變淡、aria-busy），只有第一次沒有舊結果才是骨架。
 */
export function ProjectSearchResults({ text }: { readonly text: string }) {
  const search = useProjectSearchQuery(text, PROJECT_SEARCH_PAGE_LIMIT, { keepsPreviousResult: true });
  const count = search.data?.length ?? 0;
  const isCurrent = search.isSuccess && !search.isPlaceholderData;
  const message = useSettledMessage(isCurrent ? (count > 0 ? `找到 ${String(count)} 個專案` : '找不到符合的專案') : null);
  return (
    <>
      <p className="visually-hidden" aria-live="polite">
        {message}
      </p>
      {search.isPending && <Skeleton label="正在搜尋專案…" rows={SKELETON_ROWS} />}
      {search.isError && (
        <ErrorState
          error={search.error}
          title="搜尋失敗"
          onRetry={() => {
            void search.refetch();
          }}
        />
      )}
      {search.isSuccess && <Hits hits={search.data} isBusy={search.isPlaceholderData} />}
    </>
  );
}
