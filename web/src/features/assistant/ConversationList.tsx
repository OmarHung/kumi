import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import type { AssistantConversationSummary } from '../../api/aiTypes';
import { useConversations, useDeleteConversation, type AssistantScope } from '../../api/assistant';
import { errorMessage } from '../../api/http';
import { buttonClassName } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu } from '../../components/Menu/Menu';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { formatRelativeTime } from '../../domain/time';
import { assistantPagePath } from '../work-items/itemLinks';
import styles from './Assistant.module.css';

const NEW_CONVERSATION = '新的對話';
const SKELETON_ROWS = 4;

interface ConversationListProps {
  readonly projectKey: AssistantScope;
  readonly currentId: string | undefined;
  /** 選了一則或按了「新的對話」之後（窄螢幕的對話框要關上）。 */
  readonly onNavigate?: () => void;
}

interface RowProps extends ConversationListProps {
  readonly conversation: AssistantConversationSummary;
  readonly now: number;
  readonly onDelete: (conversation: AssistantConversationSummary) => void;
}

function titleOf(conversation: AssistantConversationSummary): string {
  return conversation.title ?? NEW_CONVERSATION;
}

function ConversationRow({ projectKey, currentId, conversation, now, onDelete, onNavigate }: RowProps) {
  const title = titleOf(conversation);
  return (
    <li className={styles.conversationRow}>
      <Link
        to={assistantPagePath(projectKey, conversation.id)}
        className={styles.conversationLink}
        aria-current={conversation.id === currentId ? 'true' : undefined}
        onClick={onNavigate}
      >
        <span className={styles.conversationTitle}>{title}</span>
        <span className={styles.conversationMeta}>
          {conversation.isRunning && <span className={styles.running}>回覆中</span>}
          <time dateTime={conversation.updatedAt}>{formatRelativeTime(conversation.updatedAt, now)}</time>
        </span>
      </Link>
      <Menu
        menuLabel={`「${title}」的動作`}
        align="end"
        items={[
          {
            key: 'delete',
            label: '刪除',
            icon: 'delete',
            tone: 'danger',
            onSelect: () => {
              onDelete(conversation);
            },
          },
        ]}
        renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={`「${title}」的更多動作`} size="sm" />}
      />
    </li>
  );
}

function DeleteDialog({
  projectKey,
  conversation,
  isCurrent,
  onClose,
}: {
  readonly projectKey: AssistantScope;
  readonly conversation: AssistantConversationSummary;
  readonly isCurrent: boolean;
  readonly onClose: () => void;
}) {
  const remove = useDeleteConversation(projectKey);
  const navigate = useNavigate();
  return (
    <ConfirmDialog
      title="刪除這則對話？"
      description={`「${titleOf(conversation)}」會永久刪除，不能復原。`}
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={remove.isPending}
      error={remove.isError ? errorMessage(remove.error) : null}
      onConfirm={() => {
        remove.mutate(conversation.id, {
          onSuccess: () => {
            onClose();
            if (isCurrent)
              void navigate(assistantPagePath(projectKey), {
                replace: true,
              });
          },
        });
      }}
      onCancel={onClose}
    />
  );
}

interface ItemsProps extends ConversationListProps {
  readonly conversations: ReturnType<typeof useConversations>;
  readonly now: number;
  readonly onDelete: (conversation: AssistantConversationSummary) => void;
}

function ConversationItems({ projectKey, currentId, conversations, now, onDelete, onNavigate }: ItemsProps) {
  if (conversations.isPending) return <Skeleton label="正在載入對話…" rows={SKELETON_ROWS} />;
  if (conversations.isLoadingError) {
    return (
      <ErrorState
        error={conversations.error}
        title="對話載入失敗"
        onRetry={() => {
          void conversations.refetch();
        }}
      />
    );
  }
  if (conversations.data.length === 0) return <p className={styles.listMessage}>還沒有對話。</p>;
  return (
    <ul className={styles.conversationList} aria-label="對話">
      {conversations.data.map((conversation) => (
        <ConversationRow
          key={conversation.id}
          projectKey={projectKey}
          currentId={currentId}
          conversation={conversation}
          now={now}
          onDelete={onDelete}
          onNavigate={onNavigate}
        />
      ))}
    </ul>
  );
}

/** 對話清單：最上面「新的對話」；每列是標題與相對時間（回覆中有小字），「更多」裡有刪除（要確認）。 */
export function ConversationList({ projectKey, currentId, onNavigate }: ConversationListProps) {
  const conversations = useConversations(projectKey);
  const [now] = useState(() => Date.now());
  const [pendingDelete, setPendingDelete] = useState<AssistantConversationSummary | null>(null);
  return (
    <>
      <Link to={assistantPagePath(projectKey)} className={buttonClassName('secondary')} onClick={onNavigate}>
        <Icon name="add" />
        {NEW_CONVERSATION}
      </Link>
      <ConversationItems
        projectKey={projectKey}
        currentId={currentId}
        conversations={conversations}
        now={now}
        onDelete={setPendingDelete}
        onNavigate={onNavigate}
      />
      {pendingDelete !== null && (
        <DeleteDialog
          projectKey={projectKey}
          conversation={pendingDelete}
          isCurrent={pendingDelete.id === currentId}
          onClose={() => {
            setPendingDelete(null);
          }}
        />
      )}
    </>
  );
}
