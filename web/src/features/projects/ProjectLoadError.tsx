import { isApiError } from '../../api/http';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { NotFoundPage } from '../errors/NotFoundPage';
import { StatusPage } from '../../components/StatusPage/StatusPage';

const HTTP_NOT_FOUND = 404;

interface ProjectLoadErrorProps {
  readonly projectKey: string;
  readonly error: unknown;
  readonly onRetry: () => void;
}

/** 專案讀不到：找不到（或看不到）時說明並回到專案清單，其他錯誤可以重試。專案頁與項目頁共用。 */
export function ProjectLoadError({ projectKey, error, onRetry }: ProjectLoadErrorProps) {
  if (isApiError(error) && error.status === HTTP_NOT_FOUND) {
    return (
      <NotFoundPage
        title="找不到這個專案"
        message={`沒有代號是 ${projectKey} 的專案，或是你沒有權限看它。`}
        backTo="/projects"
        backLabel="回到專案清單"
      />
    );
  }
  return (
    <StatusPage title={projectKey}>
      <ErrorState error={error} title="專案載入失敗" onRetry={onRetry} />
    </StatusPage>
  );
}
