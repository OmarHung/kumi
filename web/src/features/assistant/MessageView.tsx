import { plainMentions } from './composer/userMentionText';
import { Link } from 'react-router';
import type { AiOptions, AssistantMessage, AssistantToolPart } from '../../api/aiTypes';
import type { AssistantScope } from '../../api/assistant';
import { buttonClassName } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { Markdown } from '../../components/Markdown/Markdown';
import { Notice } from '../../components/Notice/Notice';
import { effortLabel, toolLabel } from '../ai/aiLabels';
import { changeSetPagePath, itemPagePath } from '../work-items/itemLinks';
import { CopyMessageButton } from './CopyMessageButton';
import { MessageAttachments } from './MessageAttachments';
import { ItemBatchProposalCard } from './ItemBatchProposalCard';
import { ItemDeletionProposalCard } from './ItemDeletionProposalCard';
import { MemberProposalCard } from './MemberProposalCard';
import { NewItemsProposalCard } from './NewItemsProposalCard';
import { ProjectProposalCard } from './ProjectProposalCard';
import { ProposalCard } from './ProposalCard';
import { QuestionCard, type QuestionAnswering } from './questions/QuestionCard';
import { parseQuestionBlock } from './questions/questionBlock';
import { replyPhaseText, type ReplyPhase } from './replyPhase';
import { findModel } from './selection';
import styles from './Assistant.module.css';

// 助理的回覆沒有提及，也沒有附件可以對。
const NO_MENTIONS: ReadonlyMap<string, string> = new Map();

const PROPOSE_TOOL = 'propose_changeset';
const PROPOSE_ITEM_TOOL = 'propose_item_changes';
const PROPOSE_PROJECT_TOOL = 'propose_project';
const PROPOSE_NEW_ITEMS_TOOL = 'propose_new_items';
const PROPOSE_MEMBERS_TOOL = 'propose_member_changes';
const PROPOSE_DELETIONS_TOOL = 'propose_item_deletions';
const PROPOSE_BATCH_TOOL = 'propose_bulk_item_changes';
const DEFAULT_FAILURE = '回覆失敗了，請再試一次。';

function ToolRow({ tool }: { readonly tool: AssistantToolPart }) {
  const label = toolLabel(tool.name);
  if (tool.status === 'running') {
    return (
      <div className={styles.toolRow} data-status="running" role="status">
        <span className={styles.spinner} aria-hidden="true" />
        <span>{`${label}…中`}</span>
      </div>
    );
  }
  const isFailed = tool.status === 'failed';
  return (
    <div className={styles.toolRow} data-status={tool.status}>
      <Icon name={isFailed ? 'error' : 'check'} className={styles.toolIcon} />
      <span>
        {label}
        {isFailed && tool.error !== null && <span className={styles.toolError}>{`：${tool.error}`}</span>}
      </span>
    </div>
  );
}

function ChangeSetCard({ projectKey, number }: { readonly projectKey: string; readonly number: number }) {
  return (
    <div className={styles.changeSetCard}>
      <span className={styles.changeSetTitle}>{`變更集 #${String(number)} 等待核准`}</span>
      <Link to={changeSetPagePath(projectKey, number)} className={buttonClassName('secondary', 'sm')}>
        查看變更集
      </Link>
    </div>
  );
}

function AssistantText({ text, answer }: { readonly text: string; readonly answer: QuestionAnswering | undefined }) {
  const { before, questions, after } = parseQuestionBlock(text);
  const markdown = (body: string) => <Markdown text={body} mentionNames={NO_MENTIONS} hasAttachments={false} linkHosts="always" />;
  // 讀不出問題區塊（含串流到一半）時整段照一般的文字顯示。
  if (questions === null) return markdown(text);
  return (
    <>
      {before.trim() !== '' && markdown(before)}
      <QuestionCard questions={questions} answering={answer} />
      {after.trim() !== '' && markdown(after)}
    </>
  );
}

interface PartViewProps {
  readonly part: AssistantMessage['parts'][number];
  readonly projectKey: AssistantScope;
  readonly answer: QuestionAnswering | undefined;
}

function PartView({ part, projectKey, answer }: PartViewProps) {
  if (part.type === 'text') {
    return part.text === '' ? null : <AssistantText text={part.text} answer={answer} />;
  }
  return (
    <>
      <ToolRow tool={part} />
      {projectKey !== null && part.name === PROPOSE_TOOL && part.status === 'done' && part.changeSetNumber !== null && (
        <ChangeSetCard projectKey={projectKey} number={part.changeSetNumber} />
      )}
      {part.name === PROPOSE_ITEM_TOOL && part.status === 'done' && part.itemProposalId !== null && (
        <ProposalCard proposalId={part.itemProposalId} />
      )}
      {part.name === PROPOSE_PROJECT_TOOL && part.status === 'done' && part.projectProposalId !== null && (
        <ProjectProposalCard proposalId={part.projectProposalId} />
      )}
      {part.name === PROPOSE_NEW_ITEMS_TOOL && part.status === 'done' && part.newItemProposalId !== null && (
        <NewItemsProposalCard proposalId={part.newItemProposalId} />
      )}
      {part.name === PROPOSE_MEMBERS_TOOL && part.status === 'done' && part.memberProposalId !== null && (
        <MemberProposalCard proposalId={part.memberProposalId} />
      )}
      {part.name === PROPOSE_DELETIONS_TOOL && part.status === 'done' && part.itemDeletionProposalId !== null && (
        <ItemDeletionProposalCard proposalId={part.itemDeletionProposalId} />
      )}
      {part.name === PROPOSE_BATCH_TOOL && part.status === 'done' && part.itemBatchProposalId !== null && (
        <ItemBatchProposalCard proposalId={part.itemBatchProposalId} />
      )}
    </>
  );
}

/** 回覆下面一行小字：「Claude Opus 5.5 · 高」，兩個以上的連線時前面加連線名稱；停止的加「已停止」。 */
function footerText(message: AssistantMessage, options: AiOptions): string | null {
  const parts: string[] = [];
  const { selection } = message;
  if (selection !== null) {
    if (options.connections.length >= 2 && message.connectionName !== null) parts.push(message.connectionName);
    parts.push(findModel(options, selection)?.displayName ?? selection.model);
    if (selection.effort !== null) parts.push(effortLabel(selection.effort));
  }
  if (message.status === 'stopped') parts.push('已停止');
  return parts.length === 0 ? null : parts.join(' · ');
}

interface MessageViewProps {
  readonly message: AssistantMessage;
  /** null 是不選專案的對話（不選項目、沒有變更集；附件照常有）。 */
  readonly projectKey: AssistantScope;
  readonly options: AiOptions;
  /** 這一則正在長出來。 */
  readonly isStreaming: boolean;
  /** 這一則在主機來源的進度（排隊中、執行中）；沒有是 null。 */
  readonly phase?: ReplyPhase;
  /** 這一則的問題卡片能作答時才有（最新一則、已結束、後面沒有使用者的訊息）；沒有就是唯讀。 */
  readonly answer?: QuestionAnswering;
}

/** 複製用的文字：使用者打的字、助理回覆的文字段落（多段用空行接起來，不含工具列與卡片）。 */
function copyTextOf(message: AssistantMessage): string {
  const texts = message.parts.flatMap((part) => (part.type === 'text' && part.text !== '' ? [part.text] : []));
  return message.role === 'user' ? texts.join('') : texts.join('\n\n');
}

function UserMessage({ message, projectKey }: { readonly message: AssistantMessage; readonly projectKey: AssistantScope }) {
  // 提及的語法是給助理看的：使用者看到與複製的都是 @名稱。
  const text = plainMentions(copyTextOf(message));
  return (
    <div className={styles.userBlock}>
      <MessageAttachments attachments={message.attachments} projectKey={projectKey} />
      {(text !== '' || message.itemKey !== null) && (
        <div className={styles.userMessage}>
          {projectKey !== null && message.itemKey !== null && (
            <Link to={itemPagePath(projectKey, message.itemKey)} className={styles.aboutItem}>
              {`關於 ${message.itemKey}`}
            </Link>
          )}
          {text}
        </div>
      )}
      {text !== '' && (
        <div className={styles.userActions}>
          <CopyMessageButton text={text} />
        </div>
      )}
    </div>
  );
}

export function MessageView({ message, projectKey, options, isStreaming, phase = null, answer }: MessageViewProps) {
  if (message.role === 'user') return <UserMessage message={message} projectKey={projectKey} />;
  const footer = isStreaming ? null : footerText(message, options);
  const copyText = isStreaming ? '' : copyTextOf(message);
  const hasRunningTool = message.parts.some((part) => part.type === 'tool' && part.status === 'running');
  return (
    <div className={styles.assistantMessage}>
      {message.parts.map((part, index) => (
        <PartView key={part.type === 'tool' ? part.callId : `text-${String(index)}`} part={part} projectKey={projectKey} answer={isStreaming ? undefined : answer} />
      ))}
      {isStreaming && !hasRunningTool && (
        <div className={styles.toolRow}>
          <span className={styles.spinner} aria-hidden="true" />
          <span>{replyPhaseText(phase)}</span>
        </div>
      )}
      {message.status === 'failed' && <Notice tone="warning">{message.error ?? DEFAULT_FAILURE}</Notice>}
      {(footer !== null || copyText !== '') && (
        <div className={styles.messageFooter}>
          <span className={styles.footerText}>{footer}</span>
          {copyText !== '' && <CopyMessageButton text={copyText} />}
        </div>
      )}
    </div>
  );
}
