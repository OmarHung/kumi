import { useEffect, useLayoutEffect, useRef, useState, type Dispatch, type RefObject, type SetStateAction } from 'react';
import type { AssistantAttachment, AssistantAttachmentKind } from '../../api/aiTypes';
import { deleteAssistantAttachment, releaseAssistantAttachment, uploadAssistantAttachment } from '../../api/assistantAttachments';
import { errorMessage, isApiError } from '../../api/http';
import { guessKind, rejectionReason } from './attachmentRules';

/** uploading：傳送中。ready：伺服器收下了，等著送出。failed：上傳失敗（可以重試）。rejected：前端先擋下（不上傳，只能移除）。 */
export type AttachmentStatus = 'uploading' | 'ready' | 'failed' | 'rejected';

export interface AttachmentEntry {
  readonly id: string;
  readonly file: File;
  readonly status: AttachmentStatus;
  /** 上傳成功前依類型與副檔名猜的；成功之後以伺服器看內容判斷的為準。 */
  readonly kind: AssistantAttachmentKind | null;
  readonly attachment: AssistantAttachment | null;
  /** failed、rejected 時給人看的原因。 */
  readonly message: string | null;
  /** failed 時重試有沒有意義：只有網路錯誤、5xx、429（再試可能成功）；類型不收、檔名不合法、太大這類永遠不會成功，只能移除。 */
  readonly canRetry: boolean;
}

const NEW_KEY = '';

interface State {
  readonly key: string;
  readonly byKey: Readonly<Record<string, readonly AttachmentEntry[]>>;
}

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

const HTTP_TOO_MANY_REQUESTS = 429;
const HTTP_SERVER_ERROR = 500;

/** 再試可能成功的失敗：連不上（status 0）、429、5xx。 */
function isRetryable(error: unknown): boolean {
  return isApiError(error) && (error.status === 0 || error.status === HTTP_TOO_MANY_REQUESTS || error.status >= HTTP_SERVER_ERROR);
}

/** 換了對話：從「新的對話」送出而開出來的那一則，小卡跟著過去（和草稿的字一樣）；其他的各自一份。 */
function switchedTo(state: State, key: string, createdId: string | null): State {
  const carried = state.key === NEW_KEY && key === createdId ? (state.byKey[NEW_KEY] ?? []) : null;
  if (carried === null || carried.length === 0) return { ...state, key };
  return { key, byKey: { ...state.byKey, [NEW_KEY]: [], [key]: [...(state.byKey[key] ?? []), ...carried] } };
}

function mapEntries(state: State, change: (entries: readonly AttachmentEntry[]) => readonly AttachmentEntry[]): State {
  const byKey: Record<string, readonly AttachmentEntry[]> = {};
  for (const [name, entries] of Object.entries(state.byKey)) byKey[name] = change(entries);
  return { ...state, byKey };
}

const NO_ENTRIES: readonly AttachmentEntry[] = [];

function patched(state: State, id: string, patch: Partial<AttachmentEntry>): State {
  return mapEntries(state, (entries) => entries.map((entry) => (entry.id === id ? { ...entry, ...patch } : entry)));
}

export interface ComposerAttachments {
  readonly entries: readonly AttachmentEntry[];
  /** 選檔、拖進來、貼上：先擋數量與大小，其他的立刻開始上傳。 */
  readonly add: (files: readonly File[]) => void;
  /** 移除一張小卡：上傳中的中止、已經上傳的呼叫 DELETE（失敗不擋）。 */
  readonly remove: (id: string) => void;
  readonly retry: (id: string) => void;
  /** 伺服器收下了：這些小卡拿掉（檔案已經綁在訊息上，不能 DELETE）。 */
  readonly clearSent: (ids: readonly string[]) => void;
}

type SetState = Dispatch<SetStateAction<State>>;

/** 新選的檔案變成小卡：依序檢查數量與大小（算進已經接受的），被擋下的不上傳。 */
function buildEntries(files: readonly File[], existing: readonly AttachmentEntry[], nextId: () => string): readonly AttachmentEntry[] {
  const accepted = existing.filter((entry) => entry.status !== 'rejected');
  let count = accepted.length;
  let bytes = accepted.reduce((sum, entry) => sum + entry.file.size, 0);
  return files.map<AttachmentEntry>((file) => {
    const reason = rejectionReason(file, count, bytes);
    if (reason === null) {
      count += 1;
      bytes += file.size;
    }
    return { id: nextId(), file, status: reason === null ? 'uploading' : 'rejected', kind: guessKind(file), attachment: null, message: reason, canRetry: false };
  });
}

/**
 * 一個檔案的上傳：進行中的請求記在 controllers（移除時中止），結果寫回那一張小卡。
 * 輸入框已經卸載才拿到回應（伺服器其實收下了）：馬上刪掉，不佔著「待送最多 5 個」。
 */
function useUpload(projectKey: string, setState: SetState, controllers: RefObject<Map<string, AbortController>>, mountedRef: RefObject<boolean>) {
  return (entry: AttachmentEntry) => {
    const controller = new AbortController();
    controllers.current.set(entry.id, controller);
    setState((current) => patched(current, entry.id, { status: 'uploading', message: null, canRetry: false }));
    uploadAssistantAttachment(projectKey, entry.file, { signal: controller.signal })
      .then((attachment) => {
        controllers.current.delete(entry.id);
        if (!mountedRef.current) {
          releaseAssistantAttachment(projectKey, attachment.id);
          return;
        }
        setState((current) => patched(current, entry.id, { status: 'ready', attachment, kind: attachment.kind }));
      })
      .catch((error: unknown) => {
        controllers.current.delete(entry.id);
        if (isAbort(error) || !mountedRef.current) return;
        setState((current) => patched(current, entry.id, { status: 'failed', message: errorMessage(error), canRetry: isRetryable(error) }));
      });
  };
}

/**
 * 離開輸入框（卸載：站內換頁、換專案；或整頁離開的 pagehide）：已上傳還沒送出的附件盡力刪掉（keepalive），不佔伺服器「待送最多 20 個」。
 * 還在傳的不中止：SPA 換頁時請求會繼續，拿到回應再刪（useUpload）。只是換對話不算離開，草稿與小卡照舊保留；
 * 關掉浮動面板也不卸載（面板只是藏起來），草稿與小卡一樣留著。
 */
function useReleaseOnUnmount(projectKey: string, state: State, isSending: boolean): RefObject<boolean> {
  const mountedRef = useRef(true);
  const latest = useRef({ projectKey, state, isSending });
  // 和畫面同一次提交就更新：小卡一出現，離開（pagehide、卸載）時就要刪得到它，不能等被動的 effect 晚一步跑。
  useLayoutEffect(() => {
    latest.current = { projectKey, state, isSending };
  });
  useEffect(() => {
    mountedRef.current = true;
    const releaseAll = () => {
      const { projectKey: key, state: last, isSending: isInFlight } = latest.current;
      // 送出中離開：訊息的請求還帶著這些編號（串流在 AppShell，換頁不中斷），刪了會讓那一則送不出去。
      if (isInFlight) return;
      Object.values(last.byKey).forEach((entries) => {
        entries.forEach((entry) => {
          if (entry.attachment !== null) releaseAssistantAttachment(key, entry.attachment.id);
        });
      });
    };
    // 重新整理、關掉分頁、離開網站時 React 不會卸載元件，只有 pagehide；放進往返快取的頁面（persisted）之後會原樣回來，不刪。
    const handlePageHide = (event: PageTransitionEvent) => {
      if (!event.persisted) releaseAll();
    };
    window.addEventListener('pagehide', handlePageHide);
    return () => {
      window.removeEventListener('pagehide', handlePageHide);
      mountedRef.current = false;
      releaseAll();
    };
  }, []);
  return mountedRef;
}

/**
 * 輸入框的附件小卡，每個對話一份（和草稿的字一起分開，從「新的對話」送出時跟著過去）。
 * 選了就立刻上傳（先上傳、送出時只帶編號）。
 */
export function useComposerAttachments(projectKey: string, conversationId: string | undefined, createdId: string | null, isSending: boolean): ComposerAttachments {
  const key = conversationId ?? NEW_KEY;
  const [state, setState] = useState<State>({ key, byKey: {} });
  if (state.key !== key) setState(switchedTo(state, key, createdId));
  const entries = state.byKey[key] ?? NO_ENTRIES;

  const controllers = useRef(new Map<string, AbortController>());
  const nextId = useRef(0);
  const mountedRef = useReleaseOnUnmount(projectKey, state, isSending);
  const upload = useUpload(projectKey, setState, controllers, mountedRef);

  const add = (files: readonly File[]) => {
    const added = buildEntries(files, entries, () => `attachment-${String((nextId.current += 1))}`);
    if (added.length === 0) return;
    setState((current) => ({ ...current, byKey: { ...current.byKey, [current.key]: [...(current.byKey[current.key] ?? NO_ENTRIES), ...added] } }));
    added.filter((entry) => entry.status === 'uploading').forEach(upload);
  };

  const forget = (ids: readonly string[]) => {
    for (const id of ids) {
      controllers.current.get(id)?.abort();
      controllers.current.delete(id);
    }
    setState((current) => mapEntries(current, (list) => list.filter((entry) => !ids.includes(entry.id))));
  };

  const remove = (id: string) => {
    const entry = entries.find((candidate) => candidate.id === id);
    // 移除失敗不擋：伺服器上待送的附件 24 小時後自己清掉。
    if (entry?.attachment != null) deleteAssistantAttachment(projectKey, entry.attachment.id).catch(() => undefined);
    forget([id]);
  };

  const retry = (id: string) => {
    const entry = entries.find((candidate) => candidate.id === id);
    if (entry?.status === 'failed') upload(entry);
  };

  return { entries, add, remove, retry, clearSent: forget };
}
