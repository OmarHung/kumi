import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type RefObject } from 'react';
import { getPage, PAGE_CONFLICT, updatePage } from '../../../api/knowledge';
import { errorMessage, isApiError } from '../../../api/http';
import type { KnowledgePage, KnowledgePagePatch } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';
import type { OutdatedDraft } from '../../item-page/RestoredDraftNotice';
import { useRefocusAfterClose, useServerBasedDraft } from '../../item-page/descriptionDraft';
import { pageDraftKey, pageDraftOf, parsePageDraft, patchOf, serializePageDraft, type PageDraft } from './pageDraft';

const OUTDATED_DRAFT: OutdatedDraft = {
  message: '這一頁在你離開之後被別人改過；這是你上次沒送出的內容，儲存會蓋掉新的內容。',
  discardText: '看伺服器上的內容',
};
const EMPTY_TITLE_MESSAGE = '標題不能是空的。';

function isPageConflict(error: unknown): boolean {
  return isApiError(error) && error.status === 409 && error.code === PAGE_CONFLICT;
}

function detailOf(error: unknown, path: string): string | null {
  return isApiError(error) ? (error.details.find((detail) => detail.path === path)?.message ?? null) : null;
}

type DraftStore = ReturnType<typeof useServerBasedDraft>;

/**
 * 衝突的狀態（和項目說明同一套，docs/design/08-interaction.md 8.3「頁面的編輯與衝突」）：開始編輯時記下頁面的版本，
 * 編輯中重抓到的版本變了是 changed（提示，不打斷編輯）；儲存被伺服器擋下（409）是 conflict（自己的內容留在編輯器裡，由使用者選）。
 */
function useConflictState(page: KnowledgePage, stored: DraftStore, isEditing: boolean) {
  const [isRejected, setIsRejected] = useState(false);
  // 使用者按了「繼續編輯」的那一個版本：同一個版本不再提示，別人又改了才再提示。
  const [acknowledged, setAcknowledged] = useState<number | null>(null);
  // 還原的草稿已經過時（頁面在離開之後被改過）：提示已經說「儲存會蓋掉新的內容」，照提示蓋掉，送顯示提示那一刻的版本。
  const isOutdatedRestore = stored.outdated !== null;
  const [outdatedRevision, setOutdatedRevision] = useState<number | null>(null);
  if (isOutdatedRestore && outdatedRevision === null) setOutdatedRevision(page.revision);
  if (!isOutdatedRestore && outdatedRevision !== null) setOutdatedRevision(null);
  const startedAt = Number(stored.revision ?? page.revision);
  const isChanged = isEditing && !isOutdatedRestore && page.revision !== startedAt && page.revision !== acknowledged;
  return {
    conflict: isRejected ? ('conflict' as const) : isChanged ? ('changed' as const) : null,
    isRejected,
    expectedRevision: isOutdatedRestore ? (outdatedRevision ?? page.revision) : startedAt,
    reject: () => { setIsRejected(true); },
    clearRejected: () => { setIsRejected(false); },
    keepEditing: () => { setAcknowledged(page.revision); },
    reset: () => {
      setIsRejected(false);
      setAcknowledged(null);
    },
  };
}

type ConflictState = ReturnType<typeof useConflictState>;

interface EditContext {
  readonly page: KnowledgePage;
  readonly stored: DraftStore;
  readonly state: ConflictState;
  readonly draft: PageDraft | null;
  readonly setDraft: (draft: PageDraft | null) => void;
  readonly requestRefocus: (expectsContent: boolean) => void;
}

/** 送出與結束編輯。 */
function useSaveFlow({ page, stored, state, setDraft, requestRefocus }: EditContext) {
  const queryClient = useQueryClient();
  const spaceKey = page.space.key;
  const pageKey = queryKeys.knowledgePage(spaceKey, page.number);
  const [titleError, setTitleError] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: (patch: KnowledgePagePatch) => updatePage(spaceKey, page.number, patch),
    onSuccess: (saved) => {
      queryClient.setQueryData(pageKey, saved);
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeTree(spaceKey) });
    },
    onError: (error) => {
      if (!isPageConflict(error)) return;
      state.reject();
      // 讀到別人改成什麼：「看新的內容」「用我的版本覆蓋」都要用最新的。
      void queryClient.invalidateQueries({ queryKey: pageKey });
    },
  });

  function close() {
    requestRefocus(true);
    setDraft(null);
    stored.clear();
    mutation.reset();
    state.reset();
    setTitleError(null);
  }

  /** 相對於 base 有改的欄位才送；都沒改就直接結束編輯。 */
  function send(next: PageDraft, base: PageDraft, expectedRevision: number) {
    const patch = patchOf(next, base, expectedRevision);
    if (patch === null) {
      close();
      return;
    }
    mutation.mutate(patch, { onSuccess: close });
  }

  return { mutation, titleError, setTitleError, close, send };
}

type SaveFlow = ReturnType<typeof useSaveFlow>;

/** 衝突時的兩個選擇：看新的內容（捨棄自己的修改）、用我的版本覆蓋（用現在的版本再送一次）。 */
function useConflictResolution(context: EditContext, flow: SaveFlow) {
  const queryClient = useQueryClient();
  const { page, stored, state, draft } = context;
  const spaceKey = page.space.key;
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [isResolving, setIsResolving] = useState(false);

  // 被擋下來之後要看伺服器上現在的內容（繞過「30 秒內算最新」的快取），同時更新快取。
  const fetchLatest = () =>
    queryClient.query({ queryKey: queryKeys.knowledgePage(spaceKey, page.number), queryFn: ({ signal }) => getPage(spaceKey, page.number, signal), staleTime: 0 });

  async function resolve(kind: 'reload' | 'overwrite') {
    if (isResolving || draft === null || flow.mutation.isPending) return;
    setIsResolving(true);
    try {
      const latest = state.isRejected ? await fetchLatest() : page;
      const base = pageDraftOf(latest);
      stored.rebase({ text: serializePageDraft(base), revision: String(latest.revision) });
      state.clearRejected();
      setRefreshError(null);
      if (kind === 'overwrite') {
        flow.send(draft, base, latest.revision);
        return;
      }
      context.setDraft(base);
      stored.clear();
      flow.mutation.reset();
      state.reset();
    } catch (error) {
      setRefreshError(errorMessage(error));
    } finally {
      setIsResolving(false);
    }
  }

  return {
    isResolving,
    refreshError,
    clearRefreshError: () => { setRefreshError(null); },
    reload: () => resolve('reload'),
    overwrite: () => resolve('overwrite'),
  };
}

/** 儲存失敗要顯示在哪：標題的問題在標題下面，其他的在編輯器下面（內容的細項優先）。 */
function errorsOf(error: unknown, titleError: string | null, refreshError: string | null) {
  const titleDetail = detailOf(error, 'title');
  const message = error === null || titleDetail !== null ? null : (detailOf(error, 'body') ?? errorMessage(error));
  return { titleError: titleError ?? titleDetail, error: refreshError ?? message };
}

interface EditActionsContext {
  readonly current: PageDraft;
  readonly context: EditContext;
  readonly flow: SaveFlow;
  readonly resolution: ReturnType<typeof useConflictResolution>;
}

/** 編輯的動作：開始、打字、捨棄還原的草稿、儲存、取消。 */
function useEditActions({ current, context, flow, resolution }: EditActionsContext) {
  const { stored, state, draft, setDraft } = context;
  const { mutation } = flow;
  return {
    start: () => {
      mutation.reset();
      state.reset();
      resolution.clearRefreshError();
      stored.rebase();
      setDraft(current);
    },
    change: (next: Partial<PageDraft>) => {
      if (draft === null) return;
      const merged = { ...draft, ...next };
      setDraft(merged);
      stored.save(serializePageDraft(merged));
      flow.setTitleError(null);
      if (mutation.isError && !state.isRejected) mutation.reset();
    },
    // 捨棄還原的草稿：回到伺服器上的內容，留在編輯中。
    discard: () => {
      setDraft(current);
      stored.rebase();
      stored.clear();
    },
    save: () => {
      if (draft === null || mutation.isPending || resolution.isResolving) return;
      if (draft.title.trim() === '') {
        flow.setTitleError(EMPTY_TITLE_MESSAGE);
        return;
      }
      flow.send(draft, current, state.expectedRevision);
    },
    cancel: () => {
      if (!mutation.isPending && !resolution.isResolving) flow.close();
    },
  };
}

/**
 * 一頁的編輯狀態（draft 是 null 就是沒有在編輯）：草稿存在這個分頁、回到這一頁時還原並進入編輯，
 * 儲存成功、取消、沒改就儲存時清掉；結束編輯時焦點回到「編輯」。儲存時帶開始編輯時的版本（expectedRevision），
 * 別人先存了就是衝突：自己的內容留著，由使用者選「看新的內容」或「用我的版本覆蓋」。
 * startsEditing：剛新增的頁面直接進編輯（沒有草稿時）。
 */
export function usePageEditor(page: KnowledgePage, entryRef: RefObject<HTMLButtonElement | null>, startsEditing = false) {
  const current = pageDraftOf(page);
  const stored = useServerBasedDraft(pageDraftKey(page.space.key, page.number), serializePageDraft(current), String(page.revision));
  const [draft, setDraft] = useState<PageDraft | null>(() => parsePageDraft(stored.restored) ?? (startsEditing ? current : null));
  const requestRefocus = useRefocusAfterClose(draft === null ? null : '', true, entryRef);
  const state = useConflictState(page, stored, draft !== null);
  const context: EditContext = { page, stored, state, draft, setDraft, requestRefocus };
  const flow = useSaveFlow(context);
  const resolution = useConflictResolution(context, flow);
  const actions = useEditActions({ current, context, flow, resolution });
  const saveError = flow.mutation.isError && !state.isRejected ? flow.mutation.error : null;

  return {
    ...actions,
    draft,
    isSaving: flow.mutation.isPending,
    isResolving: resolution.isResolving,
    isRestored: stored.isRestored,
    outdated: stored.outdated === null ? null : OUTDATED_DRAFT,
    conflict: state.conflict,
    ...errorsOf(saveError, flow.titleError, resolution.refreshError),
    hasChanges: draft !== null && serializePageDraft(draft) !== serializePageDraft(current),
    reload: resolution.reload,
    overwrite: resolution.overwrite,
    keepEditing: state.keepEditing,
  };
}

export type PageEditor = ReturnType<typeof usePageEditor>;
