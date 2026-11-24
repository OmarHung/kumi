import { Navigate, useLocation, useParams } from 'react-router';
import { isApiError } from '../../api/http';
import { ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { NotFoundPage } from '../errors/NotFoundPage';
import { ProjectLoadError } from '../projects/ProjectLoadError';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { projectPageLink } from '../projects/projectViewPreference';
import { itemPagePath } from '../work-items/itemLinks';
import { HTTP_FORBIDDEN, HTTP_NOT_FOUND, ITEM_FORBIDDEN, ITEM_NOT_FOUND, useItemContent } from './ItemContent';
import { ItemDetailView } from './ItemDetailView';

interface ItemLoadErrorProps {
  readonly projectKey: string;
  readonly itemKey: string;
  readonly error: unknown;
  readonly onRetry: () => void;
}

function ItemLoadError({ projectKey, itemKey, error, onRetry }: ItemLoadErrorProps) {
  const status = isApiError(error) ? error.status : 0;
  const back = { backTo: projectPageLink(projectKey), backLabel: '回到專案' };
  if (status === HTTP_NOT_FOUND) {
    return <NotFoundPage title={ITEM_NOT_FOUND.title} message={ITEM_NOT_FOUND.message(itemKey)} {...back} />;
  }
  if (status === HTTP_FORBIDDEN) {
    return <NotFoundPage code="403" title={ITEM_FORBIDDEN.title} message={ITEM_FORBIDDEN.message} {...back} />;
  }
  return (
    <StatusPage title={itemKey}>
      <ErrorState error={error} title="項目載入失敗" onRetry={onRetry} />
    </StatusPage>
  );
}

/**
 * 項目頁的外殼：同時讀項目與專案（藍圖、成員、權限），處理找不到、沒有權限與失敗（ItemContent.tsx，和項目面板共用）。
 * 只有第一次就讀不到（還沒有資料）才換成錯誤畫面；已經有資料時重抓失敗（例如切回分頁時伺服器暫時 503），
 * 留著畫面與寫到一半的說明、留言，只多一行提示。
 */
export function ItemPage() {
  const { projectKey = '', itemKey = '' } = useParams();
  const content = useItemContent(itemKey, projectKey);
  // 換到正規網址時帶著連進來的 state（刪除之後要回到的專案頁、回去時焦點放回哪一筆）。
  const entry: unknown = useLocation().state;

  switch (content.status) {
    case 'itemError':
      return <ItemLoadError projectKey={projectKey} itemKey={itemKey} error={content.error} onRetry={content.retry} />;
    // 網址的專案打錯了也找得到項目（編號是全組織唯一的），換到它真正所在的專案，藍圖才對得上；
    // 編號的寫法不同（smk-7、SMK-07）也換成正規的，修改寫進的快取才是這一頁讀的那一份。
    case 'misplaced':
      return <Navigate to={itemPagePath(content.item.projectKey, content.item.key)} replace state={entry} />;
    case 'projectError':
      return <ProjectLoadError projectKey={projectKey} error={content.error} onRetry={content.retry} />;
    case 'loading':
      return (
        <StatusPage title={itemKey}>
          <LoadingState label="正在載入項目…" />
        </StatusPage>
      );
    case 'ready':
      // 以編號為 key：從一個項目連到另一個（上層）時，編輯中的狀態與留言草稿都重新開始。
      // 提示放在內容最上面固定的位置，出現或消失都不會讓 ItemDetailView 重新掛載。
      return <ItemDetailView key={itemKey} detail={content.detail} item={content.item} notices={content.notices} />;
  }
}
