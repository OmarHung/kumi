import { useEffect } from 'react';
import { Link } from 'react-router';
import type { AiOptions } from '../../api/aiTypes';
import { useProject } from '../../api/queries';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { ConversationView } from '../assistant/ConversationView';
import { useSelection } from '../assistant/useSelection';
import { storeProject } from './panelMemory';
import type { PanelSession } from './usePanelSession';
import styles from './AssistantPanel.module.css';

export const CHOOSE_PROJECT_TEXT = '先在上面選一個專案，助理會幫你調整它的設定；或選「不選專案」，問模板、規劃，請助理幫你開新專案。';

interface PanelContentProps {
  readonly session: PanelSession;
  readonly options: AiOptions;
}

/** 組織沒設定 AI、但可以接自己的 AI 帳號：說明去哪裡加。 */
function AddPersonalHint() {
  return (
    <div className={styles.message}>
      <p>組織還沒設定 AI。你可以在「我的 AI 助理」接自己的 AI 帳號。</p>
      <Link to="/account/ai">前往「我的 AI 助理」</Link>
    </div>
  );
}

/** 不選專案（組織的對話）：問模板、規劃、請助理開新專案；不看任何專案的權限（後端只看是不是組織的成員）。 */
function OrganizationConversation({ session, options }: PanelContentProps) {
  const [selection, handleSelection] = useSelection(options);
  useEffect(() => {
    storeProject(null);
  }, []);
  return (
    <ConversationView
      variant="panel"
      projectKey={null}
      conversationId={session.conversationId}
      options={options}
      selection={selection}
      onSelectionChange={handleSelection}
      reply={session.reply}
      itemKey={null}
    />
  );
}

function ProjectConversation({ projectKey, session, options }: PanelContentProps & { readonly projectKey: string }) {
  const project = useProject(projectKey);
  const [selection, handleSelection] = useSelection(options);
  // 讀得到才記：路由上不存在的代號、沒有權限的專案不留在瀏覽器裡。
  const isReadable = project.data !== undefined;
  useEffect(() => {
    if (isReadable) storeProject(projectKey);
  }, [isReadable, projectKey]);
  if (project.data !== undefined && !project.data.myPermissions.includes('blueprint.read')) {
    return <p className={styles.message}>你沒有讀取這個專案設定的權限，不能使用 AI 助理。</p>;
  }
  if (project.isLoadingError) {
    return (
      <ErrorState
        error={project.error}
        title="專案載入失敗"
        onRetry={() => {
          void project.refetch();
        }}
      />
    );
  }
  // 專案還在讀時就先畫對話：換專案時輸入框的草稿與位置不被洗掉，真正的權限檢查在後端。
  return (
    <ConversationView
      variant="panel"
      projectKey={projectKey}
      conversationId={session.conversationId}
      options={options}
      selection={selection}
      onSelectionChange={handleSelection}
      reply={session.reply}
      itemKey={session.itemKey}
    />
  );
}

/** 面板的主體：沒有能用的模型、還沒選專案、專案讀不到或沒有權限時說明原因，否則就是和整頁版同一套的對話（不選專案時是組織的對話）。 */
export function PanelContent({ session, options }: PanelContentProps) {
  if (!options.available) return <AddPersonalHint />;
  if (session.projectKey === undefined) return <p className={styles.message}>{CHOOSE_PROJECT_TEXT}</p>;
  if (session.projectKey === null) return <OrganizationConversation session={session} options={options} />;
  return <ProjectConversation projectKey={session.projectKey} session={session} options={options} />;
}
