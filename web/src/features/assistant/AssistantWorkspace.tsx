import { useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useParams, useSearchParams } from 'react-router';
import type { AssistantScope } from '../../api/assistant';
import type { AiOptions } from '../../api/aiTypes';
import type { ProjectDetail } from '../../api/types';
import { IconButton } from '../../components/IconButton/IconButton';
import { Dialog } from '../../components/Dialog/Dialog';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { useUnsavedChangesGuard } from '../../lib/useUnsavedChangesGuard';
import { useIsCompact } from '../../lib/useMediaQuery';
import { panelItemOf } from '../work-items/itemLinks';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { ConversationList } from './ConversationList';
import { ItemPicker } from './ItemPicker';
import { ConversationView } from './ConversationView';
import { ListResizer } from './ListResizer';
import { useListWidth, type ListWidth } from './listWidth';
import { useAssistantItem } from './useAssistantItem';
import { useSelection } from './useSelection';
import { useAssistantReply } from './useAssistantReply';
import styles from './Assistant.module.css';

const LIST_ID = 'assistant-conversations';
const LEAVE_DESCRIPTION = '離開會停止助理的回覆。';
const LEAVE_LABELS = {
  title: '回覆還沒完成',
  confirmLabel: '停止並離開',
  cancelLabel: '留下來',
  tone: 'primary',
} as const;

interface AssistantWorkspaceProps {
  /** 專案；null 是不選專案的頁（/assistant）：沒有麵包屑與項目選擇。 */
  readonly detail: ProjectDetail | null;
  readonly options: AiOptions;
}

/** 窄螢幕的對話清單：打開一個對話框列出，選了或按「新的對話」就關上。 */
function ConversationDialog({
  projectKey,
  currentId,
  onClose,
}: {
  readonly projectKey: AssistantScope;
  readonly currentId: string | undefined;
  readonly onClose: () => void;
}) {
  return (
    <Dialog title="對話" onClose={onClose}>
      <ConversationList projectKey={projectKey} currentId={currentId} onNavigate={onClose} />
    </Dialog>
  );
}

/** 寬螢幕左邊的對話清單，加上和對話之間可以拖的分隔線（調列表的寬度）。 */
function ListColumn({ projectKey, currentId, width }: { readonly projectKey: AssistantScope; readonly currentId: string | undefined; readonly width: ListWidth }) {
  return (
    <>
      <aside id={LIST_ID} className={styles.sidebar} aria-label="對話清單">
        <ConversationList projectKey={projectKey} currentId={currentId} />
      </aside>
      <ListResizer api={width} controls={LIST_ID} />
    </>
  );
}

interface PageActionsProps {
  readonly projectKey: AssistantScope;
  readonly itemKey: string | null;
  readonly onChooseItem: (itemKey: string | null) => void;
  readonly isCompact: boolean;
  readonly onOpenList: () => void;
}

/** 頁列右邊：選項目；窄螢幕多一顆「對話」（打開對話清單）。 */
function PageActions({ projectKey, itemKey, onChooseItem, isCompact, onOpenList }: PageActionsProps) {
  return (
    <>
      {projectKey !== null && <ItemPicker projectKey={projectKey} itemKey={itemKey} onChange={onChooseItem} size="compact" />}
      {isCompact && (
        // 只留圖示：麵包屑在 320px 寬時才有地方放。
        <IconButton icon="bulletList" label="對話" variant="secondary" onClick={onOpenList} />
      )}
    </>
  );
}

/** 頁列：專案的頁有專案名稱的麵包屑（連到專案頁，hook 只在有專案時才掛）；不選專案的頁沒有。 */
function WorkspaceBar({ detail, actions }: { readonly detail: ProjectDetail | null; readonly actions: ReactNode }) {
  if (detail === null) return <PageBar title="AI 助理" actions={actions} />;
  return <ProjectWorkspaceBar detail={detail} actions={actions} />;
}

function ProjectWorkspaceBar({ detail, actions }: { readonly detail: ProjectDetail; readonly actions: ReactNode }) {
  const to = useProjectPageLink(detail.project.key);
  return <PageBar title="AI 助理" crumbs={[{ label: detail.project.name, to }]} actions={actions} />;
}

/**
 * 頁列、左邊的對話清單與右邊的對話。對話編號在網址裡（沒有是「新的對話」）；
 * 開新對話時網址從沒有換成剛開的編號，同一個路由、這個元件不會重新掛上，進行中的串流不受影響。
 * 窄螢幕（48rem 以下）清單收成頁列上的一顆「對話」，打開一個對話框列出。
 */
export function AssistantWorkspace({ detail, options }: AssistantWorkspaceProps) {
  const projectKey = detail?.project.key ?? null;
  const { conversationId } = useParams();
  const isCompact = useIsCompact();
  const [isListOpen, setIsListOpen] = useState(false);

  const workspaceRef = useRef<HTMLDivElement>(null);
  const listWidth = useListWidth(workspaceRef);
  const [selection, handleSelection] = useSelection(options);

  const reply = useAssistantReply(projectKey, conversationId);
  const guard = useUnsavedChangesGuard(reply.active !== null, LEAVE_DESCRIPTION, LEAVE_LABELS);

  // 整頁沒有「路由上的項目」（這一頁自己就是助理）：只在進來時讀一次網址的 ?item=（從面板開成整頁帶過來的），之後自己選。
  // 開新對話會換網址（拿掉 ?item=），所以不能一直跟著網址。
  const [params] = useSearchParams();
  const [initialItemKey] = useState(() => panelItemOf(params)?.toUpperCase() ?? null);
  const [itemKey, setItemKey] = useAssistantItem(initialItemKey, projectKey, reply.active !== null || reply.isSending);

  return (
    <PageLayout variant="workspace" bar={<WorkspaceBar detail={detail} actions={<PageActions projectKey={projectKey} itemKey={itemKey} onChooseItem={setItemKey} isCompact={isCompact} onOpenList={() => { setIsListOpen(true); }} />} />}>
      <div
        ref={workspaceRef}
        className={styles.workspace}
        data-compact={isCompact}
        // 寬度由使用者拖，執行時才算得出來：只設一個 CSS 變數（docs/DESIGN.md 第 13 節），排版交給 CSS。
        style={isCompact ? undefined : ({ '--assistant-list-width': `${String(listWidth.width)}px` } as CSSProperties)}
      >
        {!isCompact && <ListColumn projectKey={projectKey} currentId={conversationId} width={listWidth} />}
        <ConversationView
          projectKey={projectKey}
          conversationId={conversationId}
          options={options}
          selection={selection}
          onSelectionChange={handleSelection}
          reply={reply}
          itemKey={itemKey}
        />
      </div>
      {isCompact && isListOpen && (
        <ConversationDialog
          projectKey={projectKey}
          currentId={conversationId}
          onClose={() => {
            setIsListOpen(false);
          }}
        />
      )}
      {guard.dialog}
    </PageLayout>
  );
}
