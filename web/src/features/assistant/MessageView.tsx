import { Link } from 'react-router';
import type { AiOptions, AssistantMessage, AssistantToolPart } from '../../api/aiTypes';
import { buttonClassName } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { Markdown } from '../../components/Markdown/Markdown';
import { Notice } from '../../components/Notice/Notice';
import { effortLabel, toolLabel } from '../ai/aiLabels';
import { changeSetPagePath, itemPagePath } from '../work-items/itemLinks';
import { ProposalCard } from './ProposalCard';
import { replyPhaseText, type ReplyPhase } from './replyPhase';
import { findModel } from './selection';
import styles from './Assistant.module.css';

// 助理的回覆沒有提及，也沒有附件可以對。
const NO_MENTIONS: ReadonlyMap<string, string> = new Map();

const PROPOSE_TOOL = 'propose_changeset';
const PROPOSE_ITEM_TOOL = 'propose_item_changes';
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

function PartView({ part, projectKey }: { readonly part: AssistantMessage['parts'][number]; readonly projectKey: string }) {
  if (part.type === 'text') {
    return part.text === '' ? null : <Markdown text={part.text} mentionNames={NO_MENTIONS} hasAttachments={false} />;
  }
  return (
    <>
      <ToolRow tool={part} />
      {part.name === PROPOSE_TOOL && part.status === 'done' && part.changeSetNumber !== null && (
        <ChangeSetCard projectKey={projectKey} number={part.changeSetNumber} />
      )}
      {part.name === PROPOSE_ITEM_TOOL && part.status === 'done' && part.itemProposalId !== null && (
        <ProposalCard proposalId={part.itemProposalId} />
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
  readonly projectKey: string;
  readonly options: AiOptions;
  /** 這一則正在長出來。 */
  readonly isStreaming: boolean;
  /** 這一則在主機來源的進度（排隊中、執行中）；沒有是 null。 */
  readonly phase?: ReplyPhase;
}

export function MessageView({ message, projectKey, options, isStreaming, phase = null }: MessageViewProps) {
  if (message.role === 'user') {
    return (
      <div className={styles.userMessage}>
        {message.itemKey !== null && (
          <Link to={itemPagePath(projectKey, message.itemKey)} className={styles.aboutItem}>
            {`關於 ${message.itemKey}`}
          </Link>
        )}
        {message.parts.map((part) => (part.type === 'text' ? part.text : '')).join('')}
      </div>
    );
  }
  const footer = isStreaming ? null : footerText(message, options);
  const hasRunningTool = message.parts.some((part) => part.type === 'tool' && part.status === 'running');
  return (
    <div className={styles.assistantMessage}>
      {message.parts.map((part, index) => (
        <PartView key={part.type === 'tool' ? part.callId : `text-${String(index)}`} part={part} projectKey={projectKey} />
      ))}
      {isStreaming && !hasRunningTool && (
        <div className={styles.toolRow}>
          <span className={styles.spinner} aria-hidden="true" />
          <span>{replyPhaseText(phase)}</span>
        </div>
      )}
      {message.status === 'failed' && <Notice tone="warning">{message.error ?? DEFAULT_FAILURE}</Notice>}
      {footer !== null && <div className={styles.messageFooter}>{footer}</div>}
    </div>
  );
}
