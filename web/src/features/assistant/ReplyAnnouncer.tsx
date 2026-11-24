import type { AssistantMessage } from '../../api/aiTypes';
import type { ReplyPhase } from './replyPhase';
import { replyPhaseText } from './replyPhase';

const PROPOSE_TOOL = 'propose_changeset';
const DEFAULT_FAILURE = '回覆失敗了';

function changeSetNumberOf(message: AssistantMessage): number | null {
  for (const part of message.parts) {
    if (part.type === 'tool' && part.name === PROPOSE_TOOL && part.status === 'done' && part.changeSetNumber !== null) return part.changeSetNumber;
  }
  return null;
}

/** 這一則現在要對報讀軟體說的一句。狀態還沒存好（停止、斷線之後等伺服器的版本）時不說，存好了才說結果。 */
function announcementFor(message: AssistantMessage | undefined, isStreaming: boolean, phase: ReplyPhase): string {
  if (message === undefined) return '';
  switch (message.status) {
    case 'complete': {
      const number = changeSetNumberOf(message);
      return number === null ? '助理回覆完成' : `助理回覆完成。已提出變更集 #${String(number)}`;
    }
    case 'stopped':
      return '回覆已停止';
    case 'failed':
      return `回覆失敗：${message.error ?? DEFAULT_FAILURE}`;
    default:
      if (!isStreaming) return '';
      // 主機來源：排隊中與執行中各說一句（位置變了才換句）；其他來源沒有進度，照舊「助理正在回覆」。
      return phase === null ? '助理正在回覆' : replyPhaseText(phase).replace('…', '');
  }
}

interface ReplyAnnouncerProps {
  readonly messages: readonly AssistantMessage[] | undefined;
  /** 這一頁最近收的那一則（結束後還在）；打開舊的對話沒有。 */
  readonly watchedId: string | null;
  /** 正在長的那一則（沒有在收是 null）。 */
  readonly activeMessageId: string | null;
  /** 主機來源的進度（排隊中、執行中）。 */
  readonly phase?: ReplyPhase;
}

/**
 * 給報讀軟體的回覆狀態：一個一直掛著的 polite 區域（先在才聽得到之後填進來的字），只在狀態改變時換一句，
 * 文字一段一段長出來不會唸。只管這一頁自己收過的那一則，打開舊的對話不說話。
 */
export function ReplyAnnouncer({ messages, watchedId, activeMessageId, phase = null }: ReplyAnnouncerProps) {
  const message = watchedId === null ? undefined : messages?.find((candidate) => candidate.id === watchedId);
  return (
    <div className="visually-hidden" aria-live="polite" aria-atomic="true">
      {announcementFor(message, activeMessageId !== null && activeMessageId === watchedId, phase)}
    </div>
  );
}
