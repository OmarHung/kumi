import { Navigate, useLocation, useParams } from 'react-router';
import { isApiError } from '../../../api/http';
import { ErrorState, LoadingState, RefetchErrorNotice } from '../../../components/StatusViews/StatusViews';
import { StatusPage } from '../../../components/StatusPage/StatusPage';
import { NotFoundPage } from '../../errors/NotFoundPage';
import { KNOWLEDGE_HOME } from '../knowledgeLinks';
import { SpaceShell } from './SpaceShell';
import { useSpaceTree } from './spaceQueries';

const HTTP_NOT_FOUND = 404;

/**
 * 空間（/knowledge/:spaceKey）與空間裡的一頁（/knowledge/:spaceKey/:pageNumber）：同一個路由，左邊的頁面樹換頁時不重新掛上。
 * 網址上的代號不分大小寫，讀到之後換成空間自己的代號（樹、快取、連結才用同一份）。
 */
export function KnowledgeSpacePage() {
  const { spaceKey = '', pageNumber } = useParams();
  const location = useLocation();
  const query = useSpaceTree(spaceKey);
  if (query.isPending) {
    return (
      <StatusPage title="知識庫">
        <LoadingState label="正在載入空間…" />
      </StatusPage>
    );
  }
  if (query.isLoadingError) {
    if (isApiError(query.error) && query.error.status === HTTP_NOT_FOUND) {
      return (
        <NotFoundPage
          title="找不到這個空間"
          message={`沒有代號是 ${spaceKey} 的空間，或是你沒有權限看它。`}
          backTo={KNOWLEDGE_HOME}
          backLabel="回到知識庫"
        />
      );
    }
    return (
      <StatusPage title="知識庫">
        <ErrorState
          error={query.error}
          title="空間載入失敗"
          onRetry={() => {
            void query.refetch();
          }}
        />
      </StatusPage>
    );
  }
  const state: unknown = location.state;
  const canonicalKey = query.data.space.key;
  if (canonicalKey !== spaceKey) {
    return <Navigate replace to={location.pathname.replace(encodeURIComponent(spaceKey), encodeURIComponent(canonicalKey))} state={state} />;
  }
  return (
    <SpaceShell
      key={spaceKey}
      tree={query.data}
      pageSegment={pageNumber}
      notice={<RefetchErrorNotice query={query} message="無法更新頁面的最新資料，畫面上可能是舊的。" />}
    />
  );
}
