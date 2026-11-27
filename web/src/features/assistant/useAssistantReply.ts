import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { useNavigate } from 'react-router';
import type { AiSelection, AssistantConversation, AssistantStreamEvent } from '../../api/aiTypes';
import { getConversation, invalidateConversationLists, streamAssistantReply, useCreateConversation, type AssistantScope, type StreamOutcome } from '../../api/assistant';
import { errorMessage } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import { assistantPagePath } from '../work-items/itemLinks';
import type { ReplyPhase } from './replyPhase';
import { applyStreamEvent, mergeStored } from './streamCache';
import { refreshAfterWithdraw, withdrewProposal } from './withdrawRefresh';

/** 正在收的那一則：哪個對話、哪一則助理的訊息（收到 start 才知道）、主機來源的排隊進度。 */
export interface ActiveReply {
  readonly conversationId: string;
  readonly messageId: string | null;
  readonly phase: ReplyPhase;
}

export interface SendRequest {
  readonly text: string;
  readonly selection: AiSelection;
  /** 針對哪個項目；沒選是 null。 */
  readonly itemKey: string | null;
  /** 已經上傳好的附件編號，依順序；沒有是空陣列。 */
  readonly attachmentIds: readonly string[];
  /** 伺服器收下了（串流開始）。輸入框在這時才清掉字：送出之前失敗的，字要留著。 */
  readonly onAccepted: () => void;
}

export interface AssistantReply {
  readonly active: ActiveReply | null;
  /** 送出之前的失敗（沒有對話可開、409、422、429）的說明。 */
  readonly error: string | null;
  /** 送出到串流結束的整段（含開對話）：這段時間不能再送。 */
  readonly isSending: boolean;
  /** 最近收的那一則助理訊息（收到 start 就記，結束後留著）：報讀的狀態說明用它找那一則。 */
  readonly lastMessageId: string | null;
  /** 這一次送出剛開的對話（網址換過去之前、之後都是它）：輸入框的草稿跟著過去。 */
  readonly createdConversationId: string | null;
  readonly clearError: () => void;
  readonly send: (request: SendRequest) => Promise<void>;
  readonly stop: () => void;
}

/** 失敗說明綁在對話上：換到別的對話時上一個的說明不跟著（用比對而不是 effect 清掉）。 */
interface ScopedError {
  readonly conversationId: string | undefined;
  readonly message: string;
}

interface StreamContext {
  readonly queryClient: QueryClient;
  readonly projectKey: AssistantScope;
  readonly setActive: (next: ActiveReply | null) => void;
  readonly onStarted: (messageId: string) => void;
  /** 之後又開始了這個對話的下一輪：這一輪留下的重讀不能整份取代快取。 */
  readonly isSuperseded: () => boolean;
  /** 現在正在長的那一則助理訊息（重讀時不能被伺服器的版本蓋掉）。 */
  readonly currentMessageId: () => string | null;
}

/** 串流的事件寫進快取，結束時整理快取。回傳 false 表示串流開始之前就失敗了（呼叫端處理錯誤）。 */
async function runStream(context: StreamContext, id: string, request: SendRequest, signal: AbortSignal): Promise<void> {
  const { queryClient, projectKey, setActive, onStarted } = context;
  // 只讓清單失效（exact）：正在長的那一則對話不重抓，不然會被伺服器那一份蓋掉。
  const invalidateList = () => invalidateConversationLists(queryClient, projectKey);
  let messageId: string | null = null;
  let phase: ReplyPhase = null;
  const patch = (event: AssistantStreamEvent) => {
    queryClient.setQueryData<AssistantConversation>(queryKeys.assistantConversation(projectKey, id), (current) =>
      current === undefined ? current : applyStreamEvent(current, event, messageId),
    );
  };
  const outcome: StreamOutcome = await streamAssistantReply(
    projectKey,
    id,
    { text: request.text, selection: request.selection, itemKey: request.itemKey, attachmentIds: request.attachmentIds },
    signal,
    (event) => {
      if (event.type === 'start') {
        messageId = event.assistantMessageId;
        // 開始：清單裡這一則要標「回覆中」。
        void invalidateList();
        onStarted(messageId);
        setActive({ conversationId: id, messageId, phase });
        request.onAccepted();
      }
      // 排隊與開始執行：只更新畫面上的進度（queued 可能在 start 之前或之後到，所以帶著目前知道的 messageId）。
      if (event.type === 'queued' || event.type === 'started') {
        phase = event.type === 'queued' ? { type: 'queued', position: event.position } : { type: 'started' };
        setActive({ conversationId: id, messageId, phase });
      }
      patch(event);
      if (withdrewProposal(event)) void refreshAfterWithdraw(queryClient);
    },
  );
  setActive(null);
  void invalidateList();
  // 收到 done 的那一則已經是伺服器存下來的；其他情況（斷線、停止）以重抓為準（不等它：等的時候還能送下一則）。
  // 重抓完再讓清單失效一次：伺服器這時才清掉「回覆中」，上面那次讀到的清單還是舊的。
  if (outcome !== 'done') void reloadStored(context, id, messageId).finally(invalidateList);
}

const RELOAD_ATTEMPTS = 4;
const RELOAD_DELAY_MS = 500;

/**
 * 停止或斷線之後讀伺服器存下來的對話。伺服器收到中斷才把那則回覆存成「已停止」，立刻讀可能還沒有：
 * 這時畫面上已經長出來的文字先留著，等伺服器存好（有那一則）再換成它；試了幾次還是沒有，就以伺服器的為準。
 * 這段時間使用者可能已經送出下一則：那時不整份取代（會洗掉下一則與它正在長的回覆），只把已經存好的訊息換成伺服器的版本，
 * 也不 invalidate（重抓一樣會蓋掉）。
 */
async function reloadStored(context: StreamContext, id: string, messageId: string | null): Promise<void> {
  const { queryClient, projectKey, isSuperseded, currentMessageId } = context;
  const key = queryKeys.assistantConversation(projectKey, id);
  const apply = (stored: AssistantConversation) => {
    if (!isSuperseded()) {
      queryClient.setQueryData(key, stored);
      return;
    }
    queryClient.setQueryData<AssistantConversation>(key, (current) =>
      current === undefined ? stored : mergeStored(current, stored, currentMessageId()),
    );
  };
  for (let attempt = 0; messageId !== null && attempt < RELOAD_ATTEMPTS; attempt += 1) {
    try {
      const stored = await getConversation(projectKey, id);
      if (stored.messages.some((message) => message.id === messageId)) {
        apply(stored);
        return;
      }
    } catch {
      // 讀不到就照下面的重抓處理（它會把失敗顯示出來）。
      break;
    }
    await new Promise((resolve) => setTimeout(resolve, RELOAD_DELAY_MS));
  }
  if (!isSuperseded()) await queryClient.invalidateQueries({ queryKey: key });
}

/** 現在在收哪一則。ref 給換網址的 effect 讀：它不該因為這個值變了就重跑（開新對話時網址晚一步才換過去）。 */
function useActiveReply() {
  const [active, setActiveState] = useState<ActiveReply | null>(null);
  const activeRef = useRef<ActiveReply | null>(null);
  const setActive = useCallback((next: ActiveReply | null) => {
    activeRef.current = next;
    setActiveState(next);
  }, []);
  return { active, activeRef, setActive };
}

/** 換到別的對話（或回到「新的對話」）、離開頁面：中斷還在收的那一則。開新對話時網址從沒有換成剛開的編號，不算換掉。 */
function useAbortWhenLeaving(
  conversationId: string | undefined,
  activeRef: RefObject<ActiveReply | null>,
  controllerRef: RefObject<AbortController | null>,
): void {
  useEffect(() => {
    if (activeRef.current !== null && activeRef.current.conversationId !== conversationId) controllerRef.current?.abort();
  }, [activeRef, controllerRef, conversationId]);
  useEffect(
    () => () => {
      controllerRef.current?.abort();
    },
    [controllerRef],
  );
}

/** 最近一輪是哪個對話的第幾輪：重讀（停止之後）靠它知道自己之後有沒有新的一輪。 */
interface RunMark {
  readonly conversationId: string;
  readonly token: number;
}

/** 開始收一則回覆：串流開始之前的失敗（什麼都沒送出）記成這個對話的說明，字留在輸入框。 */
function useRunReply(
  projectKey: AssistantScope,
  controllerRef: RefObject<AbortController | null>,
  activeRef: RefObject<ActiveReply | null>,
  setActive: (next: ActiveReply | null) => void,
  onStarted: (messageId: string) => void,
  setScopedError: (error: ScopedError) => void,
) {
  const queryClient = useQueryClient();
  const markRef = useRef<RunMark>({ conversationId: '', token: 0 });
  return useCallback(
    async (id: string, request: SendRequest) => {
      const controller = new AbortController();
      controllerRef.current = controller;
      const token = markRef.current.token + 1;
      markRef.current = { conversationId: id, token };
      setActive({ conversationId: id, messageId: null, phase: null });
      const context: StreamContext = {
        queryClient,
        projectKey,
        setActive,
        onStarted,
        isSuperseded: () => markRef.current.conversationId === id && markRef.current.token !== token,
        currentMessageId: () => activeRef.current?.messageId ?? null,
      };
      try {
        await runStream(context, id, request, controller.signal);
      } catch (failure) {
        setActive(null);
        // 失敗（含串流開始之前）：清單不能留著舊的「回覆中」。
        void invalidateConversationLists(queryClient, projectKey);
        // 送出被擋（主機來源不能用、選擇失效、排隊滿了）：能選的 AI 與 notice 可能已經變了，選單要重讀。
        void queryClient.invalidateQueries({ queryKey: queryKeys.aiOptions });
        setScopedError({ conversationId: id, message: errorMessage(failure) });
      } finally {
        if (controllerRef.current === controller) controllerRef.current = null;
      }
    },
    [activeRef, controllerRef, onStarted, projectKey, queryClient, setActive, setScopedError],
  );
}

interface SendState {
  readonly isSending: boolean;
  readonly createdId: string | null;
  readonly send: (request: SendRequest) => Promise<void>;
}

/**
 * 送出：沒有對話（在 /assistant）時先開一則、換網址再送。開對話要等一下、run() 才設 controller，
 * 防重送一進來就先佔住（ref），整個流程結束才放掉。
 */
function useSend(
  projectKey: AssistantScope,
  conversationId: string | undefined,
  run: (id: string, request: SendRequest) => Promise<void>,
  setScopedError: (error: ScopedError | null) => void,
  onCreated: ((id: string) => void) | undefined,
): SendState {
  const navigate = useNavigate();
  const create = useCreateConversation(projectKey);
  const sendingRef = useRef(false);
  const [isSending, setIsSending] = useState(false);
  const [createdId, setCreatedId] = useState<string | null>(null);
  const send = useCallback(
    async (request: SendRequest) => {
      if (sendingRef.current) return;
      sendingRef.current = true;
      setIsSending(true);
      try {
        setScopedError(null);
        let id = conversationId;
        if (id === undefined) {
          try {
            id = (await create.mutateAsync()).id;
          } catch (failure) {
            setScopedError({ conversationId: undefined, message: errorMessage(failure) });
            return;
          }
          // 先換過去再送：換了之後這一頁的對話編號才是這一則，回覆長出來的地方才看得到。
          // 整頁換網址；浮動的面板不動網址（頁面是使用者正在看的別的東西），由呼叫端記下新的對話編號。
          setCreatedId(id);
          if (onCreated === undefined) void navigate(assistantPagePath(projectKey, id), { replace: true });
          else onCreated(id);
        }
        await run(id, request);
      } finally {
        sendingRef.current = false;
        setIsSending(false);
      }
    },
    [conversationId, create, navigate, onCreated, projectKey, run, setScopedError],
  );
  return { isSending, createdId, send };
}

/**
 * 送一則訊息、把串流收進快取（internal/HANDOFF-ai-assistant.md 7.2）：沒有對話（在 /assistant）時先開一則、換網址再送；
 * 使用者按停止、換到別的對話或離開頁面就中斷。沒收到 done（中途斷線）或被停止時重抓這則對話：伺服器存下來的才算數。
 */
export function useAssistantReply(
  projectKey: AssistantScope,
  conversationId: string | undefined,
  /** 開了新的對話之後（網址以外的地方記對話編號時給，例如浮動的面板）；沒給就換整頁的網址。 */
  onCreated?: (id: string) => void,
): AssistantReply {
  const { active, activeRef, setActive } = useActiveReply();
  const [scopedError, setScopedError] = useState<ScopedError | null>(null);
  const controllerRef = useRef<AbortController | null>(null);

  const [lastMessageId, setLastMessageId] = useState<string | null>(null);
  const run = useRunReply(projectKey, controllerRef, activeRef, setActive, setLastMessageId, setScopedError);
  const { isSending, createdId, send } = useSend(projectKey, conversationId, run, setScopedError, onCreated);

  const stop = useCallback(() => {
    controllerRef.current?.abort();
  }, []);

  useAbortWhenLeaving(conversationId, activeRef, controllerRef);

  const error = scopedError !== null && scopedError.conversationId === conversationId ? scopedError.message : null;
  return {
    active,
    error,
    isSending,
    lastMessageId,
    createdConversationId: createdId,
    clearError: () => {
      setScopedError(null);
    },
    send,
    stop,
  };
}
