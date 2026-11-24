import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useRef, useState, type RefObject } from 'react';
import { getItem } from '../../api/items';
import { errorMessage, isApiError } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import type { ItemDetail } from '../../api/types';
import { draftKey } from '../../lib/draftStorage';
import { cellErrorMessage } from '../item-table/cells/EditableCell';
import { useItemCommand } from '../item-table/cells/useItemCommand';
import type { DescriptionConflict } from './DescriptionConflictNotice';
import { submitDescription, useRefocusAfterClose, useServerBasedDraft } from './descriptionDraft';

/** 後端的 WorkErrorCodes.ItemDescriptionConflict：儲存時說明已經被別人改過。 */
export const DESCRIPTION_CONFLICT_CODE = 'item.description_conflict';

function isDescriptionConflict(error: unknown): boolean {
  return isApiError(error) && error.status === 409 && error.code === DESCRIPTION_CONFLICT_CODE;
}

/** 伺服器上現在的項目（繞過「30 秒內算最新」的快取），同時更新快取。 */
function fetchLatest(queryClient: QueryClient, itemKey: string): Promise<ItemDetail> {
  return queryClient.query({ queryKey: queryKeys.item(itemKey), queryFn: ({ signal }) => getItem(itemKey, signal), staleTime: 0 });
}

type DraftStore = ReturnType<typeof useServerBasedDraft>;
type ItemCommand = ReturnType<typeof useItemCommand>;

/**
 * 衝突的狀態：開始編輯時記下說明的版本，編輯中重抓到的版本變了是 changed（提示，不打斷編輯），
 * 儲存被伺服器擋下（409）是 conflict（自己的內容留在編輯器裡，由使用者選看新的內容或蓋掉）。
 */
function useConflictState(item: ItemDetail, stored: DraftStore, isEditing: boolean) {
  const [isRejected, setIsRejected] = useState(false);
  // 使用者按了「繼續編輯」的那一個版本：同一個版本不再提示，別人又改了才再提示。
  const [acknowledgedRevision, setAcknowledgedRevision] = useState<string | null>(null);
  // 還原的草稿已經是過時的（說明在離開之後被改過）：提示已經說「儲存會蓋掉新的說明」，照提示蓋掉，送現在的版本，不再另外提示。
  const isOutdatedRestore = stored.outdated !== null;
  // 顯示過時提示那一刻的版本：記下來，不跟著之後的重抓換；之後別人又改了，儲存照樣會 409。
  const [outdatedRevision, setOutdatedRevision] = useState<string | null>(null);
  if (isOutdatedRestore && outdatedRevision === null) setOutdatedRevision(item.descriptionRevision);
  if (!isOutdatedRestore && outdatedRevision !== null) setOutdatedRevision(null);
  const isChanged =
    isEditing && !isOutdatedRestore && item.descriptionRevision !== stored.revision && item.descriptionRevision !== acknowledgedRevision;
  const conflict: DescriptionConflict | null = isRejected ? 'conflict' : isChanged ? 'changed' : null;
  return {
    conflict,
    isRejected,
    expectedRevision: isOutdatedRestore ? (outdatedRevision ?? item.descriptionRevision) : stored.revision,
    reject: () => { setIsRejected(true); },
    clearRejected: () => { setIsRejected(false); },
    keepEditing: () => { setAcknowledgedRevision(item.descriptionRevision); },
    reset: () => {
      setIsRejected(false);
      setAcknowledgedRevision(null);
    },
  };
}

interface ResolveContext {
  readonly item: ItemDetail;
  readonly stored: DraftStore;
  readonly command: ItemCommand;
  readonly draft: string | null;
  readonly isRejected: boolean;
  readonly setDraft: (text: string) => void;
  readonly onResolved: () => void;
  readonly send: (next: string | null, revision: string | undefined, done: (saved: string | null) => void) => void;
  readonly close: (saved: string | null) => void;
  readonly clearRejected: () => void;
}

/** 衝突時的兩個選擇：看新的內容（捨棄自己的修改）、用我的版本覆蓋（用現在的版本再送一次）。 */
function useConflictResolution(context: ResolveContext) {
  const queryClient = useQueryClient();
  const [refreshError, setRefreshError] = useState<string | null>(null);
  // 重抓與送出期間不能再按（連按不重送）：同步擋用 ref，畫面停用用 state。
  const inFlight = useRef(false);
  const [isResolving, setIsResolving] = useState(false);
  const { item, stored, command, draft } = context;

  function begin(): boolean {
    if (inFlight.current) return false;
    inFlight.current = true;
    setIsResolving(true);
    return true;
  }

  function end() {
    inFlight.current = false;
    setIsResolving(false);
  }

  async function reload() {
    if (!begin()) return;
    try {
      const latest = context.isRejected ? await fetchLatest(queryClient, item.key) : item;
      const text = latest.description ?? '';
      context.setDraft(text);
      stored.rebase({ text, revision: latest.descriptionRevision });
      stored.clear();
      command.reset();
      context.onResolved();
      setRefreshError(null);
    } catch (error) {
      setRefreshError(errorMessage(error));
    } finally {
      end();
    }
  }

  async function overwrite() {
    if (draft === null || command.isPending || !begin()) return;
    try {
      const latest = await fetchLatest(queryClient, item.key);
      stored.rebase({ text: latest.description ?? '', revision: latest.descriptionRevision });
      context.clearRejected();
      setRefreshError(null);
      submitDescription(draft, latest.description ?? null, (next, done) => { context.send(next, latest.descriptionRevision, done); }, context.close);
    } catch (error) {
      setRefreshError(errorMessage(error));
    } finally {
      end();
    }
  }

  return { reload, overwrite, isResolving, refreshError, clearRefreshError: () => { setRefreshError(null); } };
}

interface SaveFlowContext {
  readonly item: ItemDetail;
  readonly stored: DraftStore;
  readonly command: ItemCommand;
  readonly state: ReturnType<typeof useConflictState>;
  readonly draft: string | null;
  readonly setDraft: (text: string | null) => void;
  readonly requestRefocus: (expectsDescription: boolean) => void;
}

/** 送出與結束編輯。 */
function useSaveFlow({ item, stored, command, state, draft, setDraft, requestRefocus }: SaveFlowContext) {
  const queryClient = useQueryClient();
  const current = item.description ?? null;

  /** saved：結束之後的說明（取消、沒改是現在的內容，存好是伺服器回來的內容），決定焦點回到哪一個入口。 */
  function close(saved: string | null) {
    requestRefocus(saved !== null);
    setDraft(null);
    stored.clear();
    command.reset();
    state.reset();
  }

  function send(next: string | null, revision: string | undefined, done: (saved: string | null) => void) {
    command.mutate(
      { kind: 'patch', patch: { description: next, expectedDescriptionRevision: revision } },
      {
        onSuccess: (updated) => {
          done(updated.description);
        },
        onError: (error) => {
          if (!isDescriptionConflict(error)) return;
          state.reject();
          // 讀到別人改成什麼：「看新的內容」「用我的版本覆蓋」都要用最新的。
          void queryClient.invalidateQueries({ queryKey: queryKeys.item(item.key) });
        },
      },
    );
  }

  function save() {
    if (draft === null || command.isPending) return;
    submitDescription(draft, current, (next, done) => { send(next, state.expectedRevision, done); }, close);
  }

  return { close, send, save, cancel: () => { if (!command.isPending) close(current); } };
}

/**
 * 編輯中的草稿（null 是沒有在編輯）與送出。結束編輯時焦點回到入口（「編輯說明」或「新增說明」），鍵盤才接得下去。
 * 草稿只在編輯中、而且和伺服器上的內容不同時才存進這個分頁；回到這個項目時還原並進入編輯。
 * 儲存成功、取消（Esc）、沒改就儲存時清掉。儲存時帶開始編輯時的說明版本（衝突的處理見上面）。
 */
export function useDescriptionEditor(item: ItemDetail, entryRef: RefObject<HTMLButtonElement | null>) {
  const command = useItemCommand(item.key);
  const current = item.description ?? null;
  const stored = useServerBasedDraft(draftKey('description', item.key), item.description ?? '', item.descriptionRevision);
  const [draft, setDraft] = useState<string | null>(stored.restored);
  const requestRefocus = useRefocusAfterClose(draft, current !== null, entryRef);
  const state = useConflictState(item, stored, draft !== null);
  const flow = useSaveFlow({ item, stored, command, state, draft, setDraft, requestRefocus });
  const resolution = useConflictResolution({
    item, stored, command, draft, setDraft,
    send: flow.send,
    close: flow.close,
    isRejected: state.isRejected,
    onResolved: state.reset,
    clearRejected: state.clearRejected,
  });

  const commandError = command.isError && !state.isRejected ? cellErrorMessage(command.error, ['description']) : null;
  return {
    draft,
    isSaving: command.isPending,
    isResolving: resolution.isResolving,
    isRestored: stored.isRestored,
    outdated: stored.outdated,
    conflict: state.conflict,
    error: resolution.refreshError ?? commandError,
    start: () => {
      command.reset();
      state.reset();
      resolution.clearRefreshError();
      stored.rebase();
      setDraft(current ?? '');
    },
    change: (next: string) => {
      setDraft(next);
      stored.save(next);
      if (command.isError && !state.isRejected) command.reset();
    },
    // 捨棄還原的草稿：回到伺服器上的內容，留在編輯中（說明不是留言，清成空白再存就是把說明清掉）。
    discard: () => {
      setDraft(current ?? '');
      stored.rebase();
      stored.clear();
    },
    reload: resolution.reload,
    overwrite: resolution.overwrite,
    keepEditing: state.keepEditing,
    save: flow.save,
    cancel: flow.cancel,
  };
}

export type DescriptionEditor = ReturnType<typeof useDescriptionEditor>;
