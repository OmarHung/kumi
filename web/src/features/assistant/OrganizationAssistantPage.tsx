import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { useAiOptions } from '../../api/assistant';
import { buttonClassName } from '../../components/Button/Button';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import { EmptyState, ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { AssistantWorkspace } from './AssistantWorkspace';

const TITLE = 'AI 助理';

function Reading({ children }: { readonly children: ReactNode }) {
  return (
    <PageLayout variant="reading" bar={<PageBar title={TITLE} />}>
      {children}
    </PageLayout>
  );
}

/**
 * 不選專案的 AI 助理（/assistant 與 /assistant/:conversationId）：問模板、規劃，請助理幫忙開新專案。
 * 只看組織有沒有開放可以用的 AI；不看任何專案的權限（後端只看是不是組織的成員）。
 */
export function OrganizationAssistantPage() {
  const options = useAiOptions();
  if (options.isPending) {
    return (
      <StatusPage title={TITLE}>
        <LoadingState label="正在載入 AI 助理…" />
      </StatusPage>
    );
  }
  if (options.isLoadingError) {
    return (
      <Reading>
        <ErrorState
          error={options.error}
          title="AI 助理載入失敗"
          onRetry={() => {
            void options.refetch();
          }}
        />
      </Reading>
    );
  }
  if (!options.data.available) {
    const back = (
      <Link to="/projects" className={buttonClassName('secondary')}>
        回到專案清單
      </Link>
    );
    return (
      <Reading>
        <EmptyState title="AI 助理還不能用" action={back}>
          組織還沒開放可以用的 AI。請組織管理員到「AI 設定」開放組織的 API 金鑰，或允許成員使用自己的 AI 帳號；也可以請組織擁有者設定主機 AI 代理。
        </EmptyState>
      </Reading>
    );
  }
  return <AssistantWorkspace detail={null} options={options.data} />;
}
