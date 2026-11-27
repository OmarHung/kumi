import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import type {
  AiOptions,
  AssistantConversation,
  AssistantConversationListing,
  AssistantConversationSummary,
  AssistantMessageInput,
  AssistantStreamEvent,
  ItemProposal,
  NewItemProposal,
  ProjectProposal,
} from './aiTypes';
import { createSseParser, toStreamEvent } from './assistantStream';
import { ApiError, apiRequest, CSRF_HEADER, networkError, unwrapResponse } from './http';
import { listItems } from './items';
import { listCycles, listMilestones } from './planning';
import type { Item } from './types';
import { pollWhileApplying } from './proposalDecision';
import { queryKeys } from './queryKeys';

/**
 * 助理的對話屬於哪裡：專案代號，或 null＝不選專案（組織的對話，internal/HANDOFF-org-assistant.md）。
 * 兩邊的端點、形狀一樣，只是路徑與快取的鍵分開。
 */
export type AssistantScope = string | null;

const conversationsPath = (projectKey: AssistantScope) =>
  projectKey === null ? '/v1/assistant/conversations' : `/v1/projects/${encodeURIComponent(projectKey)}/assistant/conversations`;
const conversationPath = (projectKey: AssistantScope, conversationId: string) =>
  `${conversationsPath(projectKey)}/${encodeURIComponent(conversationId)}`;

/** 成員能選的 AI；available 是 false 時沒有 AI 助理的入口。 */
export function getAiOptions(signal?: AbortSignal): Promise<AiOptions> {
  return apiRequest<AiOptions>('/v1/ai/options', { signal });
}

export function useAiOptions() {
  return useQuery({
    queryKey: queryKeys.aiOptions,
    queryFn: ({ signal }) => getAiOptions(signal),
  });
}

export function listConversations(projectKey: AssistantScope, signal?: AbortSignal): Promise<readonly AssistantConversationSummary[]> {
  return apiRequest<readonly AssistantConversationSummary[]>(conversationsPath(projectKey), { signal });
}

/** 403 ai.assistant_forbidden；422 ai.unavailable（組織沒開 AI）。 */
export function createConversation(projectKey: AssistantScope): Promise<AssistantConversationSummary> {
  return apiRequest<AssistantConversationSummary>(conversationsPath(projectKey), { method: 'POST', body: {} });
}

export function getConversation(projectKey: AssistantScope, conversationId: string, signal?: AbortSignal): Promise<AssistantConversation> {
  return apiRequest<AssistantConversation>(conversationPath(projectKey, conversationId), { signal });
}

/** 409 ai.conversation_busy：正在回覆的對話不能刪。 */
export function deleteConversation(projectKey: AssistantScope, conversationId: string): Promise<null> {
  return apiRequest<null>(conversationPath(projectKey, conversationId), {
    method: 'DELETE',
  });
}

/** 自己所有的對話（各專案與不選專案的），新的在前；看不到的專案的對話不列。 */
export function listAllConversations(signal?: AbortSignal): Promise<readonly AssistantConversationListing[]> {
  return apiRequest<readonly AssistantConversationListing[]>('/v1/assistant/all-conversations', { signal });
}

/**
 * 一邊的對話清單變了（開新的、刪掉、送出、回覆結束）：那一邊的清單與跨專案的清單一起失效。
 * exact：對話的快取鍵以清單的鍵開頭，不加的話正在長的那一則對話也被重抓、蓋掉串流寫進去的內容。
 */
export function invalidateConversationLists(queryClient: QueryClient, projectKey: AssistantScope): Promise<void> {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.assistantConversations(projectKey), exact: true }),
    queryClient.invalidateQueries({ queryKey: queryKeys.allAssistantConversations, exact: true }),
  ]).then(() => undefined);
}

const RUNNING_POLL_MS = 2000;

/**
 * 對話清單。有哪一則「回覆中」就每兩秒讀一次，直到都存完：串流收到 done 的當下，伺服器還沒放掉那一則的租約，
 * 這時重讀的清單仍說它在回覆（標題後面一直是「回覆中」），輪詢是這個時間差的後備。
 */
export function useConversations(projectKey: AssistantScope) {
  return useQuery({
    queryKey: queryKeys.assistantConversations(projectKey),
    queryFn: ({ signal }) => listConversations(projectKey, signal),
    refetchInterval: (query) => (query.state.data?.some((conversation) => conversation.isRunning) === true ? RUNNING_POLL_MS : false),
  });
}

/** 跨專案的對話清單；和單一專案的清單一樣，有「回覆中」的就輪詢到存完。 */
export function useAllConversations() {
  return useQuery({
    queryKey: queryKeys.allAssistantConversations,
    queryFn: ({ signal }) => listAllConversations(signal),
    refetchInterval: (query) => (query.state.data?.some((conversation) => conversation.isRunning) === true ? RUNNING_POLL_MS : false),
  });
}

/**
 * 一則對話。自己這一頁正在收串流時（isStreaming）不重抓也不輪詢：串流寫進快取的訊息還沒存完，
 * 重抓會用伺服器那一份蓋掉正在長出來的文字，視窗重新取得焦點也一樣。
 * 其他時候伺服器說還在回覆（重新整理、串流中途斷線、剛按了停止、別的分頁在回）就每兩秒讀一次，直到它存完。
 */
export function useConversation(projectKey: AssistantScope, conversationId: string | undefined, isStreaming = false) {
  return useQuery({
    queryKey: queryKeys.assistantConversation(projectKey, conversationId ?? ''),
    queryFn: ({ signal }) => getConversation(projectKey, conversationId ?? '', signal),
    enabled: conversationId !== undefined,
    refetchOnWindowFocus: !isStreaming,
    refetchInterval: (query) => (!isStreaming && query.state.data?.conversation.isRunning === true ? RUNNING_POLL_MS : false),
  });
}

export function useCreateConversation(projectKey: AssistantScope) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => createConversation(projectKey),
    onSuccess: (created) => {
      // 先放進快取：換網址之後不必等一次讀取，也不會讀到比串流還舊的內容。
      queryClient.setQueryData<AssistantConversation>(queryKeys.assistantConversation(projectKey, created.id), {
        conversation: created,
        messages: [],
      });
      // exact：不然剛放進去的對話（鍵以清單的鍵開頭）也被重抓。
      void invalidateConversationLists(queryClient, projectKey);
    },
  });
}

export function useDeleteConversation(projectKey: AssistantScope) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (conversationId: string) => deleteConversation(projectKey, conversationId),
    onSuccess: (_, conversationId) => {
      queryClient.removeQueries({
        queryKey: queryKeys.assistantConversation(projectKey, conversationId),
      });
      void invalidateConversationLists(queryClient, projectKey);
    },
  });
}

/** 串流怎麼結束的：done 收到了；incomplete 是連線中途斷了（沒收到 done，要重抓對話）；aborted 是使用者按了停止。 */
export type StreamOutcome = 'done' | 'incomplete' | 'aborted';

function isAbort(error: unknown, signal: AbortSignal): boolean {
  return signal.aborted || (error instanceof DOMException && error.name === 'AbortError');
}

async function openStream(path: string, input: AssistantMessageInput, signal: AbortSignal): Promise<Response> {
  try {
    return await fetch(path, {
      method: 'POST',
      credentials: 'same-origin',
      signal,
      headers: {
        Accept: 'text/event-stream',
        'Content-Type': 'application/json',
        [CSRF_HEADER]: '1',
      },
      body: JSON.stringify(input),
    });
  } catch (error) {
    if (isAbort(error, signal)) throw error;
    throw networkError();
  }
}

/** 伺服器每 15 秒送一次心跳；三倍的時間一個位元組都沒收到，就當連線已經斷了（代理或網路悄悄卡住）。 */
export const STREAM_IDLE_TIMEOUT_MS = 45_000;
const STREAM_INTERRUPTED = '回覆的連線中斷了，請再試一次。';

/** 回應不是串流、或還沒開始（沒收到 start）就結束：什麼都沒送出去，照送出之前的失敗處理（字留著）。 */
function streamInterrupted(): ApiError {
  return new ApiError({ status: 0, code: 'client.stream_interrupted', message: STREAM_INTERRUPTED });
}

/**
 * 讀到結束或中斷為止，每個事件交給 onEvent；回傳有沒有收到 done。
 * 超過 STREAM_IDLE_TIMEOUT_MS 沒有任何資料：自己中斷連線（idle），當成斷線（incomplete）；使用者按停止才是 aborted。
 */
async function readEvents(
  body: ReadableStream<Uint8Array>,
  signal: AbortSignal,
  idle: AbortController,
  onEvent: (event: AssistantStreamEvent) => void,
): Promise<StreamOutcome> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  const parser = createSseParser();
  let hasDone = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const arm = () => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      idle.abort();
    }, STREAM_IDLE_TIMEOUT_MS);
  };
  try {
    arm();
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      arm();
      for (const message of parser.push(decoder.decode(value, { stream: true }))) {
        const event = toStreamEvent(message);
        if (event === null) continue;
        hasDone = hasDone || event.type === 'done';
        onEvent(event);
      }
    }
  } catch {
    return signal.aborted ? 'aborted' : 'incomplete';
  } finally {
    clearTimeout(timer);
    reader.releaseLock();
  }
  return signal.aborted ? 'aborted' : hasDone ? 'done' : 'incomplete';
}

/**
 * 送一則訊息並讀回覆的串流（internal/HANDOFF-ai-assistant.md 6.2）。串流開始之前的失敗（409、422、429…）是一般的 JSON 錯誤，
 * 丟出 ApiError（照 http.ts 拆信封）；回應不是 text/event-stream、或沒收到 start 就結束也丟 ApiError。
 * 開始之後的事件一個一個交給 onEvent。signal 取消（停止）時回傳 aborted，不丟錯誤。
 */
export async function streamAssistantReply(
  projectKey: AssistantScope,
  conversationId: string,
  input: AssistantMessageInput,
  signal: AbortSignal,
  onEvent: (event: AssistantStreamEvent) => void,
): Promise<StreamOutcome> {
  // 自己的中斷（沒有資料太久）與使用者的停止分開：fetch 與讀取看 idle.signal，結果的判斷看 signal。
  const idle = new AbortController();
  const relay = () => {
    idle.abort();
  };
  if (signal.aborted) relay();
  signal.addEventListener('abort', relay);
  try {
    return await streamWith(projectKey, conversationId, input, signal, idle, onEvent);
  } finally {
    signal.removeEventListener('abort', relay);
  }
}

async function streamWith(
  projectKey: AssistantScope,
  conversationId: string,
  input: AssistantMessageInput,
  signal: AbortSignal,
  idle: AbortController,
  onEvent: (event: AssistantStreamEvent) => void,
): Promise<StreamOutcome> {
  let response: Response;
  try {
    response = await openStream(`${conversationPath(projectKey, conversationId)}/messages`, input, idle.signal);
  } catch (error) {
    if (isAbort(error, signal)) return 'aborted';
    throw error;
  }
  if (!response.ok) {
    unwrapResponse(response.status, await response.text());
    return 'incomplete';
  }
  const isStream = response.headers.get('Content-Type')?.includes('text/event-stream') === true;
  if (!isStream || response.body === null) throw streamInterrupted();
  const progress = { hasStarted: false };
  const outcome = await readEvents(response.body, signal, idle, (event) => {
    progress.hasStarted = progress.hasStarted || event.type === 'start';
    onEvent(event);
  });
  if (outcome === 'incomplete' && !progress.hasStarted) throw streamInterrupted();
  return outcome;
}

const proposalPath = (proposalId: string) => `/v1/ai/item-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.item_proposal_not_found：別人的、不存在的。 */
export function getItemProposal(proposalId: string, signal?: AbortSignal): Promise<ItemProposal> {
  return apiRequest<ItemProposal>(proposalPath(proposalId), { signal });
}

/** 409 ai.item_proposal_closed（已經處理過）；工作引擎的失敗原樣回來（409 item.description_conflict、403、422…）。 */
export function applyItemProposal(proposalId: string): Promise<ItemProposal> {
  return apiRequest<ItemProposal>(`${proposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.item_proposal_closed。 */
export function dismissItemProposal(proposalId: string): Promise<ItemProposal> {
  return apiRequest<ItemProposal>(`${proposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useItemProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.itemProposal(proposalId),
    queryFn: ({ signal }) => getItemProposal(proposalId, signal),
  });
}

const ITEM_CHOICE_LIMIT = 10;

/** 選項目的下拉：這個專案的項目，text 是編號或標題（空的時候是最前面的幾個）。 */
export function useAssistantItemChoices(projectKey: string, text: string) {
  return useQuery({
    queryKey: queryKeys.assistantItemChoices(projectKey, text),
    // 換字時先留著上一份結果（變成新的之前不閃成空的）。
    placeholderData: keepPreviousData,
    // 項目的標題常在別處改：每次打開下拉都重讀，不留著 30 秒的舊結果。
    staleTime: 0,
    queryFn: async ({ signal }): Promise<readonly Item[]> => {
      const page = await listItems(projectKey, { limit: ITEM_CHOICE_LIMIT, ...(text === '' ? {} : { q: text }) }, signal);
      return page.items;
    },
  });
}

/** 套用或作廢之後：換成伺服器回的新狀態；套用還要讓那個項目的各份快取失效（單一項目、活動、清單、我的工作）。 */
export function useDecideItemProposal(proposal: ItemProposal) {
  const queryClient = useQueryClient();
  const settle = (decided: ItemProposal) => {
    queryClient.setQueryData(queryKeys.itemProposal(proposal.id), decided);
  };
  const apply = useMutation({
    mutationFn: () => applyItemProposal(proposal.id),
    onSuccess: (decided) => {
      settle(decided);
      void queryClient.invalidateQueries({ queryKey: queryKeys.item(proposal.itemKey) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.items(proposal.projectKey) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.assistantItemChoiceLists(proposal.projectKey) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
    },
    onError: () => {
      // 已經處理過（別的分頁按了）等失敗：以伺服器現在的狀態為準。
      void queryClient.invalidateQueries({ queryKey: queryKeys.itemProposal(proposal.id) });
    },
  });
  const dismiss = useMutation({ mutationFn: () => dismissItemProposal(proposal.id), onSuccess: settle });
  return { apply, dismiss };
}

const projectProposalPath = (proposalId: string) => `/v1/ai/project-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.project_proposal_not_found：別人的、不存在的。 */
export function getProjectProposal(proposalId: string, signal?: AbortSignal): Promise<ProjectProposal> {
  return apiRequest<ProjectProposal>(projectProposalPath(proposalId), { signal });
}

/** 403（不是組織管理員）、409 project.key_taken、409 ai.project_proposal_closed、422 project.*：失敗時建議維持待處理。 */
export function applyProjectProposal(proposalId: string): Promise<ProjectProposal> {
  return apiRequest<ProjectProposal>(`${projectProposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.project_proposal_closed。 */
export function dismissProjectProposal(proposalId: string): Promise<ProjectProposal> {
  return apiRequest<ProjectProposal>(`${projectProposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useProjectProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.projectProposal(proposalId),
    queryFn: ({ signal }) => getProjectProposal(proposalId, signal),
  });
}

/** 套用之後：換成伺服器回的新狀態，並讓專案清單失效（新專案要出現在側欄與 ⌘K）；不要：只換狀態。 */
export function useDecideProjectProposal(proposal: ProjectProposal) {
  const queryClient = useQueryClient();
  const settle = (decided: ProjectProposal) => {
    queryClient.setQueryData(queryKeys.projectProposal(proposal.id), decided);
  };
  const apply = useMutation({
    mutationFn: () => applyProjectProposal(proposal.id),
    onSuccess: (decided) => {
      settle(decided);
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
    },
    onError: () => {
      // 已經處理過（別的分頁按了）：以伺服器現在的狀態為準；代號被用掉等失敗維持待處理，重讀也不會變。
      void queryClient.invalidateQueries({ queryKey: queryKeys.projectProposal(proposal.id) });
    },
  });
  const dismiss = useMutation({ mutationFn: () => dismissProjectProposal(proposal.id), onSuccess: settle });
  return { apply, dismiss };
}

const newItemProposalPath = (proposalId: string) => `/v1/ai/new-item-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.new_item_proposal_not_found：別人的、不存在的。 */
export function getNewItemProposal(proposalId: string, signal?: AbortSignal): Promise<NewItemProposal> {
  return apiRequest<NewItemProposal>(newItemProposalPath(proposalId), { signal });
}

/** 409 ai.new_item_proposal_closed；第一個項目就建立失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyNewItemProposal(proposalId: string): Promise<NewItemProposal> {
  return apiRequest<NewItemProposal>(`${newItemProposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.new_item_proposal_closed。 */
export function dismissNewItemProposal(proposalId: string): Promise<NewItemProposal> {
  return apiRequest<NewItemProposal>(`${newItemProposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

/** applying（別的分頁按了、正在建立）時每 2 秒重讀，直到不是 applying。 */
export function useNewItemProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.newItemProposal(proposalId),
    queryFn: ({ signal }) => getNewItemProposal(proposalId, signal),
    refetchInterval: pollWhileApplying,
  });
}

/**
 * 套用之後：換成伺服器回的新狀態，並讓這個專案的項目清單（子項目的清單也在裡面）、掛在底下的既有項目、父項目候選、
 * 助理的選項目、我的工作與專案清單（計數）失效。套用失敗（5xx、逾時、網路）時伺服器可能已經建立了一部分，也失效同一組。
 */
export function useDecideNewItemProposal(proposal: NewItemProposal) {
  const queryClient = useQueryClient();
  const settle = (decided: NewItemProposal) => {
    queryClient.setQueryData(queryKeys.newItemProposal(proposal.id), decided);
  };
  const invalidateCreated = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.items(proposal.projectKey) });
    const parentKeys = new Set(proposal.items.flatMap((item) => (item.parentKey === null ? [] : [item.parentKey])));
    parentKeys.forEach((parentKey) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.item(parentKey) });
    });
    void queryClient.invalidateQueries({ queryKey: queryKeys.parentCandidates(proposal.projectKey) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.assistantItemChoiceLists(proposal.projectKey) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
    void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
  };
  // 先加人再建項目：加人的那一步可能已經成功，成員與人員的快取一起失效。
  const invalidateMembers = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.project(proposal.projectKey), exact: true });
    void queryClient.invalidateQueries({ queryKey: queryKeys.people });
  };
  const apply = useMutation({
    mutationFn: () => applyNewItemProposal(proposal.id),
    onSuccess: (decided) => {
      settle(decided);
      invalidateCreated();
      if (proposal.addMembers.length > 0) invalidateMembers();
    },
    onError: () => {
      // 已經處理過（別的分頁按了）：以伺服器現在的狀態為準；第一個就失敗的維持待處理，重讀也不會變。
      void queryClient.invalidateQueries({ queryKey: queryKeys.newItemProposal(proposal.id) });
      invalidateCreated();
      if (proposal.addMembers.length > 0) invalidateMembers();
    },
  });
  const dismiss = useMutation({
    mutationFn: () => dismissNewItemProposal(proposal.id),
    onSuccess: settle,
    // 409（別的分頁已處理）：重讀，卡片才會從待處理變成實際的狀態。
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.newItemProposal(proposal.id) });
    },
  });
  return { apply, dismiss };
}

/**
 * 新增項目建議卡片要顯示的週期與里程碑名稱：和專案頁同一組快取，只有建議真的帶了才讀（沒帶時不多打請求）。
 * 讀失敗時 data 是 undefined，卡片只寫「週期」「里程碑」不帶名稱。
 */
export function useProposalPlanningNames(projectKey: string, needsCycles: boolean, needsMilestones: boolean) {
  const cycles = useQuery({
    queryKey: queryKeys.cycles(projectKey),
    queryFn: ({ signal }) => listCycles(projectKey, signal),
    enabled: needsCycles,
  });
  const milestones = useQuery({
    queryKey: queryKeys.milestones(projectKey),
    queryFn: ({ signal }) => listMilestones(projectKey, signal),
    enabled: needsMilestones,
  });
  return { cycles: cycles.data, milestones: milestones.data, cyclesFailed: cycles.isError, milestonesFailed: milestones.isError };
}
