import { useRef, useState } from 'react';
import type { AiOptions, AiSelection } from '../../api/aiTypes';
import { useConversation } from '../../api/assistant';
import { isApiError } from '../../api/http';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { Composer } from './Composer';
import { EmptyIntro } from './EmptyIntro';
import { MessageView } from './MessageView';
import { ReplyAnnouncer } from './ReplyAnnouncer';
import type { ReplyPhase } from './replyPhase';
import type { AssistantReply } from './useAssistantReply';
import { useStickToBottom } from './useStickToBottom';
import styles from './Assistant.module.css';

/** 例子按下之後填進輸入框的字；nonce 讓同一句連按兩次也算一次新的填入。 */
interface FillState {
  readonly text: string;
  readonly nonce: number;
}

const HTTP_NOT_FOUND = 404;
const SKELETON_ROWS = 3;

interface ConversationViewProps {
  readonly projectKey: string;
  readonly conversationId: string | undefined;
  readonly options: AiOptions;
  readonly selection: AiSelection | null;
  readonly onSelectionChange: (selection: AiSelection) => void;
  readonly reply: AssistantReply;
  /** 針對哪個項目；沒選是 null。 */
  readonly itemKey: string | null;
  /** 整頁（預設）或浮動的面板：面板窄，說明的留白縮小。 */
  readonly variant?: 'page' | 'panel';
}

interface BodyProps {
  readonly projectKey: string;
  readonly conversationId: string | undefined;
  readonly conversation: ReturnType<typeof useConversation>;
  readonly options: AiOptions;
  /** 正在長出來的那一則助理訊息（沒有在收就是 null）。 */
  readonly activeMessageId: string | null;
  /** 正在長的那一則的進度（主機來源的排隊、執行中）。 */
  readonly phase: ReplyPhase;
  readonly onPick: (text: string) => void;
  readonly hasItem: boolean;
}

function ConversationBody({ projectKey, conversationId, conversation, options, activeMessageId, phase, onPick, hasItem }: BodyProps) {
  const messages = conversation.data?.messages;
  if (conversationId !== undefined && conversation.isPending) return <Skeleton label="正在載入對話…" rows={SKELETON_ROWS} />;
  if (conversation.isLoadingError) {
    const isMissing = isApiError(conversation.error) && conversation.error.status === HTTP_NOT_FOUND;
    return (
      <ErrorState
        error={conversation.error}
        title={isMissing ? '找不到這則對話' : '對話載入失敗'}
        onRetry={
          isMissing
            ? undefined
            : () => {
                void conversation.refetch();
              }
        }
      />
    );
  }
  if (messages === undefined || messages.length === 0) return <EmptyIntro onPick={onPick} hasItem={hasItem} />;
  return (
    <div className={styles.thread} data-content-start>
      {messages.map((message) => (
        <MessageView
          key={message.id}
          message={message}
          projectKey={projectKey}
          options={options}
          isStreaming={activeMessageId === message.id}
          phase={activeMessageId === message.id ? phase : null}
        />
      ))}
    </div>
  );
}

/** 右邊的對話：訊息（自己捲，人在最下面時跟著長）與最下面的輸入框。沒有對話或還沒有訊息時是說明與例子。 */
export function ConversationView({ projectKey, conversationId, options, selection, onSelectionChange, reply, itemKey, variant = 'page' }: ConversationViewProps) {
  const isStreamingHere = reply.active !== null && reply.active.conversationId === conversationId;
  const conversation = useConversation(projectKey, conversationId, isStreamingHere);
  const { scrollerRef, onScroll, pin } = useStickToBottom(conversation.data?.messages, conversationId ?? '');
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const [fill, setFill] = useState<FillState | null>(null);

  return (
    <section className={styles.conversation} aria-label="對話" data-variant={variant}>
      <div ref={scrollerRef} className={styles.scroller} onScroll={onScroll}>
        <RefetchErrorNotice query={conversation} message="無法更新這則對話，畫面上可能是舊的。" />
        <ConversationBody
          projectKey={projectKey}
          conversationId={conversationId}
          conversation={conversation}
          options={options}
          activeMessageId={isStreamingHere ? reply.active.messageId : null}
          phase={isStreamingHere ? reply.active.phase : null}
          hasItem={itemKey !== null}
          onPick={(text) => {
            setFill({ text, nonce: (fill?.nonce ?? 0) + 1 });
            inputRef.current?.focus();
          }}
        />
      </div>
      <ReplyAnnouncer messages={conversation.data?.messages} watchedId={reply.lastMessageId} activeMessageId={isStreamingHere ? reply.active.messageId : null} phase={isStreamingHere ? reply.active.phase : null} />
      <Composer
        options={options}
        selection={selection}
        onSelectionChange={onSelectionChange}
        isStreaming={reply.active !== null}
        isSending={reply.isSending}
        createdConversationId={reply.createdConversationId}
        isLoading={conversationId !== undefined && conversation.isPending}
        conversationId={conversationId}
        error={reply.error}
        onDismissError={reply.clearError}
        fill={fill}
        inputRef={inputRef}
        hasItem={itemKey !== null}
        onSend={(text, chosen, onAccepted) => {
          pin();
          void reply.send({ text, selection: chosen, itemKey, onAccepted });
        }}
        onStop={reply.stop}
      />
    </section>
  );
}
