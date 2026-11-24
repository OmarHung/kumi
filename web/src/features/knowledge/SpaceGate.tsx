import type { ReactNode } from 'react';
import { useParams } from 'react-router';
import { isApiError } from '../../api/http';
import type { KnowledgeSpace } from '../../api/knowledgeTypes';
import { LoadingState, ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { NotFoundPage } from '../errors/NotFoundPage';
import { KNOWLEDGE_HOME } from './knowledgeLinks';
import { useKnowledgeSpace } from './useKnowledgeSpaces';

const HTTP_NOT_FOUND = 404;

interface SpaceGateProps {
  /** 載入中與讀不到時頁列上的分頁標題。 */
  readonly title: string;
  readonly loadingLabel: string;
  /** 讀好之後畫的內容；notice 是背景重抓失敗的提示，放在內容最上面。 */
  readonly children: (space: KnowledgeSpace, notice: ReactNode) => ReactNode;
}

/** 網址上的空間：載入、找不到（或看不到，兩者一樣是 404）、讀不到可以重試；讀好才交給內容。垃圾桶與設定頁共用。 */
export function SpaceGate({ title, loadingLabel, children }: SpaceGateProps) {
  const { spaceKey = '' } = useParams();
  const space = useKnowledgeSpace(spaceKey);
  if (space.isPending) {
    return (
      <StatusPage title={title}>
        <LoadingState label={loadingLabel} />
      </StatusPage>
    );
  }
  if (space.isLoadingError) {
    if (isApiError(space.error) && space.error.status === HTTP_NOT_FOUND) {
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
      <StatusPage title={title}>
        <ErrorState
          error={space.error}
          title="空間載入失敗"
          onRetry={() => {
            void space.refetch();
          }}
        />
      </StatusPage>
    );
  }
  const notice = <RefetchErrorNotice query={space} message="無法更新這個空間的最新資料，畫面上可能是舊的。" />;
  return children(space.data, notice);
}
