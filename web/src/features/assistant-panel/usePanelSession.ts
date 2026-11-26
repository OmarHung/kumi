import { useCallback, useState } from 'react';
import { useAssistantReply, type AssistantReply } from '../assistant/useAssistantReply';
import { useAssistantItem } from '../assistant/useAssistantItem';
import type { PanelScope } from './panelMemory';
import { useAssistantProject } from './useAssistantProject';

interface ConversationState {
  readonly projectKey: PanelScope;
  readonly id: string | undefined;
}

export interface PanelSession {
  /** 面板現在對著哪個專案；null 是「不選專案」（組織的對話）；還沒有（不在專案頁、也沒選過）是 undefined。 */
  readonly projectKey: PanelScope;
  readonly chooseProject: (projectKey: string | null) => void;
  /** 助理針對的項目：在項目頁自動帶，也能自己換或取消；沒有是 null。 */
  readonly itemKey: string | null;
  readonly chooseItem: (itemKey: string | null) => void;
  /** 現在的對話；undefined 是「新的對話」。換了專案就回到「新的對話」。 */
  readonly conversationId: string | undefined;
  readonly selectConversation: (id: string | undefined) => void;
  readonly reply: AssistantReply;
  /** 回覆中（含送出到串流結束的整段）：不換專案、不換對話。 */
  readonly isBusy: boolean;
}

/**
 * 面板的狀態：專案、對話與回覆。放在 AppShell 層、關掉面板或換頁都不卸下，所以回覆不會被中斷。
 * 專案與回覆互相牽制（回覆中不換專案、回覆要知道專案），用「上一次算出來的忙碌」連起來：
 * 忙碌的狀態在渲染時調整，晚一個畫面對使用者不可見。
 */
export function usePanelSession(routeProjectKey: string | undefined, routeItemKey: string | null): PanelSession {
  const [wasBusy, setWasBusy] = useState(false);
  const [projectKey, chooseProject] = useAssistantProject(routeProjectKey, wasBusy);
  const [itemKey, chooseItem] = useAssistantItem(routeItemKey, projectKey ?? null, wasBusy);
  const [conversation, setConversation] = useState<ConversationState>({ projectKey: undefined, id: undefined });
  const conversationId = conversation.projectKey === projectKey ? conversation.id : undefined;

  // 浮動的面板開了新的對話不換網址：頁面是使用者正在看的別的東西。
  const handleCreated = useCallback(
    (id: string) => {
      setConversation({ projectKey, id });
    },
    [projectKey],
  );
  const reply = useAssistantReply(projectKey ?? null, conversationId, handleCreated);
  const isBusy = reply.active !== null || reply.isSending;
  if (isBusy !== wasBusy) setWasBusy(isBusy);

  return {
    projectKey,
    chooseProject,
    itemKey,
    chooseItem,
    conversationId,
    selectConversation: (id) => {
      setConversation({ projectKey, id });
    },
    reply,
    isBusy,
  };
}
