import type { AiSelection } from '../../../api/aiTypes';
import type { AssistantReply } from '../useAssistantReply';
import type { QuestionAnswering } from './QuestionCard';

function noop(): void {
  // 回答沒有輸入框的字要清。
}

/**
 * 問題卡片的作答：和輸入框送出同一條路（同一個專案、模型與項目），失敗的說明就是輸入框上方那一則。
 * 還沒選模型、回覆進行中時是 null（卡片唯讀）。
 */
export function questionAnswering(reply: AssistantReply, selection: AiSelection | null, itemKey: string | null, onBeforeSend: () => void): QuestionAnswering | null {
  if (selection === null || reply.active !== null) return null;
  return {
    isSending: reply.isSending,
    error: reply.error,
    onSubmit: (text) => {
      onBeforeSend();
      void reply.send({ text, selection, itemKey, attachmentIds: [], onAccepted: noop });
    },
  };
}
